using System;

namespace LiteGame.Modules.Input
{
    /// <summary>
    /// 输入拦截源的**唯一裁决多项式**：任一登记源在其条件下成立，游戏意图即被拦下。
    /// </summary>
    public sealed class IntentGate
    {
        /// <summary>拦截源标识（登记用；<see cref="BlockedBy"/> 返回的就是它）。</summary>
        public readonly struct BlockerKey
        {
            public readonly string Name;
            public readonly string Reason;

            public BlockerKey(string name, string reason)
            {
                Name = name;
                Reason = reason;
            }

            public override string ToString() => Name;
        }

        private struct Blocker
        {
            public Func<bool> IsBlocking;
            public BlockerKey Key;
        }

        /// <summary>登记容量（超出即拒绝——"容量不足显性失败"与 UI 排序区间同口径，不静默丢弃）。</summary>
        public const int MaxBlockers = 16;

        private readonly Blocker[] _blockers = new Blocker[MaxBlockers];
        private int _count;

        /// <summary>当前是否被拦下（<see cref="Evaluate"/> 未跑过时为 false——未裁决不放行也不拦，语义 = 无源）。</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>拦下当前意图的源；未拦下为 <c>null</c>。诊断与验收用（"谁关的输入门"）。</summary>
        public BlockerKey? BlockedBy { get; private set; }

        /// <summary>登记源数量。</summary>
        public int Count => _count;

        /// <summary>
        /// 登记拦截源。<paramref name="key"/> 名重复即抛——两个同名源会让"谁拦的"失去意义
        /// （§6.2："禁用页面交互不等于停止阻挡下层射线"——职责必须可分辨）。
        /// </summary>
        public void Register(BlockerKey key, Func<bool> isBlocking)
        {
            if (string.IsNullOrEmpty(key.Name)) throw new ArgumentException("拦截源必须有名字", nameof(key));
            if (isBlocking == null) throw new ArgumentNullException(nameof(isBlocking));
            if (_count >= MaxBlockers)
                throw new InvalidOperationException($"拦截源超出上限 {MaxBlockers}（容量不足显性失败，不静默丢弃）");
            for (int i = 0; i < _count; i++)
                if (_blockers[i].Key.Name == key.Name)
                    throw new InvalidOperationException($"拦截源重名：{key.Name}");

            _blockers[_count] = new Blocker { IsBlocking = isBlocking, Key = key };
            _count++;
        }

        /// <summary>
        /// 注销拦截源（按名）。
        /// </summary>
        public bool Remove(string name)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_blockers[i].Key.Name != name) continue;

                for (int j = i; j < _count - 1; j++) _blockers[j] = _blockers[j + 1];
                _count--;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 求值：任一源成立即拦下，**先登记者优先**报出理由（产品登记顺序即优先级）。
        /// 零分配——本方法是每渲染帧的热路径。
        /// </summary>
        public bool Evaluate()
        {
            for (int i = 0; i < _count; i++)
            {
                Func<bool> f = _blockers[i].IsBlocking;
                if (f != null && f())
                {
                    IsBlocked = true;
                    BlockedBy = _blockers[i].Key;
                    return true;
                }
            }
            IsBlocked = false;
            BlockedBy = null;
            return false;
        }

        /// <summary>清空（宿主关闭/测试复位；语义 = 没有任何拦截源，而非"全部放行"）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _count; i++) _blockers[i] = default;
            _count = 0;
            IsBlocked = false;
            BlockedBy = null;
        }
    }
}
