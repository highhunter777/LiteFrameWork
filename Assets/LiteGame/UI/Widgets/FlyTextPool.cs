using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>飘字池：屏幕坐标文本上飘淡出，池化复用（规格：池深 16——经 <see cref="ObjectPool{T}"/>
    /// 内核收编，《对象池专项设计》§5：复位动作映射进 onRelease 回调，maxIdle=PoolDepth 语义保留）。
    /// **项目红线：禁用原生协程**——帧循环用 UniTask.NextFrame；生命周期取消 = GetCancellationTokenOnDestroy。</summary>
    public class FlyTextPool : MonoBehaviour
    {
        public RectTransform Template;                       // 非激活模板（其上挂 Text）
        public float RiseDistance = 80f;
        public float Duration = 0.8f;
        public int PoolDepth = 16;

        private ObjectPool<TMP_Text> _pool;                  // 懒建：PoolDepth 序列化后首次取件时定型

        /// <summary>池中闲置件数（诊断面：归 PlayMode/夜间观察——归还路径走 UniTask.NextFrame）。</summary>
        public int PooledCount => _pool?.UnusedCount ?? 0;

        private ObjectPool<TMP_Text> Pool => _pool ??= new ObjectPool<TMP_Text>(
            create: () => Instantiate(Template, transform).GetComponent<TMP_Text>(),
            onGet: label => label.gameObject.SetActive(true),
            onRelease: label =>
            {
                label.gameObject.SetActive(false);
                var cg = GetCanvasGroup(label);
                if (cg != null) cg.alpha = 1f;               // 归还前复位（下次 Acquire 直接可用）
            },
            onDestroy: label => Destroy(label.gameObject),   // 池满（DropNewest）：归还侧满则销毁，语义保留
            maxIdle: PoolDepth);

        private readonly Dictionary<TMP_Text, CanvasGroup> _cgCache = new Dictionary<TMP_Text, CanvasGroup>();

        /// <summary>取实例的 CanvasGroup（**模板自带**，随 prefab 序列化进来）。
        /// 刻意**不在运行时 AddComponent**——运行期往实例上补组件会静默失效；组件随 prefab 序列化即规避整类风险。
        /// 缓存避免每帧 GetComponent。</summary>
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
            var label = Acquire();                           // 激活在池 onGet（原复位/激活口径映射进回调）
            label.transform.SetParent(transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchoredPosition = anchoredPosition;
            label.text = text;
            FlyAsync(rt, label, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private TMP_Text Acquire() => Pool.Acquire();

        private async UniTaskVoid FlyAsync(RectTransform rt, TMP_Text label, CancellationToken ct)
        {
            // 淡出走 CanvasGroup.alpha（§3.2）；位置走 transform（纯 transform，不触发 mesh 重建）。
            // 组件由模板自带 → 取不到（模板缺组件）时守卫语义 = **只跳过淡出**：alpha 写入逐处判空，
            // 位移与回收照常——缺组件不得中断飞行与归还（守卫不得进循环条件：含 cg 判空会在
            // 首帧退出，位移/回收全失效且不归还池）。
            var cg = GetCanvasGroup(label);
            if (cg != null) cg.alpha = 1f;
            var start = rt.anchoredPosition;
            float t = 0f;
            while (t < Duration)
            {
                if (this == null || label == null || rt == null) return;   // 宿主销毁：直接退出（不归还池）
                if (await UniTask.NextFrame(ct).SuppressCancellationThrow()) return;   // 取消（销毁/回收）：退出
                // ★ await 之后复检：销毁可能发生在等待期间
                if (this == null || label == null || rt == null) return;

                t += Time.unscaledDeltaTime;                 // UI 轨（unscaled：时停不停）
                float k = Mathf.Clamp01(t / Duration);
                if (cg != null) cg.alpha = 1f - k;
                rt.anchoredPosition = start + Vector2.up * (RiseDistance * k);
            }
            if (this == null || label == null) return;       // 正常收尾前的最后一道守卫
            Pool.Release(label);                             // 复位/停用/超限销毁由内核回调链驱动
        }
    }

}
