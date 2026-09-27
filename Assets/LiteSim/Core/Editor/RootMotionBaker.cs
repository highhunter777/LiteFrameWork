using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LiteSim;
using UnityEditor;
using UnityEngine;

namespace LiteSim.EditorTools
{
    /// <summary>烘焙条目（配置资产里逐动作登记；字段语义见 <see cref="RootMotionEntry"/>）。</summary>
    [Serializable]
    public class RootMotionBakeItem
    {
        public int ActionId;
        public int MotionVersion = 1;
        public AnimationClip Clip;
        public RootMotionDirectionPolicy DirectionPolicy = RootMotionDirectionPolicy.StartFacingLocked;
        public RootMotionMovementPolicy MovementPolicy = RootMotionMovementPolicy.Disable;
        public RootMotionCancelPolicy CancelPolicy = RootMotionCancelPolicy.NotCancellable;
    }

    /// <summary>烘焙配置资产（引用 CombatGirls 包内片段——包不入库，此资产也不入库；产物 .g.cs 才是共享物）。</summary>
    [CreateAssetMenu(fileName = "RootMotionBakeConfig", menuName = "LiteSim/根位移烘焙配置")]
    public class RootMotionBakeConfigAsset : ScriptableObject
    {
        public List<RootMotionBakeItem> Items = new List<RootMotionBakeItem>();
    }

    /// <summary>
    /// 根位移烘焙器（《联机动画与根位移专项设计》§3 制作链：采样 → 数值规范化/量化 → 校验 →
    /// 生成两端共享产物 → 摘要）。菜单：LiteSim/根位移/烘焙 RootMotionData.g.cs。
    ///
    /// **提取口径**：人形片段的根轨迹 = RootT 曲线（Avatar 局部空间）——逐 60Hz 采样帧间差分为
    /// 局部平面增量（毫米量化，round half away from zero）；非人形片段不支持（fail-fast，§3 不许静默回退）。
    /// **校验**（§3）：NaN/Inf、单 tick 超速（>2m）、重复 ActionId、非零 Y 位移（记峰值并警告——
    /// YPolicy 默认 Sim 重力，视觉抬升不进权威位移）。失败即中止并列出"动作/帧号"定位。
    /// **确定性**：排序固定（ActionId 升序）、InvariantCulture 固定格式、量化吸收曲线求值的浮点尾差
    /// ——同配置重复烘焙逐字节一致。
    /// 产物落在 LiteSim/Core/Scripts（BuildHash 源集内——§3"接入 buildHash"随源集自动覆盖，烘焙后重跑
    /// scripts/gen-build-hash.py）。
    /// </summary>
    public static class RootMotionBaker
    {
        private const string ConfigPath = "Assets/LiteSim/Core/Editor/RootMotionBakeConfig.asset";
        private const string OutputPath = "Assets/LiteSim/Core/Scripts/RootMotionData.g.cs";
        private const int MaxDeltaMm = 2000;        // 单 tick 位移上限（2m/tick = 120 m/s——超出按恶意资产拒绝）

        [MenuItem("LiteSim/根位移/烘焙 RootMotionData.g.cs")]
        public static void Bake()
        {
            var config = AssetDatabase.LoadAssetAtPath<RootMotionBakeConfigAsset>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"[RootMotionBaker] 缺烘焙配置:{ConfigPath}——右键 Create>LiteSim>根位移烘焙配置 建一件并登记动作");
                return;
            }
            if (config.Items == null || config.Items.Count <= 0)
            {
                Debug.LogError("[RootMotionBaker] 烘焙配置为空（零条目禁止出空产物——§3 不许静默零位移）");
                return;
            }

            var entries = new List<RootMotionEntry>(config.Items.Count);
            var errors = new StringBuilder();
            var seen = new HashSet<int>();

            var sorted = new List<RootMotionBakeItem>(config.Items);
            sorted.Sort((a, b) => a.ActionId.CompareTo(b.ActionId));

            foreach (var item in sorted)
            {
                if (!seen.Add(item.ActionId))
                {
                    errors.Append($"ActionId {item.ActionId} 重复登记\n");
                    continue;
                }
                string error = BakeOne(item, entries);
                if (error != null) errors.Append(error).Append('\n');
            }

            if (errors.Length > 0)
            {
                Debug.LogError($"[RootMotionBaker] 烘焙中止，失败清单：\n{errors}");
                return;
            }

