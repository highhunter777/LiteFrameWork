using TMPro;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiteFramework.UI
{
    /// <summary>
    /// 确认/警告对话框控件：Configure 后由 UI 服务装配按表行打开；按钮回调一次性（弹起即清）。
    /// </summary>
    public class UIDialog : MonoBehaviour
    {
        public TMP_Text Title;
        public TMP_Text Message;
        public Button OkButton;
        public Button CancelButton;

        private Action _onOk, _onCancel;
        private UniTaskCompletionSource<bool> _waiter;

        /// <summary>装配文案与回调（回调一次性：触发后自动清空）。</summary>
        public void Configure(string title, string message, Action onOk, Action onCancel = null)
        {
            SetText(Title, title);
            SetText(Message, message);
            _onOk = onOk;
            _onCancel = onCancel;
            _waiter = null;                                   // 直接回调模式与等待式互斥：Configure 重置等待源

            if (OkButton != null)
            {
                OkButton.onClick.RemoveAllListeners();
                OkButton.onClick.AddListener(() => { var cb = _onOk; _onOk = _onCancel = null; cb?.Invoke(); });
            }
            if (CancelButton != null)
            {
                CancelButton.onClick.RemoveAllListeners();
                CancelButton.onClick.AddListener(() => { var cb = _onCancel; _onOk = _onCancel = null; cb?.Invoke(); });
            }
        }

        /// <summary>
        /// 等待式确认：装配文案并返回结果任务（true = Ok / false = Cancel 或被外部收口）。
        /// </summary>
        public UniTask<bool> WaitAsync(string title, string message, string okText = null, string cancelText = null)
        {
            Configure(title, message, () => _waiter?.TrySetResult(true), () => _waiter?.TrySetResult(false));
            SetButtonLabel(OkButton, okText);
            SetButtonLabel(CancelButton, cancelText);
            _waiter = new UniTaskCompletionSource<bool>();    // Configure 会重置等待源（两模式互斥）——等待源最后武装
            return _waiter.Task;
        }

        /// <summary>外部收口（对话框被非按钮路径关闭）：结果落 false（Canceled/Closed 语义由上层判定）。幂等。</summary>
        public bool SettleExternally()
        {
            _onOk = _onCancel = null;                         // 一次性：外部收口后按钮不触发
            return _waiter != null && _waiter.TrySetResult(false);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null || string.IsNullOrEmpty(label)) return;
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
        }
    }
}
