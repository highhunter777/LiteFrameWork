using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 错误流程：任何流程 Fail 后的落点。显示失败原因（内建最小错误 UI，代码构建、不经资源包），
    /// **消费 <see cref="ErrorKind"/>** 决定提示语，并经 <see cref="ErrorRecoveryService"/> 执行终止动作
    /// （Security：导出诊断后阻断；Fatal：导出诊断并安全退出——编辑器内不退出）。
    /// 失败原因经 **payload** 传来（<c>ProcedureArgs.Error</c>）。
    /// </summary>
    public sealed class ProcedureError : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        /// <summary>最小错误 UI 根名——幂等判定用（Error 流程可重入，不重复建 UI）。</summary>
        private const string ErrorUiRootName = "BootstrapErrorUI";

        /// <summary>Player 冒烟（scripts/player-smoke.ps1）与日志检索的固定标记。</summary>
        private const string SmokeMarker = "[BootstrapError]";

        private readonly ErrorRecoveryService _recovery;

        public ProcedureError(CancellationToken rootToken = default, ErrorRecoveryService recovery = null)
            : base(rootToken)
        {
            _recovery = recovery;
        }

        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.Error, ct).ForgetLogged("ProcedureError");

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, Exception error, CancellationToken ct)
        {
            LaunchLoading.End();                     // 兜底：任何流程泄漏的启动走廊在此收口（错误 UI 不能被遮罩挡住）
            ErrorKind kind = ErrorRecoveryService.Classify(error);
            string message = error?.Message ?? "(payload 无 Error)";
            try
            {
                Log.Error($"启动失败[{kind}]，停机待查:{message}", LogTag.Procedure);
                ShowMinimalErrorUI(message, kind);
                Log.Error($"{SmokeMarker} minimal error UI shown - kind={kind} - see Player.log", LogTag.Procedure);

                if (_recovery == null) return;
                RecoveryOutcome outcome = await _recovery.HandleAsync(error, "bootstrap.error", null, ct);
                Log.Error($"{SmokeMarker} recovery result={outcome.Result} action={outcome.Action}", LogTag.Procedure);
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception recoveryEx)
            {
                Log.Error($"{SmokeMarker} 恢复动作失败:{recoveryEx.Message}", LogTag.Procedure);
            }
        }

        /// <summary>
        /// 引导期（流程机就绪前）的启动失败落点：内建最小错误 UI + 冒烟标记（无恢复执行器——宿主未就绪）。
        /// </summary>
        public static void ShowBootstrapError(string message)
            => ShowBootstrapError(message, ErrorKind.Fatal);

        /// <summary>同上，按异常分类展示（未标记 = Fatal）。</summary>
        public static void ShowBootstrapError(Exception error)
            => ShowBootstrapError(error?.Message ?? "(无异常)", ErrorRecoveryService.Classify(error));

        private static void ShowBootstrapError(string message, ErrorKind kind)
        {
            try
            {
                Log.Error($"{SmokeMarker} bootstrap failed [{kind}]: {message}", LogTag.Bootstrap);
                ShowMinimalErrorUI(message, kind);
            }
            catch (Exception uiEx)
            {
                // 错误 UI 自身失败不能吞掉原始错误——两处都落日志（Player.log 冒烟断言仍可命中第一条）
                Log.Error($"{SmokeMarker} UI 创建失败:{uiEx.Message}", LogTag.Bootstrap);
            }
        }

        /// <summary>分类 → 用户可见提示（错误页只消费 <see cref="ErrorKind"/>，不拼第二套枚举）。</summary>
        private static string HintFor(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.Transient: return "RETRYABLE - restart the client.";
                case ErrorKind.Recoverable: return "RECOVERABLE - restart after clearing cache.";
                case ErrorKind.Compatibility: return "UPDATE REQUIRED - please update the client.";
                case ErrorKind.Security: return "BLOCKED - security verification failed.";
                default: return "FATAL - diagnostics exported; see Player.log.";
            }
        }

        /// <summary>
        /// 内建最小错误 UI：Canvas(Overlay) + 黑底 + 全屏文本。诊断用途，代码构建——
        /// 不经资源包（关键错误界面必须可从内置资源启动，总设计 §6.1）；
        /// 主行英文规避内置字体无 CJK 字形的限制，完整诊断以 Player.log 为准。
        /// </summary>
        private static void ShowMinimalErrorUI(string message, ErrorKind kind)
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
            text.text = $"BOOT FAILED [{kind}]\n\n{message}\n\n{HintFor(kind)}\nSee Player.log for details.";
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(120, 120);
            text.rectTransform.offsetMax = new Vector2(-120, -120);
        }
    }
}
