using System;
using LiteClient;
using LiteClient.UI;
using LiteSim;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 全局击杀提示驱动（击杀提示批；《战斗HUD层级设计》§2/§3）：把 <see cref="PublicKillFeed"/> 的待展示
    /// 击杀逐条播报——**多条堆叠**（每行独立驻留、行满则留队等待，不覆盖他人），行件全 **prefab 预置**
    /// （禁运行时建视觉——结构脚手架扫描）。
    ///
    /// - **数据源**：客户端会话的公共击杀面（快照携带的房间级最近击杀窗口）——**全局**（含他人击杀），
    ///   不止本地造成的击杀；本地造成的击杀同样经此路（权威记录含本人）；
    /// - **行格式** <c>击杀者 [武器图标] 死者</c>：击杀者为本地 → "你"，否则占位 <c>玩家{k}</c>
    ///   （<see cref="PlayerIdentity"/> 身份序号单源——与排行榜/阵亡归属共用）；`killer==0`（自杀/环境）
    ///   → <c>玩家{v} 阵亡</c>（无图标）；武器图标按击杀帧装备槽取图（缺件静默降级：不显示图标）；
    /// - **计时**：单条驻留 <see cref="RowDwellSeconds"/> 秒，步进走 <see cref="UiAnimationClock.Delta"/>
    ///   （红线 9：控件计时统一 UIClock）；
    /// - **写纪律**：仅行内容变化时写；隐藏行整组 <c>SetActive(false)</c>；
    /// - 测试注：镜像/本地 Id/队列走构造注入（同面板与排行榜驱动）——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleKillFeedDriver : IDisposable
    {
        /// <summary>同时可堆叠的行数（prefab 预置行数＝`Tmp_KillFeedRow` 实例数——行满则新条留队等待，不覆盖）。
        /// 改本值须同批改 `Hud_Battle.prefab` 的行实例数——结构守卫
        /// `BattleHudMenuEditModeTests.HUD_击杀提示面…` 按本值逐行点名，漏改即红。</summary>
        public const int RowCount = 5;

        /// <summary>装备槽数（武器图标下标域；= <see cref="SimConfig.WeaponSlotsPerEntity"/>——不硬编码）。</summary>
        public const int SlotCount = SimConfig.WeaponSlotsPerEntity;

        /// <summary>单条驻留时长（秒）。</summary>
        public const float RowDwellSeconds = 4f;

        /// <summary>本地击杀者文案（与占位"玩家{k}"区分）。</summary>
        public const string LocalKillerLabel = "你";

        /// <summary>无归属（自杀/环境）行文案的动词。</summary>
        public const string DeathVerb = "阵亡";

        private readonly SimWorldState _state;
        private readonly Func<long> _localEntityId;
        private readonly PublicKillFeed _feed;
        private readonly Sprite[] _weaponIcons;      // 可缺（缺件静默降级：隐藏图标）
        private readonly GameObject[] _rows;
        private readonly TextMeshProUGUI[] _killers;
        private readonly Image[] _weapons;
        private readonly TextMeshProUGUI[] _victims;

        private readonly float[] _remaining = new float[RowCount];
        private readonly bool[] _active = new bool[RowCount];
        private bool _disposed;

        /// <param name="state">逻辑镜像（占位名 <see cref="PlayerIdentity"/> 读面）。</param>
        /// <param name="localEntityId">本地实体 Id 来源（"你"判定）。</param>
        /// <param name="feed">公共击杀面（待展示队列；null = 无源，驱动静默空转）。</param>
        /// <param name="weaponIcons">武器图标（下标 = 装备槽；null/短数组 = 图标降级）。</param>
        public BattleKillFeedDriver(SimWorldState state, Func<long> localEntityId, PublicKillFeed feed,
            Sprite[] weaponIcons, GameObject[] rows, TextMeshProUGUI[] killers, Image[] weapons, TextMeshProUGUI[] victims)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _feed = feed;                                        // null = 空转（静默降级）
            _weaponIcons = weaponIcons;
            _rows = Require(rows, nameof(rows));
            _killers = Require(killers, nameof(killers));
            _weapons = Require(weapons, nameof(weapons));
            _victims = Require(victims, nameof(victims));
            for (int i = 0; i < RowCount; i++) _rows[i].SetActive(false);
        }

        private static T[] Require<T>(T[] array, string param) where T : class
        {
            if (array == null) throw new ArgumentNullException(param);
            if (array.Length != RowCount) throw new ArgumentException($"行件数须为 {RowCount}", param);
            return array;
        }

        /// <summary>每渲染帧（视图状态的消费者——同面板/排行榜/阵亡同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            float delta = UiAnimationClock.Delta;                 // 控件计时统一 UIClock（红线 9）

            for (int i = 0; i < RowCount; i++)                    // ① 到期行回收
            {
                if (!_active[i]) continue;
                _remaining[i] -= delta;
                if (_remaining[i] <= 0f) Hide(i);
            }

            while (_feed != null && _feed.PendingCount > 0)        // ② 新条入空行（行满则留队——不丢、不覆盖）
            {
                int idx = FreeRow();
                if (idx < 0) break;
                if (!_feed.TryDequeue(out PublicKillFeed.Entry entry)) break;
                Show(idx, in entry);
            }
        }

        private int FreeRow()
        {
            for (int i = 0; i < RowCount; i++)
                if (!_active[i]) return i;
            return -1;
        }

        private void Show(int idx, in PublicKillFeed.Entry e)
        {
            _active[idx] = true;
            _remaining[idx] = RowDwellSeconds;
            _rows[idx].SetActive(true);

            string victim = PlayerIdentity.PlaceholderName(_state, e.Victim);
            string killer = KillerLabel(e.Killer);

            if (string.IsNullOrEmpty(killer))
            {
                _killers[idx].gameObject.SetActive(false);
                _weapons[idx].gameObject.SetActive(false);
                _victims[idx].text = string.IsNullOrEmpty(victim) ? string.Empty : victim + " " + DeathVerb;
                return;
            }

            _killers[idx].gameObject.SetActive(true);
            _killers[idx].text = killer;
            _victims[idx].text = victim;

            Sprite icon = IconOf(e.WeaponSlot);
            _weapons[idx].gameObject.SetActive(icon != null);
            if (icon != null) _weapons[idx].sprite = icon;
        }

        /// <summary>击杀者文案：0 = 无归属（空）；本地 = "你"；否则占位"玩家{k}"。</summary>
        private string KillerLabel(long killerId)
        {
            if (killerId == 0L) return string.Empty;
            if (killerId == _localEntityId()) return LocalKillerLabel;
            return PlayerIdentity.PlaceholderName(_state, killerId);
        }

        private Sprite IconOf(int slot)
        {
            if (slot < 0 || _weaponIcons == null || slot >= _weaponIcons.Length) return null;
            return _weaponIcons[slot];
        }

        private void Hide(int idx)
        {
            if (!_active[idx] && !_rows[idx].activeSelf) return;
            _active[idx] = false;
            _remaining[idx] = 0f;
            _rows[idx].SetActive(false);
        }

        /// <summary>离场收尾（HUD 实例随视图域销毁——本件只把行归位，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < RowCount; i++) Hide(i);
        }
    }
}
