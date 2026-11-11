using LiteSim;
using UnityEngine;
using LiteGame.Adapters.Dev.Debug.Session;

namespace LiteGame.Adapters.Dev.Battle
{
    using Debug = UnityEngine.Debug;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
    /// <summary>
    /// **服务端身位碰撞体常驻可视化**（测试模式诊断件，release 整段剥离）。
    /// </summary>
    public sealed class BattleHeadshotDebugDrawer
    {
        private readonly RollbackSim _sim;

        /// <summary>本局规则提供者（对局上下文持有；null = 无测试规则 → 用烘焙默认）。</summary>
        private readonly System.Func<MatchTestRules> _rules;

        /// <summary>水平圆的折线段数（诊断观感——段数越高越圆，绘制量线性增长）。</summary>
        private const int Segments = 20;

        /// <summary>身位圆柱轮廓色（灰——与爆头带的两色区分：柱=判定边界，带=奖励区）。</summary>
        private static readonly Color BodyColor = new Color(0.65f, 0.65f, 0.65f);

        public BattleHeadshotDebugDrawer(RollbackSim sim, System.Func<MatchTestRules> rules = null)
        {
            _sim = sim;
            _rules = rules;
        }

        /// <summary>每渲染帧重画（常驻跟随）。开关关时立即返回（零开销）。</summary>
        public void Tick()
        {
            if (!TestModeRuntime.Current.DrawHeadshotDebug) return;
            SimWorldState s = _sim.State;

            float radius = CombatConfig.Geometry.HitscanRadius;
            float bodyTop = CombatConfig.Geometry.HitscanHeight;

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp <= 0) continue;                                          // 尸体非有效目标

                Vector3 foot = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                Vector3 top = foot + Vector3.up * bodyTop;                        // 身位上沿（= 顶面）
                Vector3 headLow = foot + Vector3.up * CombatConfig.Geometry.HeadHitLine;   // 双柱分界（表几何）
                float headR = CombatConfig.Geometry.HeadshotRadius;                       // 爆头柱半径（表几何）

                // 服务端命中几何＝双柱阶梯：灰＝身体柱 [0, 分界) × 命中柱全径（含台阶顶面圈）；
                // 黄红＝爆头柱 [分界, 顶] × 爆头柱半径。分界以上的环状空隙按裁决不可命中——不画。
                DrawCircle(foot, radius, BodyColor);
                DrawCircle(headLow, radius, BodyColor);                           // 身体柱顶面（= 双柱台阶）
                DrawRibs(foot, headLow, radius, BodyColor);

                // 爆头柱切片（Crit 区）：下沿黄圈 + 带内黄竖线 + 上沿红圈。
                DrawCircle(headLow, headR, Color.yellow);
                DrawCircle(top, headR, Color.red);
                DrawRibs(headLow, top, headR, Color.yellow);
            }
        }

        /// <summary>水平圆（<see cref="Segments"/> 段折线）。</summary>
        private static void DrawCircle(Vector3 center, float radius, Color col)
        {
            float step = 2f * Mathf.PI / Segments;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int k = 1; k <= Segments; k++)
            {
                float a = step * k;
                Vector3 cur = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Debug.DrawLine(prev, cur, col);
                prev = cur;
            }
        }

        /// <summary>四条母线（0°/90°/180°/270° 方位，连接 <paramref name="low"/> 与 <paramref name="top"/>）。</summary>
        private static void DrawRibs(Vector3 low, Vector3 top, float radius, Color col)
        {
            Debug.DrawLine(low + new Vector3(radius, 0f, 0f), top + new Vector3(radius, 0f, 0f), col);
            Debug.DrawLine(low + new Vector3(-radius, 0f, 0f), top + new Vector3(-radius, 0f, 0f), col);
            Debug.DrawLine(low + new Vector3(0f, 0f, radius), top + new Vector3(0f, 0f, radius), col);
            Debug.DrawLine(low + new Vector3(0f, 0f, -radius), top + new Vector3(0f, 0f, -radius), col);
        }
    }
#endif
}
