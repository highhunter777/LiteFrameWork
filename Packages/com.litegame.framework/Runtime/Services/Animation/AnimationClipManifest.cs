using System;
using UnityEngine;

namespace LiteFramework.Animation
{
    /// <summary>
    /// 片段清单（《动画模块专项设计》§4 片源载体裁决）：**键 → <see cref="AnimationClip"/> 显式引用** 的纯配置资产——直 Clip 形态（去 AC 后的主路径）的唯一片源载体。
    /// </summary>
    public sealed class AnimationClipManifest : ScriptableObject
    {
        /// <summary>单条登记：绑定键 → 片段显式引用。</summary>
        [Serializable]
        public struct Entry
        {
            /// <summary>绑定键（Profile 登记面单源——清单不发明新键）。</summary>
            public string Key;

            /// <summary>片段显式引用（缺包克隆时为 null：资源缺失态，非配置错误）。</summary>
            public AnimationClip Clip;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>登记条数（装配期遍历用；覆盖校验在装配方——本类型不判键集合完整性）。</summary>
        public int EntryCount => _entries.Length;

        /// <summary>按下标读条目（零分配遍历面——装配期逐键登记用；越界返回 false 不猜）。</summary>
        public bool TryGetEntry(int index, out Entry entry)
        {
            entry = default;
            if ((uint)index >= (uint)_entries.Length) return false;
            entry = _entries[index];
            return true;
        }

        /// <summary>键是否已登记（覆盖校验用——**只看键不看片段引用**：引用缺失是资源态，键缺失才是配置错误）。
        /// 重复键以首条为准（构建期工具保证唯一，本面不做二次裁决）。</summary>
        public bool ContainsKey(string key)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
