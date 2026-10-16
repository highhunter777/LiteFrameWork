namespace LiteGame
{
    /// <summary>
    /// 对局场景组（**单源**）：Rig＝装配件（vcam×2）；地图＝纯内容（禁 vcam）。
    /// **生命周期落点**：加载在 <c>ProcedureMatch</c>（JoinAck 后、移交 Battle 前——必须早于 Battle 开场的
    /// 相机硬门；进度进"进房等待场"窗口），卸载在 <c>ProcedureBattle</c> 收尾（正常结束/主动离场/失败
    /// 三条出口统一）——两处都引用本件，不各抄路径（红线 5）。
    /// </summary>
    internal static class BattleScenes
    {
        /// <summary>对局装配场景（Rig：`Player Camera`/`Aim Camera` 两台 vcam）。</summary>
        public const string Rig = "Assets/Scenes/BattleRig.unity";

        /// <summary>首图地图场景（纯地图：视觉＋灯＋`MapCollision` 代理＋出生标记）。</summary>
        public const string Arena = "Assets/Scenes/Arena01.unity";

        /// <summary>训练场地图场景（灰盒训练场）。</summary>
        public const string Training = "Assets/Scenes/TrainingGround.unity";

        /// <summary>本局地图场景：训练模式（testRoom）→ 训练场，其余 → 首图。</summary>
        public static string MapFor(bool testRoom) => testRoom ? Training : Arena;

        /// <summary>本局地图行键（**与 <see cref="MapFor"/> 同一个切换**——视觉与判定必须同图）。
        /// 真服务器侧协议无 `map_id`（D10），故该值只对本地服/测试房成立；多图跨服需另设计。</summary>
        public static int MapIdFor(bool testRoom)
            => testRoom ? LiteClient.MapTableProjection.TrainingMapId : LiteClient.MapTableProjection.DefaultMapId;
    }
}
