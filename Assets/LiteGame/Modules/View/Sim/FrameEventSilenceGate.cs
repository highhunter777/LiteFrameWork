using System;
using LiteSim;

namespace LiteView
{
    /// <summary>
    /// 帧事件静默门（回滚重放/和解去重）：帧号 ≤ <see cref="SilenceUntilFrame"/> 的事件丢弃，
    /// 放行的事件交 <see cref="SimView.FrameEventSink"/>；只做计数与闸门，不触达表现服务。
    /// </summary>
    internal sealed class FrameEventSilenceGate
    {
        public int SilenceUntilFrame { get; private set; } = -1;

        public int SilencedEvents { get; private set; }

        public int DeliveredEvents { get; private set; }

        public void OnReconcile(int frame) => Silence(Math.Max(SilenceUntilFrame, frame));

        public void OnRollback(int replayedToFrame) => Silence(Math.Max(SilenceUntilFrame, replayedToFrame));

        public void Silence(int untilFrame)
        {
            if (untilFrame > SilenceUntilFrame) SilenceUntilFrame = untilFrame;
        }

        public void Deliver(SimWorldState state, SimView.FrameEventSink sink)
        {
            if (state == null) return;
            FrameEventBuffer events = state.Events;
            for (int i = 0; i < events.Count; i++)
            {
                FrameEvent e = events.Items[i];
                if (state.Frame <= SilenceUntilFrame)
                {
                    SilencedEvents++;
                    continue;
                }

                DeliveredEvents++;
                sink?.Invoke(in e);
            }
        }
    }
}
