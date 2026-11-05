using System;
using System.IO;
using LiteGame.Toolchain;
using UnityEditor;
using UnityEngine;
using LiteGame.Modules.Config;
using LiteGame.Modules.Map;

namespace LiteGame.EditorTools
{
        /// <summary>
        /// 烘焙工具箱共享单源：目标资产路径、参考片段、层级查找与预制体编辑域。
        /// 六件烘焙/调带工具（<see cref="BodyCylinderBaker"/>/<see cref="MuzzleOffsetBaker"/>/
        /// <see cref="WeaponGripBaker"/>/<see cref="HeadHitLineTuner"/>/<see cref="RootMotionBaker"/>/
        /// <see cref="SimMapBaker"/>）共用的常量与原语**全部收口在此**——任何工具不得再私有复制同值路径字面量（修改指南 §1.1/§1.2）。
        /// 三族来源：prefab 采样族（PrefabPath/ControllerPath/ReferenceClip）、片段采样族（RootMotionConfigPath）
        /// 与场景采样族（MapScenePath——地图烘焙）。
        /// 产物写盘面：文本产物走 <see cref="WriteArtifact"/>、字节产物走 <see cref="WriteArtifactBytes"/>，
        /// 两者共用 write-if-changed 原语（<see cref="WriteIfChanged"/>/<see cref="WriteIfChangedBytes"/>）。
        /// </summary>
    public static class BakeTargets          // public：run_script 以本文件为编译单元直调烘焙入口，需可见（Editor 程序集，无产品面）
    {
        // ---- 目标资产（工具箱内唯一出处）----

        /// <summary>烘焙唯一来源 prefab（**不入版本管理**——重烘需本地还原；运行时同值见
        /// SimView.DefaultEntityPrefab / CombatGirlsAnimationProfile.ViewPrefabPath，属运行时面另行登记）。</summary>
        public const string PrefabPath = "Assets/ShareResource/Characters/Player(Rifle).prefab";

        /// <summary>参考姿态控制器（采样参考片段用；换包/换控制器后全工具箱重烘）。</summary>
        public const string ControllerPath = "Assets/ThirdParty/CombatGirlsCharacterPack/RifleGirl/Animations/Rifle_Controller.controller";

        /// <summary>参考姿态片段（t=0 净姿态）：射弹时刻视觉姿态单族——瞄准=AimIdle、腰射开火窗 FireIdle
        /// 亦以 AimIdle 循环填窗（见 CombatGirlsAnimationProfile/CombatAnimMachine），故单套常量取 AimIdle t=0。</summary>
        public const string ReferenceClip = "AimIdle";

        // ---- 各工具产物 ----

        /// <summary>入库身位夹具（prefab CharacterController 的 radius/height 快照；**Assets 之外的普通文件**——
        /// 供三方对账与地图烘焙出生点校验，不生成 .meta）。</summary>
        public const string BodyCcFixturePath = "Tests/EditMode/Fixtures/BodyCc.json";

        // ---- 片段采样族（根位移——来源是 RootMotionBakeConfig 引用的 CombatGirls 片段包，不入版本管理）----

        public const string RootMotionConfigPath = "Assets/LiteGame/Editor/Baking/RootMotionBakeConfig.asset";

        /// <summary>根位移数据产物去向（运行时经内容包装载；与 <c>RootMotionCatalog.DefaultAssetPath</c> 同串，
        /// 烘焙器写入前比对）。不进 BuildHash 源集——改后需重发内容包。</summary>
        public const string RootMotionBinOutput = "Assets/GameData/Config/runtime/rootmotion.bin";

        /// <summary>持枪偏移运行时 .bin 去向（烘焙产物，字节产物通道）。</summary>
        public const string GripBinOutput = "Assets/GameData/Config/runtime/grip.bin";

        // ---- 场景采样族（地图烘焙——来源是地图场景，非 prefab/片段）----

        /// <summary>地图场景（首图母版）：判定几何（<see cref="MapCollisionRoot"/> 代理树）＋出生标记（<see cref="SpawnPrefix"/>）＋边界（<see cref="MapBoundsObject"/>）。</summary>
        public const string MapScenePath = "Assets/Scenes/Arena01.unity";