            string digest = RootMotionDigest.Compute(entries.ToArray());
            WriteGenerated(entries, digest);
            AssetDatabase.Refresh();
            Debug.Log($"[RootMotionBaker] 已生成 {entries.Count} 条根位移 → {OutputPath}\ndigest={digest}\n"
                + "**记得重跑 scripts/gen-build-hash.py（产物在 BuildHash 源集内）**");
        }

        /// <summary>烘焙单条；返回 null=成功，否则错误文本（含动作/帧定位）。</summary>
        private static string BakeOne(RootMotionBakeItem item, List<RootMotionEntry> entries)
        {
            if (item.Clip == null) return $"ActionId {item.ActionId}: 未指定 AnimationClip";
            if (!item.Clip.humanMotion) return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」非人形（首版仅支持 RootT 曲线，拒绝静默回退）";

            AnimationCurve cx = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.x"));
            AnimationCurve cy = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.y"));
            AnimationCurve cz = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.z"));
            if (cx == null || cy == null || cz == null)
                return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」缺 RootT 曲线（导入未烘焙根运动）";

            int sampleCount = Math.Max(1, (int)Math.Round(item.Clip.length * RootMotionCatalog.TickRate,
                MidpointRounding.AwayFromZero) - 1);
            var dxz = new short[sampleCount * 2];
            var dyaw = new short[sampleCount];   // 首版恒零（DirectionPolicy=起始锁定）
            int yPeakMm = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                float t0 = (float)i / RootMotionCatalog.TickRate;
                float t1 = (float)(i + 1) / RootMotionCatalog.TickRate;
                float ex = cx.Evaluate(t1) - cx.Evaluate(t0);
                float ez = cz.Evaluate(t1) - cz.Evaluate(t0);
                float ey = cy.Evaluate(t1) - cy.Evaluate(t0);
                if (float.IsNaN(ex) || float.IsInfinity(ex) || float.IsNaN(ez) || float.IsInfinity(ez)
                    || float.IsNaN(ey) || float.IsInfinity(ey))
                    return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」tick {i} 采样 NaN/Inf";

                int mmX = QuantizeMm(ex);
                int mmZ = QuantizeMm(ez);
                int mmY = QuantizeMm(ey);
                if (Math.Abs(mmX) > MaxDeltaMm || Math.Abs(mmZ) > MaxDeltaMm)
                    return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」tick {i} 单帧位移超上限"
                        + $"（X={mmX}mm Z={mmZ}mm，上限 {MaxDeltaMm}）";
                if (Math.Abs(mmY) > yPeakMm) yPeakMm = Math.Abs(mmY);

                dxz[2 * i] = (short)mmX;
                dxz[2 * i + 1] = (short)mmZ;
            }

            if (yPeakMm > 0)
                Debug.LogWarning($"[RootMotionBaker] ActionId {item.ActionId}（{item.Clip.name}）检测到 Y 位移峰值 {yPeakMm}mm"
                    + "——YPolicy 默认沿用 Sim 重力，烘焙不使用 Y；若非预期请复查源资产/导入设置");

            entries.Add(new RootMotionEntry
            {
                ActionId = item.ActionId,
                MotionVersion = item.MotionVersion,
                SampleCount = sampleCount,
                DirectionPolicy = item.DirectionPolicy,
                MovementPolicy = item.MovementPolicy,
                CancelPolicy = item.CancelPolicy,
                YPeakMm = yPeakMm,
                DeltaLocalXzMm = dxz,
                DeltaYawCentiDeg = dyaw,
            });
            return null;
        }

        /// <summary>人形根曲线绑定（根轨迹存于 Animator 类型的 RootT 通道——Avatar 局部空间）。</summary>
        private static EditorCurveBinding RootBinding(string property) =>
            EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property);

        /// <summary>毫米量化（round half away from zero——与消费侧 Sim 定稿同规，§3）。</summary>
        private static int QuantizeMm(float meters) =>
            (int)Math.Clamp(Math.Round(meters * 1000f, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);

        /// <summary>生成 .g.cs（固定格式：ActionId 升序、InvariantCulture、逐字节可复现）。</summary>
        private static void WriteGenerated(List<RootMotionEntry> entries, string digest)
        {
            var sb = new StringBuilder(4096);
            sb.Append("// <auto-generated: LiteSim/根位移/烘焙 RootMotionData.g.cs —— DO NOT EDIT。").Append('\n');
            sb.Append("// 源：RootMotionBakeConfig（人形 RootT 曲线 60Hz 采样→毫米量化）；同配置重复烘焙逐字节一致。").Append('\n');
            sb.Append("// 改了片段/配置后重烘＋重跑 scripts/gen-build-hash.py（本文件在 BuildHash 源集内）。").Append('\n');
            sb.Append("namespace LiteSim").Append('\n').Append('{').Append('\n');
            sb.Append("    public static partial class RootMotionCatalog").Append('\n').Append("    {").Append('\n');
            sb.Append("        /// <summary>烘焙时点的产物摘要（RootMotionDigest 复算守卫用；对局准入两端校验）。</summary>").Append('\n');
            sb.Append("        public const string BakedContentDigest = \"").Append(digest).Append("\";").Append('\n').Append('\n');
            sb.Append("        public static readonly RootMotionEntry[] Entries = new RootMotionEntry[]").Append('\n').Append("        {").Append('\n');
            var ci = CultureInfo.InvariantCulture;
            foreach (var e in entries)
            {
                sb.Append("            new RootMotionEntry { ActionId = ").Append(e.ActionId)
                  .Append(", MotionVersion = ").Append(e.MotionVersion)
                  .Append(", SampleCount = ").Append(e.SampleCount)
                  .Append(", DirectionPolicy = (RootMotionDirectionPolicy)").Append((byte)e.DirectionPolicy)
                  .Append(", MovementPolicy = (RootMotionMovementPolicy)").Append((byte)e.MovementPolicy)
                  .Append(", CancelPolicy = (RootMotionCancelPolicy)").Append((byte)e.CancelPolicy)
                  .Append(", YPeakMm = ").Append(e.YPeakMm)
                  .Append(", DeltaLocalXzMm = new short[] { ");
                for (int i = 0; i < e.DeltaLocalXzMm.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(e.DeltaLocalXzMm[i].ToString(ci));
                }
                sb.Append(" }, DeltaYawCentiDeg = new short[] { ");
                for (int i = 0; i < e.DeltaYawCentiDeg.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(e.DeltaYawCentiDeg[i].ToString(ci));
                }
                sb.Append(" } },").Append('\n');
            }
            sb.Append("        };").Append('\n');
            sb.Append("    }").Append('\n').Append('}').Append('\n');
            File.WriteAllText(OutputPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
