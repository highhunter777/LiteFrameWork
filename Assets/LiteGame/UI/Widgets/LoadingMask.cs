using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 加载遮罩控件（2026-09-25 新增，U2 反馈面归位）：全屏阻断面 + 文案。
    ///
    /// 与 <see cref="UIDialog"/> 同一模式——**只暴露状态驱动，不自己创建视觉**（§7 视觉单一来源）：
    /// 遮罩图与文本由模板 prefab 接线，服务层（FeedbackService）持有实例后调 <see cref="SetMessage"/>。
    ///
    /// 阻断语义（§6.2"输入禁用、射线阻断和焦点是不同职责"）：阻断面靠 <see cref="Blocker"/> 的
    /// raycastTarget 吃掉射线，**不是**靠停组或禁页面——Loading 与 Toast 同层不互斥，
    /// 不能在 System 组内互相顶掉。
    /// </summary>
    public class LoadingMask : MonoBehaviour
    {
        /// <summary>全屏阻断面（模板接线；吃掉射线 = 阻断下层操作）。</summary>
        public Image Blocker;

        /// <summary>提示文案（"加载中…"等）。</summary>
        public TMP_Text Message;

        /// <summary>设置文案（null/空 = 保留模板原文案）。</summary>
        public void SetMessage(string message)
        {
            if (Message != null && !string.IsNullOrEmpty(message)) Message.text = message;
        }

        /// <summary>展示可选性与阻断开关（模板已接好；此接口供上层在隐藏/展示间切换而不销毁实例）。</summary>
        public void SetBlocking(bool blocking)
        {
            if (Blocker != null) Blocker.raycastTarget = blocking;
        }
    }
}
