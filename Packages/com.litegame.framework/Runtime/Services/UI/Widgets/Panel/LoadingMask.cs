using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 加载遮罩控件（反馈面）：全屏阻断面 + 文案。
    /// </summary>
    public class LoadingMask : MonoBehaviour
    {
        /// <summary>全屏阻断面（模板接线；吃掉射线 = 阻断下层操作）。</summary>
        public Image Blocker;

        /// <summary>提示文案（"加载中…"等）。</summary>
        public TMP_Text Message;

        /// <summary>进度条填充（Filled/Horizontal；模板接线——null 期间隐藏，"不定态"显隐随本次状态）。</summary>
        public Image ProgressFill;

        /// <summary>设置文案（null/空 = 保留模板原文案）。</summary>
        public void SetMessage(string message)
        {
            if (Message != null && !string.IsNullOrEmpty(message)) Message.text = message;
        }

        /// <summary>
        /// 写全量状态（《通用加载页设计》§3 复位纪律：文案 + 进度有无不留上次残留）。
        /// message null/空 = 回落"加载中…"；progress01 null = 不定态（进度条隐藏），有值 = 0..1 夹取显示。
        /// </summary>
        public void SetStatus(string message, float? progress01)
        {
            if (Message != null) Message.text = string.IsNullOrEmpty(message) ? "加载中…" : message;
            if (ProgressFill != null)
            {
                ProgressFill.gameObject.SetActive(progress01.HasValue);
                if (progress01.HasValue) ProgressFill.fillAmount = Mathf.Clamp01(progress01.Value);
            }
        }

        /// <summary>展示可选性与阻断开关（模板已接好；此接口供上层在隐藏/展示间切换而不销毁实例）。</summary>
        public void SetBlocking(bool blocking)
        {
            if (Blocker != null) Blocker.raycastTarget = blocking;
        }
    }
}
