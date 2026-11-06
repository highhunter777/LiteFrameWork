using System;
using System.Collections.Generic;
using RoomServer.Application;

namespace RoomServer
{
    /// <summary>
    /// 重试控制队列满载时暂存的断线事实（自 <c>ServerHost</c> 按整块搬移判据外提：自有状态 + 单提交端口， 块内互调、无外部调用点）。
    /// </summary>
    internal sealed class PendingDisconnectQueue
    {
        private readonly object _gate = new object();
        private readonly List<Entry> _pending = new List<Entry>();
        private readonly Func<RoomInstance, int, Session, bool> _submit;

        /// <summary>提交端口（宿主注入）：断线命令是否成功进入房间控制 Mailbox——true 即视为已消费，出队。</summary>
        public PendingDisconnectQueue(Func<RoomInstance, int, Session, bool> submit)
            => _submit = submit ?? throw new ArgumentNullException(nameof(submit));

        /// <summary>暂存断线事实（Room×连接号×Epoch 去重——同键不重复排队）。</summary>
        public void Enqueue(RoomInstance room, Session session)
        {
            if (room == null || session == null) return;
            lock (_gate)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    Entry pending = _pending[i];
                    if (ReferenceEquals(pending.Room, room)
                        && pending.ConnectionId == session.ConnectionId
                        && pending.SessionEpoch == session.Epoch)
                        return;
                }
                _pending.Add(new Entry
                {
                    Room = room,
                    Session = session,
                    ConnectionId = session.ConnectionId,
                    SessionEpoch = session.Epoch,
                });
            }
        }

        /// <summary>冲刷排队事实（onlyRoom 非 null 时只处理该房间）；Mailbox 已关则丢弃。</summary>
        public void Flush(RoomInstance onlyRoom = null)
        {
            Entry[] pending;
            lock (_gate)
                pending = _pending.ToArray();

            for (int i = 0; i < pending.Length; i++)
            {
                Entry item = pending[i];
                if (onlyRoom != null && !ReferenceEquals(onlyRoom, item.Room)) continue;

                if (item.Room == null || item.Room.Mailbox.IsClosed)
                {
                    Remove(item);
                    continue;
                }

                if (_submit(item.Room, item.ConnectionId, item.Session))
                    Remove(item);
            }
        }

        private void Remove(Entry item)
        {
            lock (_gate)
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    Entry current = _pending[i];
                    if (!ReferenceEquals(current.Room, item.Room)
                        || current.ConnectionId != item.ConnectionId
                        || current.SessionEpoch != item.SessionEpoch)
                        continue;
                    _pending.RemoveAt(i);
                    return;
                }
            }
        }

        private struct Entry
        {
            public RoomInstance Room;
            public Session Session;
            public int ConnectionId;
            public long SessionEpoch;
        }
    }
}
