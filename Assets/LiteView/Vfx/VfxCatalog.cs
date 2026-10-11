using System.Collections.Generic;

namespace LiteView
{
    /// <summary>
    /// 名 → 定义（《VFX服务实施指导》§1 决策 3：**命名即引用、不做 Luban 表**）：
    /// `fx_hit` → `{Root}fx_hit.prefab`。需要额外元信息（类别/覆盖时长）的特效在构造时
    /// <see cref="Register"/> 登记；未登记的按命名约定合成默认项（值类型，零分配）。
    /// </summary>
    public sealed class VfxCatalog
    {
        /// <summary>
        /// 特效 prefab 根目录（与《联机Demo美术资源清单》§0-6 的 `fx_` 命名约定配套）：
        /// 共享资源层 `Assets/ShareResource/FX/`——**只放在用的自研 `fx_*` prefab**（FX 组收集这里，
        /// CollectAll 即精确收集）；顶层 `Assets/FX/` 是 L1 原始资源库（第三方原包/参考素材，不进收集）。
        /// </summary>
        public const string DefaultRoot = "Assets/ShareResource/FX/";

        private readonly Dictionary<string, VfxDef> _defs = new Dictionary<string, VfxDef>(16);
        private readonly string _root;

        public VfxCatalog(string root = DefaultRoot)
        {
            _root = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        }

        /// <summary>登记一条定义（同名覆盖）。</summary>
        public VfxCatalog Register(VfxDef def)
        {
            if (def.IsValid) _defs[def.Name] = def;
            return this;
        }

        /// <summary>解析：登记过走登记项，否则按"命名即引用"合成（名字为空 → 无效 def）。</summary>
        public VfxDef Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return default;
            if (_defs.TryGetValue(name, out var def)) return def;
            return new VfxDef(name, VfxCategories.Default, _root + name + ".prefab");
        }
    }
}
