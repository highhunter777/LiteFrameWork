using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 身位圆柱烘焙工具（与 <see cref="MuzzleOffsetBaker"/> 同构）：把 `Player(Rifle).prefab` 的
    /// **CharacterController**（美术可调、可视、唯一）烘成 Sim 身位常量 `BodyBake.g.cs`——
    /// 服务端命中柱 / 移动去穿插 / 本地视图 CC 三方共用同一份值（"改一次 CC = 判定联动"）。
    ///
    /// **语义映射**：CC 是胶囊（半球端），Sim 物理体是竖直圆柱 <c>[pos.Y, pos.Y + height]</c>——
    /// 烘焙要求 <c>center.y == height/2</c>（圆柱底 = 脚底）；不满足即抛（不烘半个身位）。
    ///
    /// **口径**（《固定斜视角射击方案专项设计》/ 施工记录）：**物理半径/高**是角色资产派生几何——
    /// CC 为单源（消费 <c>CombatConfig.BodyRadius</c>/<c>HitscanHeight</c>；tb_combat_num 两列已退役）。
    /// **命中柱半径是另一根量**（<c>CombatConfig.HitscanRadius</c>，判定宽容裁决常量 0.45——覆盖肢体/站姿、
    /// 放过枪尖与长发尾，**非本工具产物**）；头部带下沿 = <c>HitscanHeight × 0.775</c>（比例单源，随身高自动缩放）。
    /// </summary>
    public static class BodyCylinderBaker
    {
        private const string PrefabPath = "Assets/Prefab/Player(Rifle).prefab";
        private const string OutputPath = "Assets/LiteSim/Core/Scripts/BodyBake.g.cs";

        [MenuItem("LiteGame/烘焙身位圆柱")]
        public static string All()
        {
            var sb = new StringBuilder();
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var cc = contents.GetComponent<CharacterController>();
                if (cc == null) throw new InvalidOperationException("prefab 根无 CharacterController（身位烘焙的唯一来源）");

                float baseOffset = cc.center.y - cc.height * 0.5f;
                if (Mathf.Abs(baseOffset) > 1e-4f)
                    throw new InvalidOperationException(
                        $"CC center.y({cc.center.y:F4}) ≠ height/2({cc.height * 0.5f:F4})——圆柱底不在脚底，不烘（对齐后再跑）");

                string text = BuildText(cc.radius, cc.height);
                File.WriteAllText(OutputPath, text);
                AssetDatabase.ImportAsset(OutputPath);

                sb.AppendLine($"已烘焙：Radius={cc.radius:R} Height={cc.height:R}（center.y 校验通过）");
                sb.AppendLine($"来源：{PrefabPath} 的 CharacterController");
                sb.AppendLine($"输出：{OutputPath}");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return sb.ToString();
        }

        /// <summary>生成文本（R 格式逐位往返；注释不含日期/批次痕迹——遵守注释卫生）。</summary>
        private static string BuildText(float radius, float height)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("// 本文件由 BodyCylinderBaker 生成（菜单 LiteGame/烘焙身位圆柱，或 run_script 调用）——勿手改。");
            sb.AppendLine("// 来源：Assets/Prefab/Player(Rifle).prefab 的 CharacterController（美术调 CC 后重跑即重烘；");
            sb.AppendLine("//       该 prefab 不入版本管理，重烘需本地 prefab）。");
            sb.AppendLine("// 语义：Sim 物理体竖直圆柱 [pos.Y, pos.Y + Height]（CC center.y 已校验 == Height/2）。");
            sb.AppendLine("// 消费：CombatConfig.BodyRadius（移动去穿插）+ HitscanHeight（站姿高/头带比例基底）+ 视图 CC。");
            sb.AppendLine("// 注意：命中柱半径 HitscanRadius（0.45）是判定宽容裁决常量，不是本产物。");
            sb.AppendLine("namespace LiteSim");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>身位圆柱常量（烘焙值；= prefab CharacterController 的 radius/height）。</summary>");
            sb.AppendLine("    public static class BodyBake");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const float Radius = {radius.ToString("R", inv)}f;");
            sb.AppendLine($"        public const float Height = {height.ToString("R", inv)}f;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
