using System;
using System.Collections.Generic;
using LiteFramework;

namespace LiteGame
{
    /// <summary>界面信息投影（M4 §2.0）：TbUIForm 行 → 壳消费的纯数据。**壳不碰 Luban 类型**——
    /// 投影在装配层 <see cref="UIFormCatalog"/>（唯一允许触碰 cfg 的位置）完成。</summary>
    /// <summary>per-form 缓存策略（§5.2 U2 表列——关闭后实例去向）。</summary>
    public enum UICacheStrategy
    {
        /// <summary>LRU：关闭后入池，超预算按最近使用序淘汰（默认）。</summary>
        Lru = 0,
        /// <summary>常驻：关闭后入池但**不参与 LRU 淘汰**（主界面/高频页——预算满也保留）。</summary>
        Resident = 1,
        /// <summary>关闭即销毁：不进池，释放租约与 GameObject（低频/重资源页）。</summary>
        DestroyOnClose = 2,
    }

    public sealed class UIFormInfo
    {
        public int Id;
        public string LuaPath;      // 注册表 Lua 逻辑表路径（"UI.UIMain"，LuaKeys 收口；§2.3 适配器消费）
        public string Location;     // prefab 资源完整路径（表列 = Assets/ 相对路径，如 "UI/UIMain.prefab"）
        public int Layer;           // 语义层索引（0=Bottom 1=Window 2=Top 3=System；System 归反馈面，见 §6.2）
        public bool FullScreen;     // 全屏页：激活时批量遮盖更低层级组（组级批量暂停语义）
        /// <summary>per-form 缓存策略（§5.2 U2 表列——默认 LRU；Resident 不淘汰、DestroyOnClose 关即销毁）。</summary>
        public UICacheStrategy CacheStrategy = UICacheStrategy.Lru;
    }

    /// <summary>
    /// 界面目录契约（§3"可注入面"）：壳只依赖"按 id 取界面信息"这一件事——生产实现 = Luban 投影
    /// <see cref="UIFormCatalog"/>，替身/测试可实现本接口而不必拉起配置链路（§3：避免 UIService 测试
    /// 依赖静态资源系统与完整启动流程）。
    /// </summary>
    public interface IUIFormCatalog
    {
        UIFormInfo Get(int id);
    }

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
