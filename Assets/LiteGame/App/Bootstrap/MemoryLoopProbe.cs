using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteClient;
using UnityEngine;
using UnityEngine.Profiling;

namespace LiteGame
{
    /// <summary>
    /// Player 循环内存与驻留上限实测探针（准入项「资源与缓存稳定」的"内存趋势/Player 循环记录"）。
    ///
    /// 触发：dev 三宏下装配根武装 + 命令行含 <c>-memloop</c> 才运行（smoke：player-smoke.ps1 -MemLoop）
    /// ——正常启动零影响（不查参数即无操作）。
    ///
    /// 就绪判据 = 实体加载链真打通：入口 location 取 <see cref="ContentSampleAssets.EntryLocations"/>
    /// 单源（与入口健康探针同源——候选场景 checked=1 的那个），内容代次在 Patch 激活后才可用，
    /// 未就绪按间隔重试直至超时。首个成功 Show 计入第 1 循环。
    ///
    /// 循环体 = Show → Hide（生产链路：EntityService → PrefabLeaseCache → GameObjectPool；
    /// 第 2 循环起走池命中零加载）。每循环一行 <c>[MemLoop] cycle=…</c>（内存两指标 + 驻留三计数），
    /// 结束打 <c>[MemLoop] done</c> 后主动退出——smoke 侧按行断言：
    /// **驻留不增长**（active=0 / pooled=1 / held=1 恒定——池与租约的上限面没有随循环膨胀）
    /// 与**内存趋势有界**（末段均值 − 首段均值 ≤ 预算）。
    /// </summary>
    public static class MemoryLoopProbe
    {
        private const string Arg = "-memloop";
        private const int Cycles = 30;
        private const float ReadyTimeoutSec = 180f;
        private const float RetryIntervalSec = 0.5f;

        /// <summary>武装探针（命令行未带 <c>-memloop</c> 时无操作，返回 false）。</summary>
        public static bool TryArm(ServiceContainer container)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Arg) < 0) return false;
            RunAsync(container).Forget();
            return true;
        }

        private static async UniTaskVoid RunAsync(ServiceContainer container)
        {
            try
            {
                var entities = container.Resolve<EntityService>();
                var leases = container.Resolve<PrefabLeaseCache>();
                string location = ContentSampleAssets.EntryLocations[0];

                // 就绪段：首个成功 Show 即就绪（该次占用作为第 1 循环的借出）
                EntityHandle handle = null;
                int attempts = 0;
                float deadline = Time.realtimeSinceStartup + ReadyTimeoutSec;
                while (handle == null)
                {
                    attempts++;
                    try
                    {
                        handle = await entities.ShowAsync(location);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { handle = null; }        // 内容未就绪（代次未激活等）：重试
                    if (handle == null)
                    {
                        if (Time.realtimeSinceStartup > deadline)
                        {
                            Fail($"ready-timeout attempts={attempts} location={location}");
                            return;
                        }
                        await UniTask.Delay(TimeSpan.FromSeconds(RetryIntervalSec));
                    }
                }

                for (int i = 1; i <= Cycles; i++)
                {
                    if (i > 1)
                    {
                        try
                        {
                            handle = await entities.ShowAsync(location);   // 池命中：零加载直取
                        }
                        catch (Exception ex)
                        {
                            Fail($"cycle={i} {ex.GetType().Name}: {ex.Message}");
                            return;
                        }
                        if (handle == null)
                        {
                            Fail($"cycle={i} ShowAsync 返回 null（竞态表/关闭语义不应出现在探针路径）");
                            return;
                        }
                    }

                    entities.Hide(handle.Id);
                    handle = null;
                    Debug.Log($"[MemLoop] cycle={i} allocMB={MB(Profiler.GetTotalAllocatedMemoryLong()):F1} " +
                              $"monoMB={MB(Profiler.GetMonoUsedSizeLong()):F1} " +
                              $"active={entities.ActiveCount} pooled={entities.PooledTotal} held={leases.HeldLocations}");
                    await UniTask.NextFrame();
                }

                Debug.Log($"[MemLoop] done cycles={Cycles} allocMB={MB(Profiler.GetTotalAllocatedMemoryLong()):F1} " +
                          $"monoMB={MB(Profiler.GetMonoUsedSizeLong()):F1} " +
                          $"active={entities.ActiveCount} pooled={entities.PooledTotal} held={leases.HeldLocations}");
                Application.Quit();
            }
            catch (Exception ex)
            {
                Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Fail(string reason)
        {
            Debug.Log($"[MemLoop] fail {reason}");
            Application.Quit();
        }

        private static double MB(long bytes) => bytes / (1024.0 * 1024.0);
    }
}
