namespace LiteGameNext.Features.Battle.World
{
    /// <summary>地图行键 → 场景资源路径（与 Luban `tbmap.scene` 列一致；接入 ConfigService 前的最小单源）。</summary>
    public static class BattleMapCatalog
    {
        /// <summary>首图（arena01）。</summary>
        public const int DefaultMapId = 1;

        /// <summary>训练场。</summary>
        public const int TrainingMapId = 2;

        /// <summary>按行键取场景路径；未登记行键回退首图（不静默给空路径）。</summary>
        public static string SceneOf(int mapId)
        {
            switch (mapId)
            {
                case TrainingMapId: return "Assets/Scenes/TrainingGround.unity";
                default: return "Assets/Scenes/Arena01.unity";
            }
        }

        /// <summary>服务端未标注（0/负）时取首图，否则用声明值。</summary>
        public static int Resolve(int joinedMapId) => joinedMapId > 0 ? joinedMapId : DefaultMapId;
    }
}
