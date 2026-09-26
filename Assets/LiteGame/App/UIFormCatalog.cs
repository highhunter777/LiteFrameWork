using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 装配层投影件（M4 §2.0）：把 TbUIForm 行投影为 <see cref="UIFormInfo"/>。懒加载 + 幂等；
    /// 未命中抛（fail-fast，§3.4——表漏配在启动期当场暴露）。
    /// </summary>
    public sealed class UIFormCatalog : IUIFormCatalog
    {
        private readonly Dictionary<int, UIFormInfo> _byId = new Dictionary<int, UIFormInfo>(16);
        private readonly IConfigService _config;

        public UIFormCatalog(IConfigService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public UIFormInfo Get(int id)
        {
            if (_byId.Count == 0) Reload();                 // 首次访问投影（配置必已加载——Show 在 Main 后）
            if (!_byId.TryGetValue(id, out var info))
                throw new KeyNotFoundException($"UIForm 表无此行:{id}——核对 tbuiform 主键（§3.4 fail-fast）");
            return info;
        }

        /// <summary>重新投影（幂等；DevReload 场景表重载后可再调）。</summary>
        public void Reload()
        {
            _byId.Clear();
            foreach (var row in _config.Tables.Tbuiform.DataList)
            {
                _byId[row.Id] = new UIFormInfo
                {
                    Id = row.Id,
                    LuaPath = row.LuaPath,
                    Location = "Assets/" + row.Prefab,      // 表列 = Assets/ 相对路径（2026-09-13 定案）
                    Layer = row.Layer,
                    FullScreen = row.FullScreen,
                };
            }
            Log.Info($"UIForm 投影完成:{_byId.Count} 行", "UI");
        }
    }
}