        /// <summary>地图产物：Luban 数据源 JSON（文本产物通道——写盘走 <see cref="WriteArtifact"/>；不自动跑 gen.bat，见 <see cref="SimMapBaker"/>）。
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
            "Assets/Scenes/TrainingGround.unity", LiteGame.Modules.Config.MapTableProjection.TrainingMapId,
            "training_ground", "Luban/Data/Map/map_training_ground.json");

        /// <summary>地图烘焙目标**注册表**（加图＝加一条）：工具箱「地图」页按本表逐图出按钮，
        /// <see cref="SimMapBaker.BakeAll"/> 按本表整批烘焙。行键须全表唯一（`tbmap` 主键）且与本表
        /// Output 一一对应——**加图不改任何其他代码**（判定图/场景/卸载全按表行驱动）。</summary>
        public static readonly MapTarget[] Maps = { ArenaMap, TrainingMap };

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
        /// 文本 write-if-changed 写盘原语（<see cref="WriteArtifact(string,string)"/> 的落盘基座）：
        /// 与既有文件逐字节一致则不动文件（重烘不触碰产物文件，防"重烘即抖"）。UTF-8 无 BOM。返回是否实际写盘。
        /// </summary>
        private static bool WriteIfChanged(string outputPath, string text)
        {
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == text) return false;
            File.WriteAllText(outputPath, text);
            return true;
        }

        /// <summary>字节版 write-if-changed（数据产物通道，如 grip.bin）：内容逐字节相同则不动文件
        /// （重烘不触碰产物、资产导入不抖动）。返回是否实际写盘。</summary>
        private static bool WriteIfChangedBytes(string outputPath, byte[] bytes)
        {
            if (File.Exists(outputPath))
            {
                byte[] existing = File.ReadAllBytes(outputPath);
                if (existing.Length == bytes.Length && existing.AsSpan().SequenceEqual(bytes)) return false;
            }
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllBytes(outputPath, bytes);
            return true;
        }

        /// <summary>文本数据产物统一 emit（数据通道，如地图 Luban JSON）：write-if-changed；
        /// 产物在 Assets 外、无 ImportAsset。返回落盘结论行（planned/up to date/written），由工具拼进报告。</summary>
        public static string WriteArtifact(string outputPath, string text)
        {
            return WriteArtifact(outputPath, text, null);
        }

        /// <summary>带写盘 scope 的文本数据产物 emit（dry-run/allowlist/Undo 由 scope 统一处理）。</summary>
        public static string WriteArtifact(string outputPath, string text, ToolWriteScope scope)
        {
            if (scope == null) return (WriteIfChanged(outputPath, text) ? "written：" : "up to date：") + outputPath;
            ToolWriteOutcome outcome = scope.WriteText(outputPath, text);
            return OutcomeLabel(outcome) + outputPath;
        }

        /// <summary>字节版数据产物 emit（如 grip.bin）：write-if-changed；产物在 Assets 内，
        /// 实际写盘后 <see cref="AssetDatabase.ImportAsset"/> 触发导入。
        /// 返回落盘结论行（planned/up to date/written），由工具拼进报告。</summary>
        public static string WriteArtifactBytes(string outputPath, byte[] bytes)
        {
            return WriteArtifactBytes(outputPath, bytes, null);
        }

        /// <summary>带写盘 scope 的字节版数据产物 emit（dry-run/allowlist/Undo 由 scope 统一处理）。</summary>
        public static string WriteArtifactBytes(string outputPath, byte[] bytes, ToolWriteScope scope)
        {
            if (bytes == null) throw new System.ArgumentNullException(nameof(bytes));
            if (scope == null)
            {
                bool wrote = WriteIfChangedBytes(outputPath, bytes);
                if (wrote) AssetDatabase.ImportAsset(outputPath);
                return (wrote ? "written：" : "up to date：") + outputPath;
            }

            ToolWriteOutcome outcome = scope.WriteBytes(outputPath, bytes);
            if (outcome == ToolWriteOutcome.Written) AssetDatabase.ImportAsset(outputPath);
            return OutcomeLabel(outcome) + outputPath;
        }

        private static string OutcomeLabel(ToolWriteOutcome outcome)
        {
            switch (outcome)
            {
                case ToolWriteOutcome.Planned: return "planned：";
                case ToolWriteOutcome.Written: return "written：";
                default: return "up to date：";
            }
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
