using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LiteGame.Modules.Battle.SubModules.ArenaSubModule.RootMotion
{
    /// <summary>
    /// 根位移样本摘要（烘焙侧确定性校验工具）——规范化文本与 CombatConfigDigest 同纪律： 字段序固定、InvariantCulture、'\n' 分隔、无空白填充。
    /// </summary>
    public static class RootMotionDigest
    {
        public static string Compute(IReadOnlyList<RootMotionEntry> entries, int tickRate)
        {
            return Compute(CanonicalText(entries, tickRate));
        }

        /// <summary>规范化文本：条目按产物序（ActionId 升序），逐条
        /// "actionId/motionVersion/sampleCount/policy 三元组/yPeak/全部增量（XZ 交错，后 yaw）"。</summary>
        public static string CanonicalText(IReadOnlyList<RootMotionEntry> entries, int tickRate)
        {
            var sb = new StringBuilder(1024);
            sb.Append("tickRate").Append('\n').Append(tickRate).Append('\n');
            sb.Append("entryCount").Append('\n').Append(entries.Count).Append('\n');
            for (int e = 0; e < entries.Count; e++)
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
