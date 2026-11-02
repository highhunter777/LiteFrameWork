using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LiteSim
{
    /// <summary>
    /// 地图内容规范化摘要（烘焙产物行内 `content_digest` 的单源）：两端投影后按**同一算法**复算比对——
    /// 不等即"json→bytes→投影"链路走样（fail-fast 拒载）。
    ///
    /// 规范文本：标签行 ＋ 逐字段行（顺序固定、InvariantCulture、数值 "0.###"、'\n' 分隔、无空白）；
    /// 摘取值 ＝ SHA-256（规范文本）**前 16 hex**（小写）。覆盖面 ＝ 判定载荷（边界/障碍/出生点）——
    /// 不含 map_id/name（身份字段：改名/换 id 不改几何摘要）。
    /// </summary>
    public static class SimMapDigest
    {
        public const string SchemeTag = "map-digest-v1";

        public static string CanonicalText(SimMapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var sb = new StringBuilder(128 + map.ObstacleCount * 56 + map.SpawnPointCount * 28);
            sb.Append(SchemeTag).Append('\n');
            sb.Append("ground_y ").Append(Fmt(map.GroundY)).Append('\n');
            sb.Append("half_width ").Append(Fmt(map.HalfWidth)).Append('\n');
            sb.Append("half_depth ").Append(Fmt(map.HalfDepth)).Append('\n');
            sb.Append("obstacles ").Append(map.ObstacleCount).Append('\n');
            for (int i = 0; i < map.ObstacleCount; i++)
            {
                ref SimObstacle o = ref map.Obstacles[i];
                sb.Append("o ").Append(i).Append(' ').Append((int)o.Kind)
                  .Append(' ').Append(Fmt(o.Center.X)).Append(' ').Append(Fmt(o.Center.Y)).Append(' ').Append(Fmt(o.Center.Z))
                  .Append(' ').Append(Fmt(o.Radius)).Append(' ').Append(Fmt(o.HalfX)).Append(' ').Append(Fmt(o.HalfZ))
                  .Append(' ').Append(Fmt(o.Height)).Append(' ').Append((int)o.Surface).Append('\n');
            }
            sb.Append("spawns ").Append(map.SpawnPointCount).Append('\n');
            for (int i = 0; i < map.SpawnPointCount; i++)
            {
                ref SimVector3 p = ref map.SpawnPoints[i];
                sb.Append("s ").Append(i).Append(' ').Append(Fmt(p.X)).Append(' ').Append(Fmt(p.Y)).Append(' ').Append(Fmt(p.Z)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>内容摘要（16 hex 小写）——烘焙写进行内字段、两端投影后复算比对。</summary>
        public static string Compute(SimMapData map) => ComputeText(CanonicalText(map));

        /// <summary>规范文本级摘要（测试/工具入口）。</summary>
        public static string ComputeText(string canonicalText)
        {
            if (canonicalText == null) throw new ArgumentNullException(nameof(canonicalText));
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalText));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>数值格式单源（"0.###"、InvariantCulture；0/-0 归一为 "0"）——规范文本与烘焙 JSON 产出共用，
        /// 保证"写进 map.json 的数值文本"与"摘要复算读到的数值文本"逐字一致。</summary>
        public static string Fmt(float value)
            => value == 0f ? "0" : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
