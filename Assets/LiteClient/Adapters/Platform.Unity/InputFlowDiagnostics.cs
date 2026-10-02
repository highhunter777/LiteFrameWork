using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteClient
{
    /// <summary>
    /// 输入链现场记录器（[Diag] 临时哨位——"编辑器里移不动玩家"的定性工具）：
    /// **每层各记一位**，断点定位一锤定音：
    /// - L 位 = legacy <c>UnityEngine.Input</c>（OS→引擎事件泵，双通道对照面）
    /// - I 位 = Input System 键盘设备（Keyboard.current——引擎→InputSystem 设备面）
    /// - A 位 = Move action 输出（InputSystem→action 绑定面）
    /// 采样：InputSystem.onAfterUpdate 每帧；**状态变化才记 + 2 秒心跳行**（断流时段可见，H 尾标）。
    ///
    /// **自挂形态**（[RuntimeInitializeOnLoadMethod]——每局 Play 自动武装，域重载随局清零重挂）。
    /// 读取：<see cref="Dump"/>（eval/DevHUD 均可消费）；环缓冲 4096 行。
    ///
    /// 居住地注意：本文件住 Platform.Unity（InputSystem import 只许在此目录）；
    /// **活体 Move action 按名反射取**（GameEntry/ProcedureBattle 是产品侧 LiteGame.App——框架侧
    /// 程序集不得引用产品程序集，纯度红线；Type.GetType 按名跨界仅为诊断读取，release 随宏剥离）。
    /// 判读：L0I0=事件没进引擎；L1I0=InputSystem 设备面丢；L1I1A0=action 绑定层丢；
    /// 三层全通还移不动=意图/采样层。
    /// </summary>
    public static class InputFlowDiagnostics
    {
        private const int Capacity = 4096;
        private static readonly string[] _ring = new string[Capacity];
        private static int _head;          // 下一写入位
        private static int _count;

        private static int _lastBits = -1; // L×16 + I（WASD 位序 8421）
        private static Vector2 _lastVal = new Vector2(-99f, -99f);
        private static Vector2 _lastPending = new Vector2(-99f, -99f);
        private static Vector2 _lastSimPos = new Vector2(-99f, -99f);
        private static double _lastRow;
        private static bool _armed;
        private static InputAction _move;      // 反射取活体 Move action（未进对局时容忍 null）
        private static IInputService _input;  // 反射取活体输入服务（P 位来源；同容忍）
        private static BattleContext _battle;  // 反射取活体对局上下文（S 位来源；LiteClient.Runtime 本程序集可直引）

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Arm()
        {
            if (_armed) return;
            _armed = true;
            _lastBits = -1;
            _lastRow = 0;
            InputSystem.onAfterUpdate += Sample;
        }

        private static void Sample()
        {
            // L 位：legacy（与 IS 双通道对照）。ProjectSettings 若只开 IS 会抛——那本身就是要记的事实。
            int legacy;
            try
            {
                legacy = (Input.GetKey(KeyCode.W) ? 8 : 0) | (Input.GetKey(KeyCode.A) ? 4 : 0)
                    | (Input.GetKey(KeyCode.S) ? 2 : 0) | (Input.GetKey(KeyCode.D) ? 1 : 0);
            }
            catch (Exception)
            {
                legacy = 0;                              // legacy 通道被关（Active Input Handling 只开 IS）
            }

            var kb = Keyboard.current;
            int isBits = kb == null ? -1
                : (kb.wKey.isPressed ? 8 : 0) | (kb.aKey.isPressed ? 4 : 0)
                  | (kb.sKey.isPressed ? 2 : 0) | (kb.dKey.isPressed ? 1 : 0);

            ResolveMove();
            Vector2 val = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            double t = Time.unscaledTimeAsDouble;

            // P 位：采样链最后一环——活体待用意图（"按键通还移不动"的定位缺口：A 通 P 断 =
            // action 出值但采样/上下文门层没消费；A 通 P 也通还不动 = Sim/权威执行层）。
            Vector2 pending = _input != null ? new Vector2(_input.Pending.MoveX, _input.Pending.MoveZ) : Vector2.zero;

            // S 位：本地预测位置（"按键全通但画面不动"的分水岭——S 在动=玩家实际在动，画面不动是
            // 相机/视图层；S 不动=Sim/权威执行层）。0.5m 量化记变化，防移动期逐帧刷行。
            Vector2 simPos = Vector2.zero;
            int simFrame = -1;
            if (_battle != null)
            {
                var p = _battle.LocalPosition;                 // Sim 预测态位置（服务器路径同源）
                simPos = new Vector2(Mathf.Round(p.X * 2f) / 2f, Mathf.Round(p.Z * 2f) / 2f);
                simFrame = _battle.Sim != null ? _battle.Sim.State.Frame : -1;
            }

            int bits = legacy * 16 + Math.Max(isBits, 0);
            bool changed = bits != _lastBits || (val - _lastVal).sqrMagnitude > 0.0001f
                || (pending - _lastPending).sqrMagnitude > 0.0001f
                || (simPos - _lastSimPos).sqrMagnitude > 0.25f;   // ≥0.5m 才算位置变化
            if (!changed && t - _lastRow < 2.0) return;  // 无变化且心跳未到：不记
            bool heartbeat = !changed;                    // 心跳行：无变化但仍记（断流时段可见）
            _lastBits = bits;
            _lastVal = val;
            _lastPending = pending;
            _lastSimPos = simPos;
            _lastRow = t;

            var sb = new StringBuilder(128);
            sb.Append(t.ToString("F2")).Append(" L=").Append(legacy)
              .Append(" I=").Append(isBits < 0 ? "null" : isBits.ToString())
              .Append(" kbUpd=").Append(kb != null ? (t - (double)kb.lastUpdateTime).ToString("F1") : "-")
              .Append(" A=").Append(_move == null ? "noBattle" : val.ToString("F2"))
              .Append(" P=").Append(pending.ToString("F2"))
              .Append(" S=").Append(_battle != null ? simPos.ToString("F1") + "@" + simFrame : "noBattle")
              .Append(heartbeat ? " H" : "");
            Append(sb.ToString());
        }

        /// <summary>
        /// 按名反射取活体 Move action（进对局后可用）。跨程序集按名（Type.GetType）——
        /// GameEntry/ProcedureBattle 在产品侧 LiteGame.App，框架侧不得编译期引用（纯度红线）；
        /// 任一环缺失（未进对局/替身装配）都按"无 action 可读"，心跳照记。
        /// </summary>
        private static void ResolveMove()
        {
            if (_move != null) return;
            try
            {
                Type entryType = Type.GetType("LiteGame.GameEntry, LiteGame.App");
                if (entryType == null) return;
                UnityEngine.Object entry = UnityEngine.Object.FindAnyObjectByType(entryType);
                if (entry == null) return;

                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object fsm = entryType.GetField("_fsm", F)?.GetValue(entry);
                object current = fsm?.GetType().GetField("_current", F)?.GetValue(fsm);
                if (current == null) return;
                object input = current.GetType().GetField("_input", F)?.GetValue(current);
                if (!(input is IInputService svc)) return;
                _input = svc;   // P 位来源（与 action 同链解析；本程序集引用 LiteClient.Runtime ✓）
                if (!(current.GetType().GetField("_context", F)?.GetValue(current) is BattleContext ctx)) return;
                _battle = ctx;  // S 位来源（本地预测位——LiteClient.Runtime 本程序集可直引，非按名反射）

                // IInputService 在 LiteClient.Runtime（本程序集引用 ✓）；Source 具体类型就在本程序集
                if (!(svc.Source is NewInputIntentSource src)) return;
                object actions = typeof(NewInputIntentSource).GetField("_actions", F)?.GetValue(src);
                if (actions == null) return;
                object gamePlay = actions.GetType().GetProperty("GamePlay")?.GetValue(actions);
                _move = gamePlay?.GetType().GetProperty("Move")?.GetValue(gamePlay) as InputAction;
            }
            catch (Exception)
            {
                // 诊断读取失败即静默——不影响任何功能路径
            }
        }

        private static void Append(string line)
        {
            _ring[_head] = line;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>取最近 <paramref name="tail"/> 行（eval 读取口）。</summary>
        public static string Dump(int tail = 40)
        {
            var sb = new StringBuilder(4096);
            sb.Append("armed=").Append(_armed).Append(" lines=").Append(_count).Append('\n');
            int n = Math.Min(tail, _count);
            int start = (_head - n + Capacity) % Capacity;
            for (int i = 0; i < n; i++)
            {
                int idx = (start + i) % Capacity;
                if (_ring[idx] != null) sb.Append(_ring[idx]).Append('\n');
            }
            return sb.ToString();
        }
    }
}
#endif
