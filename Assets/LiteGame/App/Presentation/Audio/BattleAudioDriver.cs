using System;
using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using LiteView;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 对局音效驱动（帧事件路由 <see cref="HitFeedbackDispatcher"/> 的消费者——VFX/飘字同族先例）：
    /// <list type="bullet">
    /// <item><b>Fire</b>：本地造成 = 2D 满音量（自己的枪不空间化）；他人开火 = 3D 空间位（距离衰减）。
    ///   两者都走 <see cref="AudioService.PlayRetrigger"/> **同源重触发**——自动连发（步枪 600 RPM = 10 发/秒）
    ///   每发重启同一代理，节奏 = 开火节奏、不吃代理预算、不堆叠；本地 voice 优先级高于他人/命中档
    ///   （<see cref="LocalFirePriority"/>——自己的枪不被抢）。</item>
    /// <item><b>Hit/Crit</b>：命中点 3D（全角色可闻——空间衰减天然管距离；爆头档独立 clip）。</item>
    /// <item><b>Death</b>：死亡位置 3D。</item>
    /// </list>
    /// **clip 常量** = Sound 库文件名（AddressByFileName 组——见收集器 Sound 组）；租约获取在
    /// <see cref="ProcedureBattle"/>（与其他表现资产同批，缺失按 kind 静默降级——音效是表现增益）。
    /// **并发预算归 <see cref="AudioService"/>**：Effect 组代理全忙且同优先级 → 丢弃（累计计数 +
    /// 限频日志——不刷屏）。**静默门**由上游 <c>SimView.EventSink</c> 保证（回滚重放不重复出声）。
    /// **表现参数为本地常量，禁入 <c>CombatConfig</c>**。
    /// </summary>
    public sealed class BattleAudioDriver : IHitFeedbackConsumer, IDisposable
    {
        /// <summary>开火音（Sound 库 location——收集组 Sound，AddressByFileName）。</summary>
        public const string FireClipLocation = "Assets/Sound/Weapon/Fire/sfx_gun_fire_rifle_01.wav";

        /// <summary>命中音（普通档）。</summary>
        public const string HitClipLocation = "Assets/Sound/Impact/Hit/sfx_hit_generic_01.wav";

        /// <summary>命中音（爆头档——更"肉"的一档，与飘字爆头档同语义）。</summary>
        public const string CritClipLocation = "Assets/Sound/Impact/Hit/sfx_hit_flesh_01.mp3";

        /// <summary>死亡音（角色 vocal）。</summary>
        public const string DeathClipLocation = "Assets/Sound/Character/Vocal/sfx_char_death_01.wav";

        /// <summary>本地开火音的同源 voice 键（连发走同一代理重触发）。</summary>
        public const string LocalFireVoice = "gun_fire_local";

        /// <summary>他人开火音的 voice 键前缀（逐槽位一个 voice——同槽换人即接管，键域有界）。</summary>
        public const string RemoteFireVoicePrefix = "gun_fire_remote_";

        /// <summary>本地开火音优先级（高于他人开火/命中档 0——自己的枪在代理全忙时抢占而不被丢）。</summary>
        public const int LocalFirePriority = 1;

        private readonly AudioService _audio;
        private readonly AudioClip _fire;
        private readonly AudioClip _hit;
        private readonly AudioClip _crit;
        private readonly AudioClip _death;
        private bool _disposed;

        /// <summary>已路由的帧事件数（诊断/测试面——路由计数与实际出声解耦：全忙丢弃、缺 clip 降级）。</summary>
        public int RoutedCount { get; private set; }

        /// <param name="fire">开火 clip（null = 该 kind 静默跳过——租约缺失降级）。</param>
        public BattleAudioDriver(AudioService audio, AudioClip fire, AudioClip hit, AudioClip crit, AudioClip death)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _fire = fire;
            _hit = hit;
            _crit = crit;
            _death = death;
        }

        public void OnHitFeedback(in HitFeedbackContext ctx)
        {
            if (_disposed) return;

            switch (ctx.Kind)
            {
                case FrameEventKind.Fire:
                    RoutedCount++;
                    if (_fire == null) return;
                    if (ctx.LocalRole == HitLocalRole.Caused)
                        _audio.PlayRetrigger(AudioService.Group.Effect, LocalFireVoice, _fire,
                            priority: LocalFirePriority);                                       // 自己的枪：2D 同源重触发
                    else if (ctx.Slot >= 0)
                        _audio.PlayRetrigger(AudioService.Group.Effect, RemoteFireVoicePrefix + ctx.Slot, _fire,
                            0.6f, position: ctx.WorldPos);                                      // 他人开火：逐槽位 3D 重触发
                    else
                        _audio.Play3D(AudioService.Group.Effect, _fire, ctx.WorldPos, 0.6f);    // 槽位未解析：一次性 3D（无稳定 voice 键）
                    break;

                case FrameEventKind.Crit:
                    RoutedCount++;
                    if (_crit != null) _audio.Play3D(AudioService.Group.Effect, _crit, ctx.WorldPos, 0.9f);
                    break;

                case FrameEventKind.Hit:
                    RoutedCount++;
                    if (_hit != null) _audio.Play3D(AudioService.Group.Effect, _hit, ctx.WorldPos, 0.9f);
                    break;

                case FrameEventKind.Death:
                    RoutedCount++;
                    if (_death != null) _audio.Play3D(AudioService.Group.Effect, _death, ctx.WorldPos);
                    break;
            }
        }

        public void Dispose() => _disposed = true;
    }
}
