using LiteGameNext.Platform.Audio;
using LiteSim;
using UnityEngine;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>战斗音效驱动：按权威战斗事件播开火/命中/暴击/死亡音效；无 clip 时静默跳过。</summary>
    public sealed class BattleAudioDriver
    {
        private readonly AudioService _audio;
        private readonly AudioClip _fire;
        private readonly AudioClip _hit;
        private readonly AudioClip _crit;
        private readonly AudioClip _death;

        public BattleAudioDriver(AudioService audio, AudioClip fire, AudioClip hit, AudioClip crit, AudioClip death)
        {
            _audio = audio;
            _fire = fire;
            _hit = hit;
            _crit = crit;
            _death = death;
        }

        /// <summary>已路由的事件数（诊断）。</summary>
        public int RoutedCount { get; private set; }

        /// <summary>按事件类型播音效；位置用于 3D，本地开火走 2D。</summary>
        public void OnCombatEvent(FrameEventKind kind, long actorId, long localEntityId, Vector3 position)
        {
            if (_audio == null || _audio.IsShutdown) return;
            switch (kind)
            {
                case FrameEventKind.Fire:
                    RoutedCount++;
                    if (_fire == null) return;
                    if (actorId == localEntityId) _audio.Play(AudioGroup.Effect, _fire, 0.9f);
                    else _audio.Play3D(AudioGroup.Effect, _fire, position, 0.6f);
                    break;
                case FrameEventKind.Hit:
                    RoutedCount++;
                    if (_hit != null) _audio.Play3D(AudioGroup.Effect, _hit, position, 0.9f);
                    break;
                case FrameEventKind.Crit:
                    RoutedCount++;
                    if (_crit != null) _audio.Play3D(AudioGroup.Effect, _crit, position, 0.9f);
                    break;
                case FrameEventKind.Death:
                    RoutedCount++;
                    if (_death != null) _audio.Play3D(AudioGroup.Effect, _death, position, 1f);
                    break;
            }
        }
    }
}
