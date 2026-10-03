// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab
using TMPro;
using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>
    /// 轻提示承载控件（§6.1：**多条并存**）。
    ///
    /// 契约（§6.1"Toast 多条并存"）：多条提示同时存在、按序堆叠，每条各自计时独立消失，
    /// 不彼此替换、不重置他人计时；堆叠顺序与间距由**承载控件**定义（容器做纵向排布，
    /// 调用方只给文本）。条数上限超出按**最旧淘汰**并计数（§6.1"反馈可观察"）。
    ///
    /// 计时走 <see cref="Tick"/>（可泵）：EditMode 无 PlayerLoop，`UniTask.Delay` 类异步等待
    /// 会永久挂起——手动步进让编辑态用例可直接推进时间断言到期消失。驱动方按 UIClock 步进
    /// （<see cref="UiAnimationClock.Delta"/>，时停不停语义同既有 UI 动效）。
    ///
    /// 视觉取模板（§7 视觉单一来源）：条目实例化自 <see cref="Template"/>，本类不构建视觉。
    /// </summary>
    public class Toast : MonoBehaviour
    {
        private static Toast _instance;
        public static Toast Instance => _instance != null ? _instance : (_instance = FindObjectOfType<Toast>());

        /// <summary>提示条模板（非激活；含 Text 子节点）。由模板 prefab 接线，运行时不变更。</summary>
        [Tooltip("提示条模板（非激活；含 Text 子节点）")]
        public RectTransform Template;

        /// <summary>单条默认时长（秒）。<see cref="Show(string, float)"/> 可逐条覆盖。</summary>
        public float Duration = 2f;

        /// <summary>同时可见条数上限；超出淘汰最旧（§6.1"数量设上限"）。</summary>
        public int MaxVisible = 4;

        /// <summary>相邻条目间距（像素；容器负责排布）。</summary>
        public float Spacing = 8f;

        /// <summary>向上堆叠时的间距符号（+1 = 沿 +Y 生长；Y 向下为负的布局用 -1）。</summary>
        public int StackDirection = 1;

        private readonly List<Entry> _active = new List<Entry>(4);
        private ObjectPool<RectTransform> _pool;                // 懒建：MaxVisible 序列化后首次 Show 时定型
                                                                //（《对象池专项设计》§5：maxIdle=MaxVisible——
                                                                // 池侧装得下活跃峰值，Retire 复位进 onRelease）

        /// <summary>池中闲置条目数（诊断/测试面：随 Retire 增长、随 Show 复用回落）。</summary>
        public int PooledCount => _pool?.UnusedCount ?? 0;

        /// <summary>累计淘汰条数（诊断/测试面；§6.1"反馈可观察"）。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>当前可见条数。</summary>
        public int VisibleCount => _active.Count;

        /// <summary>最早一条的文本（诊断/测试面；无可见条 = ""）。</summary>
        public string FirstVisibleText
        {
            get
            {
                if (_active.Count == 0 || _active[0].Item == null) return "";
                TMP_Text label = _active[0].Item.GetComponentInChildren<TMP_Text>(true);
                return label != null ? label.text : "";
            }
        }

        private sealed class Entry
        {
            public RectTransform Item;
            public float Remain;
        }

        /// <summary>弹一条轻提示（用默认时长）。</summary>
        public void Show(string text) => Show(text, Duration);

        /// <summary>弹一条轻提示（指定时长秒数）。多条并存——已有提示不受影响，各自计时。</summary>
        public void Show(string text, float seconds)
        {
            if (Template == null)
            {
                Debug.LogError("[Toast] Template 未接线——提示无法显示（核对 Toast 模板 prefab 接线）", this);
                return;
            }

            RectTransform item = Pool.Acquire();                 // 取件即激活（onGet）；空池 Instantiate 模板
            TMP_Text label = item.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;

            _active.Add(new Entry { Item = item, Remain = seconds > 0f ? seconds : Duration });

            // 数量上限：淘汰最旧（先退出，本帧末统一 ApplyLayout）
            while (_active.Count > MaxVisible && _active.Count > 0)
            {
                Entry oldest = _active[0];
                _active.RemoveAt(0);
                Retire(oldest);
                DroppedCount++;
            }
            ApplyLayout();
        }

        /// <summary>
        /// 计时步进（由 <see cref="ToastTicker"/> 经 UIClock 驱动；测试可直接调用）。
        /// 无可见条目时零开销短路。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_active.Count == 0 || deltaTime <= 0f) return;

            bool expired = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                _active[i].Remain -= deltaTime;
                if (_active[i].Remain > 0f) continue;
                Retire(_active[i]);                       // 各自到时独立消失，不动他人计时
                _active.RemoveAt(i);
                expired = true;
            }
            if (expired) ApplyLayout();
        }

        private void Retire(Entry entry)
        {
            if (entry?.Item == null) return;
            Pool.Release(entry.Item);                           // 停用/超限销毁由内核回调链驱动
        }

        private ObjectPool<RectTransform> Pool => _pool ??= new ObjectPool<RectTransform>(
            create: () => Instantiate(Template, Template.parent),
            onGet: item => item.gameObject.SetActive(true),
            onRelease: item => item.gameObject.SetActive(false),
            onDestroy: item => Destroy(item.gameObject),
            maxIdle: MaxVisible);

        /// <summary>纵向堆叠排布：间隔由 Spacing/StackDirection 给定，顺序与创建序一致（旧在上）。</summary>
        private void ApplyLayout()
        {
            var step = new Vector2(0f, (Spacing + Template.rect.height) * (StackDirection >= 0 ? 1f : -1f));
            for (int i = 0; i < _active.Count; i++)
                _active[i].Item.anchoredPosition = step * i;
        }
    }

    /// <summary>
    /// Toast 计时驱动（容器注册即自动驱动；接 <see cref="UiAnimationClock"/> 的 UIClock 步进——
    /// 时停不停、暂停即停，与 UI 动效同轨）。
    /// **显式注入实例**：不做全局单例查找（服务只持有自己打开的 form 实例，§7）；
    /// 未持有（Toast 面从未打开）时零开销短路。
    /// </summary>
    public sealed class ToastTicker : LiteFramework.ITickable
    {
        private readonly Func<Toast> _resolve;

        /// <param name="resolve">取当前 Toast 承载实例（返回 null = 未打开，不驱动）。</param>
        public ToastTicker(Func<Toast> resolve)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        public void Tick(float realDelta)
        {
            Toast toast = _resolve();
            if (toast == null) return;
            toast.Tick(UiAnimationClock.Delta);
        }
    }
}
