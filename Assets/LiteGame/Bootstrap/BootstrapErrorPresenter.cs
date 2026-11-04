using System;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 引导期（流程机就绪前）的最小错误面：内建 UI（代码构建、不经资源包）+ 固定冒烟标记。
    /// AOT 侧落点——不依赖任何产品模块/组合根；<c>ProcedureError</c> 的流程级错误页复用本件，
    /// 保证“冷启动失败”与“运行期流程失败”看到同一张最小错误页。
    /// </summary>
    public static class BootstrapErrorPresenter
    {
        /// <summary>最小错误 UI 根名（幂等判定；Error 流程可重入）。</summary>
        public const string ErrorUIRootName = "BootstrapErrorUI";

        /// <summary>Player 冒烟（scripts/player-smoke.ps1）与日志检索的固定标记。</summary>
        public const string SmokeMarker = "[BootstrapError]";

        /// <summary>按异常分类展示（未标记 = Fatal）。</summary>
        public static void Show(Exception error)
            => Show(error?.Message ?? "(无异常)", ErrorRecoveryService.Classify(error));

        /// <summary>无异常对象时按 Fatal 展示。</summary>
        public static void Show(string message) => Show(message, ErrorKind.Fatal);

        /// <summary>分类 → 最小错误页（幂等；UI 自身失败只落日志，不吞原始错误）。</summary>
        public static void Show(string message, ErrorKind kind)
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
            if (GameObject.Find(ErrorUIRootName) != null) return;   // 幂等：Error 可重入

            var root = new GameObject(ErrorUIRootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
