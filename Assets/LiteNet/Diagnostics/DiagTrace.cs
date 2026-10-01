using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace LiteNet.Diagnostics
{
    /// <summary>
    /// 一条**结构化诊断记录**（《框架先行建设与业务接入专项设计》§8「可诊断」：
    /// "一次注入失败可关联 Build/内容、事务、会话/房间并定位阶段"——证据=受控日志/错误码/诊断产物）。
    ///
    /// 与 <c>LiteFramework.Log</c> 的分工（不是第二套日志）：<c>Log</c> 是**人读**的环形文本面
    /// （Tag + Message，32 条）；本件是**机读**的结构化面——固定字段、稳定分类码、可断言、可落产物，
    /// 供跨端（客户端/服务端）与跨域（版本/内容事务/会话/房间）**按同一个关联键对齐**。
    ///
    /// 纪律：
    /// - <see cref="Stage"/>/<see cref="Code"/> 是**稳定词表**（新阶段/新分类才加常量，动态值进
    ///   <see cref="Key"/>/<see cref="Detail"/>）；
    /// - **不记录凭据**（票据/token/密钥一律不落——同《Meta 专项》§13.1）；
    /// - 容量有界（<see cref="Capacity"/>），满了滚出最旧——它是诊断面不是审计面。
    /// </summary>
    public readonly struct DiagEvent
    {
        /// <summary>记录序号（进程内单调，便于按序读产物）。</summary>
        public readonly long Seq;
        /// <summary>记录时刻（Environment.TickCount 毫秒；同 <c>LogEntry.Tick</c> 的口径）。</summary>
        public readonly long TickMs;
        /// <summary>阶段（域段名：见 <see cref="DiagStage"/>）。</summary>
        public readonly string Stage;
        /// <summary>稳定分类码（见 <see cref="DiagCode"/>；可断言、可工单化）。</summary>
        public readonly string Code;
        /// <summary>关联键（<see cref="DiagTrace.JoinKey"/> 产出的复合键——跨端/跨域据此对齐）。</summary>
        public readonly string Key;
        /// <summary>细节（原因长句/对照值；可空）。</summary>
        public readonly string Detail;

        public DiagEvent(long seq, long tickMs, string stage, string code, string key, string detail)
        {
            Seq = seq; TickMs = tickMs; Stage = stage; Code = code; Key = key; Detail = detail;
        }
    }

    /// <summary>阶段词表（稳定；动态值不进这里）。</summary>
    public static class DiagStage
    {
        /// <summary>版本/构建身份段（buildHash 红线、握手版本比对）。</summary>
        public const string Build = "Build";
        /// <summary>内容段（发布身份/内容代次/候选）。</summary>
        public const string Content = "Content";
        /// <summary>更新事务段（激活事务：候选→待激活→确认/失败）。</summary>
        public const string Txn = "Txn";
        /// <summary>会话段（客户端会话状态机）。</summary>
        public const string Session = "Session";
        /// <summary>房间段（宿主进房判定与房间运行时拒绝）。</summary>
        public const string Room = "Room";
    }

    /// <summary>分类码词表（稳定；新分类才加常量——不用自由文本当码）。</summary>
    public static class DiagCode
    {
        /// <summary>进房被拒：buildHash 与服务器不符（版本红线）。</summary>
        public const string JoinRejectedBuildHash = "JoinRejectedBuildHash";
        /// <summary>进房被拒：票据拒绝（分类见 detail）。</summary>
        public const string JoinRejectedTicket = "JoinRejectedTicket";
        /// <summary>进房被拒：排空/容量/限流/字段边界等（分类见 detail）。</summary>
        public const string JoinRejectedAdmission = "JoinRejectedAdmission";
        /// <summary>进房被拒：房间运行时拒绝（已关闭/已满——房间侧输出）。</summary>
        public const string JoinRejectedRoom = "JoinRejectedRoom";
        /// <summary>客户端：发起进房（记录带内容/事务上下文——与服务器拒绝记录同键，两端据此对齐）。</summary>
        public const string JoinAttempt = "JoinAttempt";
        /// <summary>客户端：发起进房后未收 JoinAck 即断线（服务器未回拒绝原因时的客户端侧事实）。</summary>
        public const string JoinNoAckDisconnected = "JoinNoAckDisconnected";
        /// <summary>内容激活事务失败留档。</summary>
        public const string ActivationFailed = "ActivationFailed";
        /// <summary>内容激活事务中断恢复（启动恢复决策落地）。</summary>
        public const string ActivationRecovered = "ActivationRecovered";
    }

    /// <summary>
    /// 结构化诊断记录面（有界环形 + 关联键构造 + 产物文本）。**零依赖件**（LiteNet 双轨编译：
    /// Unity 客户端与服务端 RoomServer 共用同一份源码——两端记录天然同形，关联键可对齐）。
    ///
    /// 谁该发：**掌握稳定分类码的判定点**——宿主进房判定（<c>ServerHost</c>）、客户端会话状态机
    /// （<c>RoomClient</c>）、内容激活事务落点（组合根，见 <see cref="DiagCode.ActivationFailed"/>）。
    /// 谁不该发：每帧路径/临时调试（那是 <c>Log</c> 的活）。
    /// </summary>
    public static class DiagTrace
    {
        /// <summary>记录容量（有界；满了滚出最旧——诊断面不承担审计）。</summary>
        public const int Capacity = 64;

        private static readonly DiagEvent[] _ring = new DiagEvent[Capacity];
        private static readonly object _gate = new object();
        private static int _start;
        private static int _count;
        private static long _seq;
        private static readonly RingView _recent = new RingView();

        /// <summary>最近记录（index 0 = 最旧）；读面零分配。</summary>
        public static IReadOnlyList<DiagEvent> Recent => _recent;

        /// <summary>累计记录数（含已滚出——诊断计数）。</summary>
        public static long Total
        {
            get { lock (_gate) return _seq; }
        }

        /// <summary>
        /// 记一条（线程安全——宿主多线程调用，客户端主线程调用）。stage/code 用词表常量；
        /// key 用 <see cref="JoinKey"/> 等构造器保证**两端同形**。
        /// </summary>
        public static void Emit(string stage, string code, string key = null, string detail = null)
        {
            lock (_gate)
            {
                long seq = ++_seq;
                var e = new DiagEvent(seq, Environment.TickCount, stage, code, key, detail);
                _ring[(_start + _count) % Capacity] = e;
                if (_count < Capacity) _count++;
                else _start = (_start + 1) % Capacity;
            }
        }

        /// <summary>复位（测试夹具用；同 <c>Log.ResetForTesting</c> 的纪律）。</summary>
        public static void ResetForTesting()
        {
            lock (_gate)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _start = 0;
                _count = 0;
                _seq = 0;
            }
        }

        /// <summary>
        /// 关联键构造（**单一来源**——两端/各域都用它拼键，字段序固定、显式占位：
        /// 空值写 "-"，不使用平台换行，保证端与端、进程与进程可比）。
        /// </summary>
        public static string JoinKey(string roomId, int playerId, string buildHash)
            => "room=" + (string.IsNullOrEmpty(roomId) ? "-" : roomId)
             + ";player=" + playerId
             + ";build=" + (string.IsNullOrEmpty(buildHash) ? "-" : buildHash);

        /// <summary>单行文本（定序、可 grep：`#{seq} {stage} {code} key={key} {detail}`）。</summary>
        public static string Line(in DiagEvent e)
            => "#" + e.Seq + " " + e.Stage + " " + e.Code
             + " key=" + (e.Key ?? "-")
             + (string.IsNullOrEmpty(e.Detail) ? "" : " | " + e.Detail);

        /// <summary>全部记录的稳定文本（一行一条；诊断产物内容——由调用方落盘）。</summary>
        public static string Format()
        {
            var sb = new StringBuilder(Capacity * 64);
            lock (_gate)
            {
                for (int i = 0; i < _count; i++)
                    sb.Append(Line(in _ring[(_start + i) % Capacity])).Append('\n');
            }
            return sb.ToString();
        }

        private sealed class RingView : IReadOnlyList<DiagEvent>
        {
            public DiagEvent this[int i]
            {
                get
                {
                    lock (_gate)
                    {
                        if (i < 0 || i >= _count) throw new ArgumentOutOfRangeException(nameof(i));
                        return _ring[(_start + i) % Capacity];
                    }
                }
            }

            public int Count { get { lock (_gate) return _count; } }

            public IEnumerator<DiagEvent> GetEnumerator()
            {
                for (int i = 0; i < Count; i++) yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}