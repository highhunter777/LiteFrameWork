using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
        /// <summary>
        /// 烘焙工具箱共享单源：目标资产路径、参考片段、层级查找与预制体编辑域。
        /// 五件烘焙/调带工具（<see cref="BodyCylinderBaker"/>/<see cref="MuzzleOffsetBaker"/>/
        /// <see cref="WeaponGripBaker"/>/<see cref="HeadHitLineTuner"/>/<see cref="RootMotionBaker"/>）共用的
        /// 常量与原语**全部收口在此**——任何工具不得再私有复制同值路径字面量（修改指南 §1.1/§1.2）。
        /// 两族来源：prefab 采样族（PrefabPath/ControllerPath/ReferenceClip）与片段采样族（RootMotionConfigPath）。
        /// </summary>
    internal static class BakeTargets
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

        /// <summary>
        /// 统一产物 emit（四工具唯一写盘面）：固定头（生成器名 + 工具箱入口 + 勿手改）→ 工具语义注释行 → 代码体。
        /// LF 行尾、UTF-8 无 BOM、R 格式由调用方保证；**write-if-changed**——与既有产物逐字节一致则不动文件
        /// （重烘不触碰源集文件，防"重烘即抖"），变化才写盘 + ImportAsset 触发重编译。
        /// 返回落盘结论行（up to date / written），由工具拼进报告。
        /// </summary>
        public static string WriteGenerated(string outputPath, string generatorName, string[] semanticLines, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// 本文件由 {generatorName} 生成（烘焙工具箱：菜单 LiteGame/烘焙工具箱，或 run_script 直调）——勿手改。");
            foreach (var line in semanticLines) sb.AppendLine(line);
            sb.Append(body);
            string text = sb.ToString().Replace("\r\n", "\n");   // AppendLine 在 Windows 是 CRLF；产物统一 LF（BuildHash 侧行尾归一化，两者等价）

            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == text)
                return "up to date：" + outputPath;
            File.WriteAllText(outputPath, text);
            AssetDatabase.ImportAsset(outputPath);
            return "written：" + outputPath;
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
