using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using LiteGame.UI;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 模态输入恢复 Player 探针（准入项「UI 与表现可组合」的"模态输入恢复 Player 段"证据）。
    ///
    /// 触发：命令行含 <c>-inputmodal</c> 才运行（smoke：player-smoke.ps1 -InputModal）——正常启动
    /// 零影响（不查参数即无操作，同 <see cref="MemoryLoopProbe"/> 形态）。
    ///
    /// 链路（全部生产代码，探针只在设备边界注入已知意图）：
    /// 装配根登记的产品级拦截源 <c>ui.modal</c>（<c>ContainerModule</c>：模态栈打开即拦）→
    /// <see cref="IInputService"/> 上下文门（拦下 = 待用意图写全零并照常置已采样——本地/上行/权威
    /// 三处同读"这一帧没有战斗输入"）→ 模态关闭后<b>首个采样帧</b>即恢复设备实时意图
    /// （采样先于预测，不凭空多零帧）。设备边界用恒定意图源（前进 + 开火按住）经
    /// <see cref="IInputService.SetSource"/> 注入——与对局挂设备源同一 API；无头 Player 无真实按键，
    /// 恒定意图让"拦下/放行"可分辨。
    ///
    /// 模态对象是<b>真实窗体</b>：按 prefab 路径从 tbuiform 解析 id（不硬编码），按生产约定
    /// "弹窗按模态登记"（<see cref="UIService.RegisterModal"/>）后经 <see cref="UIService.ShowAsync"/>
    /// 真实打开（真 prefab + 真转场），关闭走 <see cref="UIService.CloseAsync"/>。
    ///
    /// 证据行（smoke 按行断言）：<c>[InputModal] baseline ok …</c> / <c>[InputModal] modal ok …</c> /
    /// <c>[InputModal] recovered ok …</c> / <c>[InputModal] done</c>；失败行
    /// <c>[InputModal] fail &lt;reason&gt;</c> 携带当帧状态数据（一次跑拿到全部诊断）。
    /// </summary>
    public static class InputModalProbe
    {
        private const string Arg = "-inputmodal";
        private const string ModalFormPrefab = "UI/Screens/BaselineB.prefab";
        private const int FramesPerPhase = 8;
        private const float ReadyTimeoutSec = 180f;
        private const float RetryIntervalSec = 1f;

        /// <summary>武装探针（命令行未带 <c>-inputmodal</c> 时无操作，返回 false）。</summary>
        public static bool TryArm(ClientHost host)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Arg) < 0) return false;
            RunAsync(host).Forget();
            return true;
        }

        /// <summary>恒定意图源：前进 + 开火按住（分量长度 ≤ 1 由本源保证）。设备边界的测试替身——
        /// 让被拦/放行在无头 Player 里可分辨；门/采样/恢复语义全部走生产代码。</summary>
        private sealed class HeldIntentSource : IIntentSource
        {
            public string Name => "probe.held";

            public IntentSample Sample(in SimVector3 localPos)
            {
                return new IntentSample(new SimInputFrame
                {
                    MoveZ = 1f,
                    AimPointX = 1f,
                    Buttons = SimInputFrame.ButtonFire,
                });
            }
        }

        /// <summary>一段的逐帧结论（证据行与断言共用一份口径）。</summary>
        private readonly struct PhaseResult
        {
            public readonly bool Passed;
            public readonly string BlockerName;
            public readonly float MoveZ;
            public readonly bool Fire;
            public readonly long DisposedDelta;

            public PhaseResult(bool passed, string blockerName, float moveZ, bool fire, long disposedDelta)
            {
                Passed = passed;
                BlockerName = blockerName;
                MoveZ = moveZ;
                Fire = fire;
                DisposedDelta = disposedDelta;
            }
        }

        private static async UniTaskVoid RunAsync(ClientHost host)
        {
            IInputService input = null;
            IIntentSource originalSource = null;
            try
            {
                input = host.Product<IInputService>();
                var ui = host.Product<UIService>();
                var config = host.Product<ConfigService>();

                originalSource = input.Source;
                input.SetSource(new HeldIntentSource());

                // ── 基线段：无模态 → 不拦，恒定意图逐帧可见（输入服务 Root 级，引导后即可驱动）──
                PhaseResult baseline = await PhaseAsync(input, "baseline", expectBlocked: false);
                if (!baseline.Passed)
                {
                    Debug.Log($"[InputModal] baseline frames={FramesPerPhase} " +
                              $"by={baseline.BlockerName} move={baseline.MoveZ:F2} fire={(baseline.Fire ? 1 : 0)}");
                    return;
                }
                Debug.Log($"[InputModal] baseline ok frames={FramesPerPhase} blocked=0 " +
                          $"move={baseline.MoveZ:F2} fire={(baseline.Fire ? 1 : 0)}");

                // ── 模态段：真实窗体按生产约定登记为模态并打开——表与 prefab 都经内容租约
                //    （ConfigService 惰性装载、AssetService 在 Patch 初始化），未就绪按间隔重试直至超时 ──
                int formId = 0;
                float deadline = Time.realtimeSinceStartup + ReadyTimeoutSec;
                int attempts = 0;
                while (true)
                {
                    attempts++;
                    try
                    {
                        if (formId <= 0)
                        {
                            foreach (var row in config.Tables.Tbuiform.DataList)
                                if (row.Prefab == ModalFormPrefab) { formId = row.Id; break; }
                            if (formId <= 0)
                            {
                                Fail($"form-not-found prefab={ModalFormPrefab}——核对 tbuiform 行");
                                return;
                            }
                        }
                        ui.RegisterModal(formId);
                        await ui.ShowAsync(formId);
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* 表/内容未就绪（代次未激活等）：重试 */ }
                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Fail($"modal-open-timeout attempts={attempts} form={formId}");
                        return;
                    }
                    await UniTask.Delay(TimeSpan.FromSeconds(RetryIntervalSec));
                }
                Debug.Log($"[InputModal] modal-open form={formId} isOpen={ui.IsOpen(formId)} " +
                          $"topModal={ui.TopModalId} blockers={input.BlockerCount}");
                if (!ui.IsOpen(formId) || ui.TopModalId != formId)
                {
                    Fail($"modal-open-state form={formId} isOpen={ui.IsOpen(formId)} top={ui.TopModalId}");
                    return;
                }

                PhaseResult modal = await PhaseAsync(input, "modal", expectBlocked: true);
                if (!modal.Passed)
                {
                    Debug.Log($"[InputModal] modal frames={FramesPerPhase} " +
                              $"by={modal.BlockerName} move={modal.MoveZ:F2} fire={(modal.Fire ? 1 : 0)}");
                    return;
                }
                Debug.Log($"[InputModal] modal ok frames={FramesPerPhase} by={modal.BlockerName} " +
                          $"zero=1 disposed=+{modal.DisposedDelta}");

                // ── 恢复段：关闭模态 → 首个采样帧即恢复恒定意图（无幻影零帧）──
                await ui.CloseAsync(formId);
                ui.UnregisterModal(formId);
                Debug.Log($"[InputModal] modal-close form={formId} isOpen={ui.IsOpen(formId)} modal={ui.IsModalOpen}");

                PhaseResult recovered = await PhaseAsync(input, "recovered", expectBlocked: false);
                if (!recovered.Passed)
                {
                    Debug.Log($"[InputModal] recovered frames={FramesPerPhase} " +
                              $"by={recovered.BlockerName} move={recovered.MoveZ:F2} fire={(recovered.Fire ? 1 : 0)}");
                    return;
                }
                Debug.Log($"[InputModal] recovered ok frames={FramesPerPhase} blocked=0 " +
                          $"move={recovered.MoveZ:F2} fire={(recovered.Fire ? 1 : 0)} firstFrameZero=0");

                Debug.Log($"[InputModal] done form={formId} frames={FramesPerPhase} sourceRestored=1");
                Application.Quit();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Fail($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (input != null && originalSource != null) input.SetSource(originalSource);
            }
        }

        /// <summary>
        /// 逐帧采样并断言整段一致（失败行携带当帧状态数据后即 Fail 收口）。恢复段首帧的零值
        /// 按"幻影零帧"单列报错——采样先于预测，解拦后首帧应为设备实时意图。
        /// </summary>
        private static async UniTask<PhaseResult> PhaseAsync(IInputService input, string phase, bool expectBlocked)
        {
            long disposedBefore = input.SampleDiposedByGate;
            string blockerName = "-";
            float moveZ = -1f;
            bool fire = false;

            for (int i = 1; i <= FramesPerPhase; i++)
            {
                await UniTask.NextFrame();
                input.SampleOnRenderFrame(default);
                // 渲染帧收口（生产节奏：Sample → TryTakeForSend 清帧标记）——探针独立驱动时
                // 不补这一步会因守卫的"调用即开闸"语义隔帧采样，disposed 计数减半（伪影非缺陷）。
                input.TryTakeForSend(out _);
                SimInputFrame pending = input.Pending;
                blockerName = input.BlockedByName ?? "-";
                moveZ = pending.MoveZ;
                fire = (pending.Buttons & SimInputFrame.ButtonFire) != 0;

                if (expectBlocked)
                {
                    if (!input.IsBlocked || blockerName != "ui.modal"
                        || pending.MoveX != 0f || pending.MoveZ != 0f || pending.Buttons != 0u)
                    {
                        Fail($"{phase} frame={i} expected=blocked-by-ui.modal-zero got " +
                             $"blocked={input.IsBlocked} by={blockerName} " +
                             $"move=({pending.MoveX:F2},{pending.MoveZ:F2}) buttons={pending.Buttons}");
                        return new PhaseResult(false, blockerName, moveZ, fire,
                            input.SampleDiposedByGate - disposedBefore);
                    }
                }
                else
                {
                    if (input.IsBlocked || pending.MoveZ != 1f || !fire)
                    {
                        string reason = phase == "recovered" && i == 1
                            ? "——解拦后首帧应为设备实时意图（幻影零帧）"
                            : string.Empty;
                        Fail($"{phase} frame={i} expected=pass-through-held{reason} got " +
                             $"blocked={input.IsBlocked} by={blockerName} " +
                             $"move=({pending.MoveX:F2},{pending.MoveZ:F2}) buttons={pending.Buttons}");
                        return new PhaseResult(false, blockerName, moveZ, fire,
                            input.SampleDiposedByGate - disposedBefore);
                    }
                }
            }

            return new PhaseResult(true, blockerName, moveZ, fire,
                input.SampleDiposedByGate - disposedBefore);
        }

        private static void Fail(string reason)
        {
            Debug.Log($"[InputModal] fail {reason}");
            Application.Quit();
        }
    }
}
