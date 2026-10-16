using LiteClient;

namespace LiteGame
{
    /// <summary>
    /// 对局场景**装载语义单源**（多图批收敛）：Rig＝装配件（vcam×2）；地图场景路径**查地图表**
    /// （`tbmap.scene` 列——加图＝加文件 + 加场景，本件不再持有逐图路径常量）。
    ///
    /// **地图行键的事实源 = JoinAck**（服务器权威）：`RoomClient.MapId` → <see cref="ResolveMapId"/> 裁决
    /// （0 = 服务器未标注，按首图口径回落——嵌入式/测试直构形态）；表行与场景都按它取——视觉与判定
    /// 恒同图（D16）。
    ///
    /// **生命周期落点**：加载在 <c>ProcedureMatch</c>（JoinAck 后、移交 Battle 前——必须早于 Battle
    /// 开场的相机硬门；进度进"进房等待场"窗口），卸载在 <c>ProcedureBattle</c> 收尾（正常结束/主动
    /// 离场/失败三条出口统一；场景清单按表枚举）。
    /// </summary>
    internal static class BattleScenes
    {
        /// <summary>对局装配场景（Rig：`Player Camera`/`Aim Camera` 两台 vcam）。</summary>
        public const string Rig = "Assets/Scenes/BattleRig.unity";

        /// <summary>本局地图行键裁决（**唯一处**）：JoinAck 声明 &gt;0 用之；0/负 = 服务器未标注
        /// （嵌入式/测试直构）→ 首图（<see cref="MapTableProjection.DefaultMapId"/>）。
        /// 场景路径与判定图行都必须经本裁决后取值（两处同键 = 同图）。</summary>
        public static int ResolveMapId(int joinedMapId)
            => joinedMapId > 0 ? joinedMapId : MapTableProjection.DefaultMapId;

        /// <summary>本地服（dev）形态的宿主地图行键：测试房 = 训练图，其余 = 首图。
        /// **只在装配根建本地传输时用**（真服务器形态的图由服务器定——本裁决不进产品路径）。</summary>
        public static int LocalServerMapId(bool testRoom)
            => testRoom ? MapTableProjection.TrainingMapId : MapTableProjection.DefaultMapId;
    }
}
