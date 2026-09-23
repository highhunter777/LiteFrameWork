using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 错误流程：任何流程 Fail 后的落点。显示失败原因（C0-③：内建最小错误 UI，代码构建、不经资源包），
    /// 不自动重试不静默退出——启动链失败必须人工排查后重跑。
    /// 失败原因经 **payload** 传来（`ProcedureArgs.Error`）——取代已退休的 `ProcedureOwner.LastError`。
    /// 商业错误界面（重试/回滚/清缓存入口）按总设计 §7.2 ErrorRecovery 在 C1 落地。
    /// </summary>
    public sealed class ProcedureError : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>最小错误 UI 根名——幂等判定用（Error 流程可重入，不重复建 UI）。</summary>
        private const string ErrorUiRootName = "BootstrapErrorUI";

        /// <summary>Player 冒烟（scripts/player-smoke.ps1）与日志检索的固定标记。</summary>
        private const string SmokeMarker = "[BootstrapError]";

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.Error, ct).Forget();

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, Exception error, CancellationToken ct)
        {
            try
            {
                var message = error?.Message ?? "(payload 无 Error)";
                Log.Error($"启动失败，停机待查:{message}", "Procedure");
                ShowMinimalErrorUI(message);
                Log.Error($"{SmokeMarker} minimal error UI shown - see Player.log", "Procedure");
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception uiEx)
            {
                // 错误 UI 自身失败不能吞掉原始错误——两处都落日志（Player.log 冒烟断言仍可命中第一条）
                Log.Error($"{SmokeMarker} UI 创建失败:{uiEx.Message}", "Procedure");
            }
        }

        /// <summary>
        /// 引导期（流程机就绪前）的启动失败落点：内建最小错误 UI + 冒烟标记。
        /// GameEntry 宿主初始化失败时调用——确定错误态（§C1 退出条件宿主侧保证）；
        /// 重试/回滚/清缓存入口按 §7.2 ErrorRecovery 在 C1 后续落地。
        /// </summary>
        public static void ShowBootstrapError(string message)
        {
            try
            {
                Log.Error($"{SmokeMarker} bootstrap failed: {message}", "Bootstrap");
                ShowMinimalErrorUI(message);
            }
            catch (Exception uiEx)
            {
                // 错误 UI 自身失败不能吞掉原始错误——两处都落日志（Player.log 冒烟断言仍可命中第一条）
                Log.Error($"{SmokeMarker} UI 创建失败:{uiEx.Message}", "Bootstrap");
            }
        }

        /// <summary>
        /// 内建最小错误 UI：Canvas(Overlay) + 黑底 + 全屏文本。诊断用途，代码构建——
        /// 不经资源包（关键错误界面必须可从内置资源启动，总设计 §6.1）；
        /// 主行英文规避内置字体无 CJK 字形的限制，完整诊断以 Player.log 为准。
        /// </summary>
        private static void ShowMinimalErrorUI(string message)
        {
            if (GameObject.Find(ErrorUiRootName) != null) return;   // 幂等：Error 可重入

            var root = new GameObject(ErrorUiRootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var bg = new GameObject("Background", typeof(Image));
            bg.transform.SetParent(root.transform, false);
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = new Color(0.05f, 0.05f, 0.08f, 1f);
            bgImage.rectTransform.anchorMin = Vector2.zero;
            bgImage.rectTransform.anchorMax = Vector2.one;
            bgImage.rectTransform.offsetMin = Vector2.zero;
            bgImage.rectTransform.offsetMax = Vector2.zero;

            var textGo = new GameObject("Message", typeof(Text));
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = $"BOOT FAILED\n\n{message}\n\nSee Player.log for details.";
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(120, 120);
            text.rectTransform.offsetMax = new Vector2(-120, -120);
        }
    }
}
