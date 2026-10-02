using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>飘字池：屏幕坐标文本上飘淡出，池化复用（规格：池深 16）。
    /// **项目红线：禁用原生协程**——帧循环用 UniTask.NextFrame；生命周期取消 = GetCancellationTokenOnDestroy。</summary>
    public class FlyTextPool : MonoBehaviour
    {
        public RectTransform Template;                       // 非激活模板（其上挂 Text）
        public float RiseDistance = 80f;
        public float Duration = 0.8f;
        public int PoolDepth = 16;

        private readonly Stack<TMP_Text> _pool = new Stack<TMP_Text>(16);

        /// <summary>取实例的 CanvasGroup（**模板自带**，随 prefab 序列化进来）。
        /// 刻意**不在运行时 AddComponent**——运行期往实例上补组件会静默失效；组件随 prefab 序列化即规避整类风险。
        /// 缓存避免每帧 GetComponent。</summary>
        private readonly Dictionary<TMP_Text, CanvasGroup> _cgCache = new Dictionary<TMP_Text, CanvasGroup>();

        private CanvasGroup GetCanvasGroup(TMP_Text label)
        {
            if (_cgCache.TryGetValue(label, out var cg) && cg != null) return cg;
            cg = label.GetComponent<CanvasGroup>();
            _cgCache[label] = cg;      // 可能为 null（模板缺组件）→ 由调用方判空跳过淡出
            return cg;
        }

        /// <summary>在父画布的 anchoredPosition 处飘一条文本（向上滑 + 淡出）。</summary>
        public void Show(string text, Vector2 anchoredPosition)
        {
            var label = Acquire();
            label.transform.SetParent(transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchoredPosition = anchoredPosition;
            label.text = text;
            label.gameObject.SetActive(true);
            FlyAsync(rt, label, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private TMP_Text Acquire()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var item = Instantiate(Template, transform);
            return item.GetComponent<TMP_Text>();
        }

        private async UniTaskVoid FlyAsync(RectTransform rt, TMP_Text label, CancellationToken ct)
        {
            // 淡出走 CanvasGroup.alpha（§3.2）；位置走 transform（纯 transform，不触发 mesh 重建）。
            // 组件由模板自带 → 取不到（模板被改坏）时退化为"只位移不淡出"，不抛异常、不静默炸用例。
            var cg = GetCanvasGroup(label);
            if (cg != null) cg.alpha = 1f;
            var start = rt.anchoredPosition;
            float t = 0f;
            while (t < Duration)
            {
                if (this == null || label == null || rt == null || cg == null) return;   // 宿主销毁：直接退出（不归还池）
                if (await UniTask.NextFrame(ct).SuppressCancellationThrow()) return;   // 取消（销毁/回收）：退出
                // ★ await 之后复检：销毁可能发生在等待期间
                if (this == null || label == null || rt == null || cg == null) return;

                t += Time.unscaledDeltaTime;                 // UI 轨（unscaled：时停不停）
                float k = Mathf.Clamp01(t / Duration);
                cg.alpha = 1f - k;
                rt.anchoredPosition = start + Vector2.up * (RiseDistance * k);
            }
            if (this == null || label == null) return;       // 正常收尾前的最后一道守卫
            label.gameObject.SetActive(false);
            if (cg != null) cg.alpha = 1f;                   // 归还前复位（下次 Acquire 直接可用）
            if (_pool.Count < PoolDepth) _pool.Push(label);
            else Destroy(label.gameObject);
        }
    }

}
