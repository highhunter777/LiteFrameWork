using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>实体视图工厂：优先用角色 prefab，缺资源回退调试立方体（缺美术不阻断对局）。</summary>
    public interface IBattleEntityViewFactory
    {
        /// <summary>为实体创建视图；返回 null 表示回退到默认立方体。</summary>
        GameObject Create(long entityId, bool isLocal);
    }

    /// <summary>按 prefab 名装载角色视图；首帧加载失败即永久回退立方体（不每帧重试刷日志）。</summary>
    public sealed class PrefabEntityViewFactory : IBattleEntityViewFactory
    {
        private readonly Func<string, GameObject> _instantiate;
        private readonly string _prefabName;
        private bool _disabled;

        public PrefabEntityViewFactory(Func<string, GameObject> instantiate, string prefabName = "Player(Rifle)")
        {
            _instantiate = instantiate ?? throw new ArgumentNullException(nameof(instantiate));
            _prefabName = prefabName;
        }

        public GameObject Create(long entityId, bool isLocal)
        {
            if (_disabled) return null;
            GameObject view;
            try
            {
                view = _instantiate(_prefabName);
            }
            catch (Exception)
            {
                _disabled = true;
                return null;
            }
            if (view == null)
            {
                _disabled = true;
                return null;
            }
            view.name = "Entity_" + entityId;
            var tint = view.GetComponent<HitFlashTint>();
            if (tint == null) tint = view.AddComponent<HitFlashTint>();
            tint.SetBaseColor(isLocal ? new Color(0.2f, 0.9f, 0.4f) : new Color(0.9f, 0.3f, 0.3f));
            return view;
        }
    }
}
