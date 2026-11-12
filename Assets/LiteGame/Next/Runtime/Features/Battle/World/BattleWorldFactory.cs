using System;
using LiteGameNext.Platform.Content;
using UnityEngine;

namespace LiteGameNext.Features.Battle.World
{
    /// <summary>世界视图装配：由内容服务按 prefab 文件名装载（编辑器/Player 同一入口）。</summary>
    public static class BattleWorldFactory
    {
        public static BattleWorldView Create(NextContentService content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            GameObject instance = content.InstantiateSync(
                NextContentService.AddressKey(BattleWorldView.PrefabPath));
            BattleWorldView view = instance.GetComponent<BattleWorldView>();
            if (view == null)
            {
                content.Release(instance);
                throw new InvalidOperationException("战斗世界 prefab 根缺少 BattleWorldView");
            }
            return view;
        }
    }
}
