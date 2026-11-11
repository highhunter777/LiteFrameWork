using System;
using System.Collections.Generic;
using LiteGame.Modules.Battle.SubModules.ArenaSubModule.RootMotion;
using LiteGame.Toolchain;
using LiteSim;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
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

    /// <summary>烘焙配置资产（引用 CombatGirls 包内片段——包不入库，此资产随工具收编迁至 Baking/；
    /// 产物 `rootmotion.bin` 才是共享物）。经 <see cref="BakingToolbox"/>「根位移」页执行。</summary>
    [CreateAssetMenu(fileName = "RootMotionBakeConfig", menuName = "LiteSim/根位移烘焙配置")]
    public class RootMotionBakeConfigAsset : ScriptableObject
    {
        public List<RootMotionBakeItem> Items = new List<RootMotionBakeItem>();
    }

    /// <summary>
    /// 根位移烘焙器（烘焙工具箱「根位移」页，或 run_script 调用；《联机动画与根位移专项设计》§3 制作链： 采样 → 数值规范化/量化 → 校验 → 导出两端共享数据产物 → 摘要）。
    /// </summary>
    public static class RootMotionBaker
    {
        private const string ConfigPath = BakeTargets.RootMotionConfigPath;
        private const int MaxDeltaMm = 2000;        // 单 tick 位移上限（2m/tick = 120 m/s——超出按恶意资产拒绝）

        public static string All()
        {
            return All(null);
        }

        public static string All(ToolWriteScope writeScope)
        {
            if (!string.Equals(BakeTargets.RootMotionBinOutput, RootMotionCatalog.DefaultAssetPath, StringComparison.Ordinal))
            {
                return "ABORT 产物路径与运行时装载路径不一致：" + BakeTargets.RootMotionBinOutput
                    + " ≠ " + RootMotionCatalog.DefaultAssetPath;
            }

            var config = AssetDatabase.LoadAssetAtPath<RootMotionBakeConfigAsset>(ConfigPath);
            if (config == null)
                return "ABORT 缺烘焙配置:" + ConfigPath + "——右键 Create>LiteSim>根位移烘焙配置 建一件并登记动作";
            if (config.Items == null || config.Items.Count <= 0)
                return "ABORT 烘焙配置为空（零条目禁止出空产物——§3 不许静默零位移）";

            var entries = new List<RootMotionEntry>(config.Items.Count);
            var errors = new System.Text.StringBuilder();
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
                return "ABORT 烘焙中止，失败清单：\n" + errors;

            int tickRate = SimConfig.TickRate;
            byte[] bytes;
            RootMotionCatalog catalog;
            try
            {
                bytes = RootMotionCatalog.Encode(tickRate, entries);
                catalog = RootMotionCatalog.Load(bytes);          // 同一格式自校验（写盘前即失败）
            }
            catch (Exception ex)
            {
                return "ABORT 编码/自校验失败：" + ex.Message;
            }

            string sampleDigest = RootMotionDigest.Compute(entries, tickRate);
            string decodedDigest = RootMotionDigest.Compute(catalog.Entries, tickRate);
            if (!string.Equals(sampleDigest, decodedDigest, StringComparison.Ordinal))
                return "ABORT 编码往返丢字段（样本摘要不一致）：写入前 " + sampleDigest + "，读回 " + decodedDigest;

            string write = BakeTargets.WriteArtifactBytes(BakeTargets.RootMotionBinOutput, bytes, writeScope);

            return "已导出：" + BakeTargets.RootMotionBinOutput
                + "\n  条目数=" + catalog.Entries.Count
                + "\n  tickRate=" + catalog.TickRate
                + "\n  bytes=" + bytes.Length
                + "\n  sha256=" + catalog.Digest
                + "\n  " + write
                + "\n  数据产物：改后无需重生成 BuildHash，但需重发内容包（Config 收集组）；"
                + "运行时装载见对局视图装配（BattleViewRig）。";
        }

        /// <summary>烘焙单条；返回 null=成功，否则错误文本（含动作/帧定位）。</summary>
        private static string BakeOne(RootMotionBakeItem item, List<RootMotionEntry> entries)
        {
            if (item.Clip == null) return $"ActionId {item.ActionId}: 未指定 AnimationClip";
            if (!item.Clip.humanMotion) return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」非人形（仅支持 RootT 曲线，拒绝静默回退）";

            AnimationCurve cx = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.x"));
            AnimationCurve cy = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.y"));
            AnimationCurve cz = AnimationUtility.GetEditorCurve(item.Clip, RootBinding("RootT.z"));
            if (cx == null || cy == null || cz == null)
                return $"ActionId {item.ActionId}: 片段「{item.Clip.name}」缺 RootT 曲线（导入未烘焙根运动）";

            int tickRate = SimConfig.TickRate;
            int sampleCount = Math.Max(1, (int)Math.Round(item.Clip.length * tickRate,
                MidpointRounding.AwayFromZero) - 1);
            var dxz = new short[sampleCount * 2];
            var dyaw = new short[sampleCount];   // 恒零（DirectionPolicy=起始锁定）
            int yPeakMm = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                float t0 = (float)i / tickRate;
                float t1 = (float)(i + 1) / tickRate;
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
    }
}
