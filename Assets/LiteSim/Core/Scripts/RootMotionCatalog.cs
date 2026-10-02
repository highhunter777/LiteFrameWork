using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LiteSim
{
    /// <summary>根位移方向策略（《联机动画与根位移专项设计》§3 DeltaYaw / DirectionPolicy）。
    /// 起始锁定 = 动作全程按发起朝向旋转局部增量（逐帧 yaw 数组恒零）；
    /// 其余值预留（逐帧转体/有限输入修正——接入时两端同步定稿）。</summary>
    public enum RootMotionDirectionPolicy : byte
    {
        StartFacingLocked = 0,
        FrameByFrame = 1,
        InputCorrected = 2,
    }

    /// <summary>动作期间普通输入位移策略（§3 MovementPolicy：禁用/叠加/有限修正——执行层消费）。</summary>
    public enum RootMotionMovementPolicy : byte
    {
        Disable = 0,
        Stack = 1,
        Limited = 2,
    }

    /// <summary>动作取消策略（§3 CancelPolicy：取消帧窗与取消后速度归属由执行层按策略定稿）。</summary>
    public enum RootMotionCancelPolicy : byte
    {
        NotCancellable = 0,
        Windowed = 1,
    }

    /// <summary>单个动作的烘焙根位移（§3 位移数据契约：**只读玩法数据**，服务端不加载
    /// AnimationClip/Avatar/Animator——两端经生成物共享同一份数值）。
    ///
    /// 量化纪律（烘焙侧与消费侧同源定稿——§3"单位、舍入规则和计算次序在 Sim 侧定稿"）：
    /// - 平面增量：**毫米有符号整数**（short，±32.767m 单 tick 上限恒安全）；
    /// - Yaw：**厘度**（1/100 度，short）——DirectionPolicy=起始锁定，数组恒零；
    /// - 舍入：round half away from zero（正负对称，无银行家舍入歧义）；
    /// - 采样：固定 60Hz（TickRate）；SampleCount = max(1, round(clip 时长×60) − 1) 个
    ///   "帧 i → 帧 i+1"增量；循环片段不回绕（尾帧差按曲线末值持位——原地走跑不受影响）。
    /// Y 轴：YPolicy 默认沿用 Sim 重力/地面（§3）——烘焙只记录 <see cref="YPeakMm"/> 供校验，
    /// 不把视觉抬升写进权威位移。</summary>
    public sealed class RootMotionEntry
    {
        public int ActionId;
        public int MotionVersion;

        /// <summary>每 tick 位移增量数（有效索引 i ∈ [0, SampleCount)，i = SimFrame − StartFrame）。</summary>
        public int SampleCount;

        public RootMotionDirectionPolicy DirectionPolicy;
        public RootMotionMovementPolicy MovementPolicy;
        public RootMotionCancelPolicy CancelPolicy;

        /// <summary>烘焙期检测到的 Y 位移峰值（毫米）——校验用（YPolicy=Sim 重力时非零应复查源资产）。</summary>
        public int YPeakMm;

        /// <summary>平面局部增量（毫米）：[2i]=X、[2i+1]=Z；长度恒 = 2×SampleCount。</summary>
        public short[] DeltaLocalXzMm;

        /// <summary>每 tick 偏航（厘度）：长度恒 = SampleCount（恒零）。</summary>
        public short[] DeltaYawCentiDeg;
    }

    /// <summary>根位移目录（生成部分见 RootMotionData.g.cs——烘焙器产物，勿手改）。
    /// 本文件只放契约与摘要算法（两端 + L1 同源复算）。</summary>
    public static partial class RootMotionCatalog
    {
        /// <summary>烘焙采样率＝Sim 逻辑帧率（§3 固定 60Hz）。</summary>
        public const int TickRate = SimConfig.TickRate;

        /// <summary>烘焙产物摘要（64 位 hex）——对规范化样本、动作帧数与全部影响判定的策略求摘要（§3
        /// ContentDigest）；对局准入两端校验。≠ CombatConfigDigest（那个只覆盖数值装载面）。</summary>
        public static string ContentDigest => RootMotionDigest.Compute(Entries);
    }

    /// <summary>根位移摘要（SHA-256 全量 hex）——规范化文本与 <see cref="CombatConfigDigest"/> 同纪律：
    /// 字段序固定、InvariantCulture、'\n' 分隔、无空白填充。烘焙器写入 .g.cs 前调用，
    /// L1 用例按同规则复算比对（防手改生成物）。</summary>
    public static class RootMotionDigest
    {
        public static string Compute(RootMotionEntry[] entries)
        {
            return Compute(CanonicalText(entries));
        }

        /// <summary>规范化文本：条目按烘焙序（已按 ActionId 升序生成），逐条
        /// "actionId/motionVersion/sampleCount/policy 三元组/yPeak/全部增量（XZ 交错，后 yaw）"。</summary>
        public static string CanonicalText(RootMotionEntry[] entries)
        {
            var sb = new StringBuilder(1024);
            sb.Append("tickRate").Append('\n').Append(RootMotionCatalog.TickRate).Append('\n');
            sb.Append("entryCount").Append('\n').Append(entries.Length).Append('\n');
            for (int e = 0; e < entries.Length; e++)
            {
                RootMotionEntry entry = entries[e];
                sb.Append(entry.ActionId).Append('\n')
                  .Append(entry.MotionVersion).Append('\n')
                  .Append(entry.SampleCount).Append('\n')
                  .Append((byte)entry.DirectionPolicy).Append('\n')
                  .Append((byte)entry.MovementPolicy).Append('\n')
                  .Append((byte)entry.CancelPolicy).Append('\n')
                  .Append(entry.YPeakMm).Append('\n');
                for (int i = 0; i < entry.SampleCount; i++)
                    sb.Append(entry.DeltaLocalXzMm[2 * i]).Append('\n')
                      .Append(entry.DeltaLocalXzMm[2 * i + 1]).Append('\n');
                for (int i = 0; i < entry.SampleCount; i++)
                    sb.Append(entry.DeltaYawCentiDeg[i]).Append('\n');
            }
            return sb.ToString();
        }

        public static string Compute(string canonicalText)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalText);
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(64);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
