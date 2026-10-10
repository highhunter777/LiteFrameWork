// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab
using TMPro;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>气泡：挂点旁的短命提示（带朝上小三角由美术补；灰盒=文本条）。
    /// 重复 Show 取消上一次等待（用 CTS 显式表达）。</summary>
    public class UIBubble : MonoBehaviour
    {
        public TMP_Text Label;

        private CancellationTokenSource _hideCts;

        /// <summary>在气泡控件上显示文本，duration 后自动隐藏（缺省＝<see cref="UiFxDefaults.BubbleDuration"/>）。</summary>
        public void Show(string text, float duration = UiFxDefaults.BubbleDuration)
        {
            if (Label != null) Label.text = text;
            gameObject.SetActive(true);

            _hideCts?.Cancel();
            _hideCts?.Dispose();
            _hideCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            HideAfterAsync(duration, _hideCts.Token).Forget();
        }

        private async UniTaskVoid HideAfterAsync(float seconds, CancellationToken ct)
        {
            // cancelImmediately: true —— 宿主销毁/被下一次 Show 取消时**立即**观测（默认要等下一个 player loop 刻度，
            // 编辑态/回收路径下会晚到，导致已销毁对象仍被 SetActive → MissingReferenceException）
            bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update, ct, cancelImmediately: true).SuppressCancellationThrow();
            if (canceled || this == null) return;            // 已销毁：`this == null` 走 Unity 重载（别用 ReferenceEquals）
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            _hideCts?.Cancel();
            _hideCts?.Dispose();
            _hideCts = null;
        }
    }
}
