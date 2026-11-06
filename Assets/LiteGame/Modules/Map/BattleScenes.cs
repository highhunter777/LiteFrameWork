using LiteFramework;
using LiteGame.Modules.Config;

using LiteSync.Client;
namespace LiteGame.Modules.Map
{
    /// <summary>
    /// 对局场景**装载语义单源**（多图批收敛）：Rig＝装配件（vcam×2）。
    /// </summary>
    public static class BattleScenes
    {
        /// <summary>对局装配场景（Rig：`Player Camera`/`Aim Camera` 两台 vcam）。</summary>
        public const string Rig = "Assets/Scenes/BattleRig.unity";

        /// <summary>本局地图行键裁决（**唯一处**）：JoinAck 声明 &gt;0 用之；0/负 = 服务器未标注
        /// （嵌入式/测试直构）→ 首图（<see cref="MapTableProjection.DefaultMapId"/>）。
        /// 场景路径与判定图行都必须经本裁决后取值（两处同键 = 同图）。</summary>
        public static int ResolveMapId(int joinedMapId)
            => joinedMapId > 0 ? joinedMapId : MapTableProjection.DefaultMapId;

        /// <summary>本地服（dev）形态的宿主地图行键：本地测试模式 = 训练图，其余 = 首图。
        /// **只在装配根建本地传输时用**（真服务器形态的图由服务器定——本裁决不进产品路径）。</summary>
        public static int LocalServerMapId(bool testRoom)
            => testRoom ? MapTableProjection.TrainingMapId : MapTableProjection.DefaultMapId;
    }
}
