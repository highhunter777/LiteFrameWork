using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
        /// <summary>
        /// 烘焙工具箱共享单源：目标资产路径、参考片段、层级查找与预制体编辑域。
        /// 六件烘焙/调带工具（<see cref="BodyCylinderBaker"/>/<see cref="MuzzleOffsetBaker"/>/
        /// <see cref="WeaponGripBaker"/>/<see cref="HeadHitLineTuner"/>/<see cref="RootMotionBaker"/>/
        /// <see cref="SimMapBaker"/>）共用的常量与原语**全部收口在此**——任何工具不得再私有复制同值路径字面量（修改指南 §1.1/§1.2）。
        /// 三族来源：prefab 采样族（PrefabPath/ControllerPath/ReferenceClip）、片段采样族（RootMotionConfigPath）
        /// 与场景采样族（MapScenePath——地图烘焙）。
        /// </summary>
    public static class BakeTargets          // public：run_script 以本文件为编译单元直调烘焙入口，需可见（Editor 程序集，无产品面）
    {
        // ---- 目标资产（工具箱内唯一出处）----

        /// <summary>烘焙唯一来源 prefab（**不入版本管理**——重烘需本地还原；运行时同值见
        /// SimView.DefaultEntityPrefab / CombatGirlsAnimationProfile.ViewPrefabPath，属运行时面另行登记）。</summary>
        public const string PrefabPath = "Assets/ShareResource/Characters/Player(Rifle).prefab";

        /// <summary>参考姿态控制器（采样参考片段用；换包/换控制器后全工具箱重烘）。</summary>
        public const string ControllerPath = "Assets/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>参考姿态片段（t=0 净姿态）：射弹时刻视觉姿态单族——瞄准=AimIdle、腰射开火窗 FireIdle
        /// 亦以 AimIdle 循环填窗（见 CombatGirlsAnimationProfile/CombatAnimMachine），故单套常量取 AimIdle t=0。</summary>
        public const string ReferenceClip = "AimIdle";

        // ---- 各工具产物 ----

        public const string HeadBakeOutput = "Assets/LiteSim/Core/Scripts/HeadBake.g.cs";
        public const string MuzzleBakeOutput = "Assets/LiteSim/Core/Scripts/MuzzleBake.g.cs";
        public const string BodyBakeOutput = "Assets/LiteSim/Core/Scripts/BodyBake.g.cs";
        /// <summary>持枪偏移常量产物。**刻意不进 LiteSim/Core/Scripts**（BuildHash 源集）——
        /// 纯表现参数进闭包会复刻"hash 变了但判定没变"的全员拒进房噪声（gen-build-hash 头注释明令的病症）。</summary>
        public const string GripBakeOutput = "Assets/LiteView/Animation/GripBake.g.cs";

        // ---- 片段采样族（根位移——来源是 RootMotionBakeConfig 引用的 CombatGirls 片段包，不入版本管理）----

        public const string RootMotionConfigPath = "Assets/LiteGame/Editor/Baking/RootMotionBakeConfig.asset";
        public const string RootMotionOutput = "Assets/LiteSim/Core/Scripts/RootMotionData.g.cs";

        // ---- 场景采样族（地图烘焙——来源是地图场景，非 prefab/片段）----

        /// <summary>地图场景（首图母版）：判定几何（<see cref="MapCollisionRoot"/> 代理树）＋出生标记（<see cref="SpawnPrefix"/>）＋边界（<see cref="MapBoundsObject"/>）。</summary>
        public const string MapScenePath = "Assets/Scenes/Arena01.unity";

        /// <summary>地图产物：Luban 数据源 JSON（非 .g.cs 通道——写盘走 <see cref="WriteArtifact"/>；不自动跑 gen.bat，见 <see cref="SimMapBaker"/>）。
        /// **每图一文件**（`Luban/Data/Map/`；表注册 `input="Map"` 读整目录）——加图＝加文件，不改表声明。</summary>
        public const string MapOutput = "Luban/Data/Map/map_arena01.json";

        /// <summary>地图目标（**加图＝加一条**）：场景路径 ＋ 行身份 ＋ 产物文件；拓扑约定（MapCollision/spawn_/map_bounds）全图共用。</summary>
        public readonly struct MapTarget
        {
            public readonly string ScenePath;
            public readonly int MapId;
            public readonly string MapName;
            public readonly string Output;

            public MapTarget(string scenePath, int mapId, string mapName, string output)
            {
                ScenePath = scenePath; MapId = mapId; MapName = mapName; Output = output;
            }
        }

        /// <summary>首图目标（arena01）。</summary>
        public static readonly MapTarget ArenaMap = new MapTarget(MapScenePath, MapId, MapName, MapOutput);

        /// <summary>训练场目标（第二张地图；场景灰盒训练场，与首图共用拓扑约定）。</summary>
        public static readonly MapTarget TrainingMap = new MapTarget(
            "Assets/Scenes/TrainingGround.unity", LiteClient.MapTableProjection.TrainingMapId,
            "training_ground", "Luban/Data/Map/map_training_ground.json");

        /// <summary>行身份（首图；多图目标见 <see cref="ArenaMap"/>/<see cref="TrainingMap"/>）。</summary>
        public const int MapId = 1;

        /// <summary>行名（与首图场景同源——首图 = <c>Arena01.unity</c>）。</summary>
        public const string MapName = "arena01";

        // ---- 地图场景拓扑约定（烘焙器读取面；场景制作方同守）----

        /// <summary>采集根对象名（地图场景根下唯一）——单源在运行时侧 <see cref="MapTopology.MapCollisionRoot"/>（Editor 程序集别名引用，禁两侧各写字面量）。</summary>
        public const string MapCollisionRoot = MapTopology.MapCollisionRoot;

        /// <summary>边界标注对象名（挂 <see cref="LiteGame.MapBoundsMarker"/>）。</summary>
        public const string MapBoundsObject = "map_bounds";

        /// <summary>出生标记名前缀（按名 Ordinal 排序定 index）。</summary>
        public const string SpawnPrefix = "spawn_";

        /// <summary>表面类型前缀（`surf_&lt;surface&gt;_...`；无前缀 = Default）。</summary>
        public const string SurfacePrefix = "surf_";

        /// <summary>
        /// write-if-changed 写盘原语（全部产物的唯一写盘面）：与既有文件逐字节一致则不动文件
        /// （重烘不触碰产物文件，防"重烘即抖"）。UTF-8 无 BOM。返回是否实际写盘。
        /// </summary>
        private static bool WriteIfChanged(string outputPath, string text)
        {
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == text) return false;
            File.WriteAllText(outputPath, text);
            return true;
        }

        /// <summary>非 .g.cs 产物统一 emit（数据通道，如地图 Luban JSON）：write-if-changed；
        /// 产物在 Assets 外、无 ImportAsset。返回落盘结论行（up to date / written），由工具拼进报告。</summary>
        public static string WriteArtifact(string outputPath, string text)
            => (WriteIfChanged(outputPath, text) ? "written：" : "up to date：") + outputPath;

        /// <summary>
        /// .g.cs 产物统一 emit（各烘焙器唯一代码产物写盘面）：固定头（生成器名 + 工具箱入口 + 勿手改）→ 工具语义注释行 → 代码体。
        /// LF 行尾、UTF-8 无 BOM、R 格式由调用方保证；**write-if-changed**——与既有产物逐字节一致则不动文件，
        /// 变化才写盘 + ImportAsset 触发重编译。返回落盘结论行（up to date / written），由工具拼进报告。
        /// </summary>
        public static string WriteGenerated(string outputPath, string generatorName, string[] semanticLines, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// 本文件由 {generatorName} 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 直调）——勿手改。");
            foreach (var line in semanticLines) sb.AppendLine(line);
            sb.Append(body);
            string text = sb.ToString().Replace("\r\n", "\n");   // AppendLine 在 Windows 是 CRLF；产物统一 LF（BuildHash 侧行尾归一化，两者等价）

            bool wrote = WriteIfChanged(outputPath, text);
            if (wrote) AssetDatabase.ImportAsset(outputPath);
            return (wrote ? "written：" : "up to date：") + outputPath;
        }

        /// <summary>在 <paramref name="root"/> 子树内按名深搜（含根）——四工具共用一份实现。</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>载入 <see cref="ControllerPath"/> 并定位 <see cref="ReferenceClip"/>；缺控制器/缺片段返回 null（调用方出 ABORT 报告）。</summary>
        public static AnimationClip LoadReferenceClip()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null) return null;
            foreach (var clip in controller.animationClips)
                if (clip != null && clip.name == ReferenceClip) return clip;
            return null;
        }
    }

    /// <summary>
    /// 预制体编辑域：<c>LoadPrefabContents → 变更 → UnloadPrefabContents</c> 的 try/finally 收口——
    /// 三件烘焙工具共用同一编辑纪律（Unload 必达，异常路径不泄漏临时场景）。
    /// </summary>
    internal sealed class PrefabEditScope : System.IDisposable
    {
        private readonly GameObject _contents;

        private PrefabEditScope(GameObject contents) { _contents = contents; }

        /// <summary>打开 <see cref="BakeTargets.PrefabPath"/> 编辑域；载入失败返回 null（调用方出 ABORT 报告）。</summary>
        public static PrefabEditScope Open(string prefabPath = BakeTargets.PrefabPath)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            return contents == null ? null : new PrefabEditScope(contents);
        }

        public GameObject Contents => _contents;

        public void Dispose()
        {
            if (_contents != null) PrefabUtility.UnloadPrefabContents(_contents);
        }
    }
}
