#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Text;
using LiteNet.Diagnostics;

namespace LiteGame
{
    /// <summary>弱网注入只读快照（HUD/测试共用；不参与判定）。</summary>
    public readonly struct WeakNetSnapshot
    {
        public readonly bool Enabled;
        public readonly string Preset;
        public readonly WeakNetProfile Profile;
        public readonly WeakNetStats Stats;

        public WeakNetSnapshot(bool enabled, string preset, WeakNetProfile profile, WeakNetStats stats)
        {
            Enabled = enabled;
            Preset = preset ?? "none";
            Profile = profile;
            Stats = stats;
        }

        public void AppendLines(StringBuilder sb)
        {
            if (!Enabled) return;
            sb.Append("注入: ").Append(Preset)
              .Append(' ').Append(Profile.LatencyMs).Append('±').Append(Profile.JitterMs).Append("ms")
              .Append(" 丢").Append(Profile.LossPermille / 10.0).Append('%')
              .Append(" 重").Append(Profile.DuplicatePermille / 10.0).Append('%')
              .Append(" 乱").Append(Profile.ReorderPermille / 10.0).Append('%')
              .Append(" seed=").Append(Stats.Seed)
              .Append(" | 队列").Append(Stats.Queued)
              .Append(" 丢").Append(Stats.Dropped)
              .Append(" 重").Append(Stats.Duplicated)
              .Append(" 乱").Append(Stats.Reordered)
              .Append(" 溢").Append(Stats.Overflow);
        }
    }
}
#endif
