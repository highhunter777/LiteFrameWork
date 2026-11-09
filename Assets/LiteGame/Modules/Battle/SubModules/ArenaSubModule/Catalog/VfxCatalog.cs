using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 名 → 定义（《VFX服务实施指导》§1 决策 3：**命名即引用、不做 Luban 表**）： `fx_hit` → `{Root}fx_hit.prefab`。
    /// </summary>
    public sealed class VfxCatalog : IVfxCatalog
    {
        /// <summary>
        /// 特效 prefab 根目录（与《联机Demo美术资源清单》§0-6 的 `fx_` 命名约定配套）。
        /// </summary>
        public const string DefaultRoot = "Assets/LiteGame/Modules/Battle/Content/FX/";

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
