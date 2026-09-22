using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>
    /// 虚拟列表（《UI框架总设计》§8.2 重写版）：**窗口复用**——节点数由"可见容量 + overscan"决定，
    /// 不随数据总量线性创建；滚动只做"窗口平移 + 索引重绑"，窗口外的节点收回、再滚回来重新入窗。
    ///
    /// 旧实现的三个问题（UI-07/UI-08）：① 节点数按数据量建；② 裁剪只 SetActive(false) 后
    /// `continue` 跳过——滚出去的条目再也回不来（列表只能滚一次）；③ 每次 Refresh 都 AddListener，
    /// 监听只增不减。本版逐条修正：有界节点池 + 索引窗口 + ScrollRect 监听只绑一次、禁用/销毁对称解绑。
    ///
    /// 用法：Template（渲染项模板，**保持非激活**）+ 本组件挂 Content；SetSource 后 Refresh。
    /// 父链上有 ScrollRect 时滚动自动重算窗口；无 ScrollRect 视为整表可见（不做裁剪）。
    /// 首版固定尺寸单轴（Grid/变高列表属 U4）。
    /// </summary>
    public class VirtualList : MonoBehaviour
    {
        public enum Axis { Vertical, Horizontal }

        [Tooltip("渲染项模板（必须保持非激活态）")]
        public RectTransform Template;
        public Axis Direction = Axis.Vertical;
        public float Spacing = 8f;
        [Tooltip("窗口外预建条数（上下各一份，抗快速滚动抖动）")]
        public int Overscan = 2;
        [Tooltip("数据量上限（安全阀）：超限**告警**并按上限渲染——不做静默截断")]
        public int HardCap = 512;

        private IVirtualListSource _source;
        private readonly List<RectTransform> _nodes = new List<RectTransform>(16);   // 已实例化节点（含空闲）
        private readonly List<int> _boundIndex = new List<int>(16);                  // 节点绑定的数据索引（-1 = 空闲）
        private readonly List<int> _free = new List<int>(16);                        // 空闲节点槽位（复用）
        private UnityEngine.UI.ScrollRect _scroll;
        private bool _dirty;
        private bool _cappedWarned;
        private float _contentSize;

        /// <summary>已实体化（激活并绑定数据）的节点数——虚拟化的直接读数（远小于数据总量）。</summary>
        public int RealizedCount { get; private set; }

        /// <summary>当前窗口首条数据索引（诊断/测试读数）。</summary>
        public int FirstIndex { get; private set; }

        /// <summary>当前数据量（HardCap 生效后的有效值）。</summary>
        public int DataCount { get; private set; }

        public void SetSource(IVirtualListSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Refresh();
        }

        /// <summary>数据变更后调用（增量：不重建节点，只重算窗口与总尺寸）。</summary>
        public void Refresh()
        {
            if (_source == null || Template == null) return;

            ResolveScroll();
            int n = _source.Count;
            if (n > HardCap)
            {
                if (!_cappedWarned)
                {
                    _cappedWarned = true;
                    Debug.LogWarning($"[VirtualList] 数据量 {n} 超过 HardCap {HardCap}——按上限渲染（真实业务请改分页或用无上限列表）", this);
                }
                n = HardCap;
            }
            else
            {
                _cappedWarned = false;
            }
            DataCount = n;

            _contentSize = TotalSize(n);
            ApplyContentSize(_contentSize);
            ApplyWindow();
        }

        /// <summary>手动重算窗口（无 ScrollRect 的宿主、测试与尺寸变化后调用）。</summary>
        public void RefreshWindow() => ApplyWindow();

        private void OnEnable()
        {
            ResolveScroll();
            SubscribeScroll(true);
            _dirty = true;                                  // 重新启用后按当前偏移重算（池化复用的宿主）
        }

        private void OnDisable() => SubscribeScroll(false);

        private void OnDestroy() => SubscribeScroll(false);

        private void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            ApplyWindow();
        }

        // ---- 内部：窗口计算与复用 ----

        /// <summary>
        /// 窗口重算（核心）：由 Content 偏移 + 视口尺寸 + 项尺寸与间距算首末索引，
        /// 收回窗口外节点、把窗口内未绑定的索引分配给空闲节点并重绑。
        /// </summary>
        private void ApplyWindow()
        {
            if (_source == null || Template == null) return;

            int count = DataCount;
            if (count <= 0)
            {
                ReleaseAll();
                RealizedCount = 0;
                FirstIndex = 0;
                return;
            }

            float step = Step();
            if (step <= 0f) return;                          // 模板尺寸未就绪（布局未跑）：等下次

            float viewport = ViewportExtent();
            int capacity = Mathf.CeilToInt(viewport / step) + 2 * Mathf.Max(0, Overscan) + 1;
            int first = Mathf.Clamp(Mathf.FloorToInt(Offset() / step) - Mathf.Max(0, Overscan), 0, count - 1);
            int realized = Mathf.Min(capacity, count - first);

            // 1) 保留仍在窗口内的节点，其余释放回空闲池（先解绑再置空闲：§8.2 重绑前解除旧回调）
            _free.Clear();
            for (int i = 0; i < _nodes.Count; i++)
            {
                int bound = _boundIndex[i];
                if (bound < first || bound >= first + realized)
                {
                    if (bound >= 0) Unbind(_nodes[i]);
                    _boundIndex[i] = -1;
                    _nodes[i].gameObject.SetActive(false);
                    _free.Add(i);
                }
            }

            // 2) 补建节点：池上限 = 窗口容量（**不随数据量增长**）
            while (_nodes.Count < realized)
            {
                var node = Instantiate(Template, Template.parent);
                node.gameObject.SetActive(false);
                _nodes.Add(node);
                _boundIndex.Add(-1);
                _free.Add(_nodes.Count - 1);
            }

            // 3) 为窗口内每个索引找座位：已绑该索引的原地不动，其余取空闲节点重绑
            FirstIndex = first;
            for (int index = first; index < first + realized; index++)
            {
                int slot = SlotOf(index);
                if (slot < 0)
                {
                    if (_free.Count == 0) continue;          // 理论上不会发生（容量已按 realized 备足）
                    slot = _free[_free.Count - 1];
                    _free.RemoveAt(_free.Count - 1);
                    var node = _nodes[slot];
                    node.anchoredPosition = PositionOf(index, step);
                    node.gameObject.SetActive(true);
                    _boundIndex[slot] = index;
                    _source.Bind(index, node);
                }
            }
            RealizedCount = realized;
        }

        /// <summary>索引 → 已绑节点槽位（-1 = 窗口内尚未绑定）。</summary>
        private int SlotOf(int index)
        {
            for (int i = 0; i < _boundIndex.Count; i++)
                if (_boundIndex[i] == index) return i;
            return -1;
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_boundIndex[i] >= 0) Unbind(_nodes[i]);
                _boundIndex[i] = -1;
                _nodes[i].gameObject.SetActive(false);
            }
        }

        private void Unbind(RectTransform node)
        {
            if (_source is IVirtualListUnbind unbinder) unbinder.Unbind(node);   // 可选解绑口
        }

        // ---- 内部：几何 ----

        private float Step()
        {
            var size = Template.rect.size;
            float item = Direction == Axis.Vertical ? size.y : size.x;
            return item + Spacing;
        }

        private float TotalSize(int count)
            => count <= 0 ? 0f : count * (Step() - Spacing) + (count - 1) * Spacing;

        private void ApplyContentSize(float total)
        {
            var content = (RectTransform)transform;
            var size = content.sizeDelta;
            content.sizeDelta = Direction == Axis.Vertical
                ? new Vector2(size.x, total)                 // 水平路径不得写垂直尺寸（§8.2）
                : new Vector2(total, size.y);
        }

        /// <summary>Content 沿滚动轴已滚过的距离（Content 负向平移 = 视口起点前移）。</summary>
        private float Offset()
        {
            var content = (RectTransform)transform;
            float along = Direction == Axis.Vertical ? -content.anchoredPosition.y : content.anchoredPosition.x;
            return Mathf.Max(0f, along);
        }

        /// <summary>视口沿滚动轴尺寸；无 ScrollRect / 无视口 = 整表可见（不裁剪）。</summary>
        private float ViewportExtent()
        {
            var viewport = _scroll != null ? _scroll.viewport : null;
            if (viewport == null && _scroll != null) viewport = _scroll.transform as RectTransform;
            if (viewport == null) return Mathf.Max(_contentSize, 1f);
            return Direction == Axis.Vertical ? viewport.rect.height : viewport.rect.width;
        }

        private Vector2 PositionOf(int index, float step)
        {
            float along = index * step;
            return Direction == Axis.Vertical ? new Vector2(0f, -along) : new Vector2(along, 0f);
        }

        // ---- 内部：ScrollRect 接线（只绑一次；禁用/销毁对称解绑）----

        private void ResolveScroll()
        {
            var found = GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (ReferenceEquals(found, _scroll)) return;
            SubscribeScroll(false);
            _scroll = found;
            SubscribeScroll(true);
        }

        private void SubscribeScroll(bool on)
        {
            if (_scroll == null || _subscribed == on) return;
            if (on) _scroll.onValueChanged.AddListener(OnScrollChanged);
            else _scroll.onValueChanged.RemoveListener(OnScrollChanged);
            _subscribed = on;
        }

        private bool _subscribed;

        private void OnScrollChanged(Vector2 _) => _dirty = true;
    }

    /// <summary>
    /// 可选：节点重绑/回收前的解绑口（§8.2"节点重绑先解除旧回调/租约，绑定代次失效，再写新数据"）。
    /// 数据源不需要在节点上挂监听时可实现空实现，或干脆不实现本接口（内核不强制）。
    /// </summary>
    public interface IVirtualListUnbind
    {
        void Unbind(Component item);
    }
}
