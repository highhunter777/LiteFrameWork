using System;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>世界视图装配：编辑器按 prefab 资源路径实例化；Player 后续接 YooAsset。</summary>
    public static class BattleWorldFactory
    {
        public static BattleWorldView Create()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BattleWorldView.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("战斗世界 prefab 不存在：" + BattleWorldView.PrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + "(Instance)";
            BattleWorldView view = instance.GetComponent<BattleWorldView>();
            if (view == null)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException("战斗世界 prefab 根缺少 BattleWorldView");
            }

            return view;
#else
            throw new NotSupportedException("P2 非编辑器未接入内容装载器（P3a 接 YooAsset）");
#endif
        }
    }
}
