using LiteSim;
using UnityEngine;

namespace LiteGame
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
    /// <summary>
    /// **服务端身位碰撞体常驻可视化**（测试模式诊断件，release 整段剥离；开关
    /// <see cref="TestModeRuntime.DrawHeadshotDebug"/>，对局内 <b>F11</b> 即时切）。
    ///
    /// **画的就是服务端判定几何**：每个活体角色画一根**竖直圆柱**——半径取
    /// <see cref="CombatConfig.HitscanRadius"/>（命中柱：判定宽容裁决常量 0.45）、高取
    /// <see cref="CombatConfig.HitscanHeight"/>（烘焙身高）——与命中判定 <c>SimRaycast</c> 同一单源，
    /// 于是"准心落在圈内即应命中"一眼可见，所见即所判。（物理半径 <c>BodyRadius</c>=0.32 更细，不画。）
    ///
    /// <list type="bullet">
    /// <item><b>灰圈</b> = 身位圆柱的底面与顶面（脚底 / 头顶判定界）；</item>
    /// <item><b>黄圈</b> = 爆头带<b>下沿</b>（<see cref="CombatConfig.HeadHitLine"/>）——打到这里及以上即爆头；</item>
    /// <item><b>红圈</b> = 身位上沿（= 顶面，同 <see cref="CombatConfig.HitscanHeight"/>）——越过即头顶掠过打不到；</item>
    /// <item>灰竖线 = 全高母线；黄竖线 = 爆头带段（黄圈与红圈之间）。</item>
    /// </list>
    ///
    /// **画所有活体**：打谁都能看到那根柱在哪（含本地自己与补位 bot）。死亡实体不画。
    ///
    /// **数据源**：本地<b>预测态</b> <c>RollbackSim.State</c>（只读）——位置与 Sim 判定同源，
    /// 不看视图 Transform（模型 pivot/插值会让柱偏离实际判定）。
    /// **只读不改 Sim**，不改任何判定。绘制面 <see cref="Debug.DrawLine"/>（Scene/Game 视图可见）。
    /// </summary>
    public sealed class BattleHeadshotDebugDrawer
    {
        private readonly RollbackSim _sim;

        /// <summary>水平圆的折线段数（诊断观感——段数越高越圆，绘制量线性增长）。</summary>
        private const int Segments = 20;

        /// <summary>身位圆柱轮廓色（灰——与爆头带的两色区分：柱=判定边界，带=奖励区）。</summary>
        private static readonly Color BodyColor = new Color(0.65f, 0.65f, 0.65f);

        public BattleHeadshotDebugDrawer(RollbackSim sim)
        {
            _sim = sim;
        }

        /// <summary>每渲染帧重画（常驻跟随）。开关关时立即返回（零开销）。</summary>
        public void Tick()
        {
            if (!TestModeRuntime.DrawHeadshotDebug) return;
            SimWorldState s = _sim.State;

            float radius = CombatConfig.HitscanRadius;
            float bodyTop = CombatConfig.HitscanHeight;

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((s.AliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                ref EntitySlot e = ref s.Entities[i];
                if (e.Hp <= 0) continue;                                          // 尸体非有效目标

                Vector3 foot = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                Vector3 top = foot + Vector3.up * bodyTop;                        // 身位上沿（= 顶面）
                Vector3 headLow = foot + Vector3.up * CombatConfig.HeadHitLine;   // 爆头带下沿

                // 服务端身位圆柱（完整判定几何）：底/顶圈 + 全高母线
                DrawCircle(foot, radius, BodyColor);
                DrawCircle(top, radius, BodyColor);
                DrawRibs(foot, top, radius, BodyColor);

                // 爆头带段强调（奖励区）：下沿黄圈 + 带内黄竖线（上沿即红圈=顶面）
                DrawCircle(headLow, radius, Color.yellow);
                DrawCircle(top, radius, Color.red);
                DrawRibs(headLow, top, radius, Color.yellow);
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
