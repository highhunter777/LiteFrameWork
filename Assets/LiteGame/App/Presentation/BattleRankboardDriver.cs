using System;
using LiteSim;
using TMPro;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 局内排行榜驱动（排行榜批；FFA "比分"＝击杀）：每渲染帧从**公共镜像**收参赛玩家
    /// （<see cref="EntityFlags.Player"/>——阵亡/断线保留槽位，故不看存活），按
    /// <c>Kills 降序 → EntityId 升序</c> 全序排列，把结果**从左往右**填进**预置**的 8 格
    /// （最左＝第一名）；只有"击杀分布"变化才重排/写（脏位）；**自己那格**点亮高亮件；
    /// 人数 &lt; 8 的尾部空格隐藏。
    ///
    /// - **数据源**：镜像公共面（<see cref="EntitySlot.Kills"/> ＋ <see cref="EntityFlags.Player"/>）——
    ///   权威事实、非预测，天然正确（同面板驱动的读法，不跨 Lua）；
    /// - **名字**：本批**占位** <c>玩家{k}</c>（k = 参赛玩家按 EntityId 升序的序号，局内稳定；
    ///   真名字/昵称归 G3 Profile——见审计登记）；
    /// - **`Txt_Score`（团队比分）**：预留节点——<see cref="MatchStateData.Team"/>==0（FFA）时隐藏，
    ///   不显示恒 0 假数据；Team 模式落地即启用；
    /// - **禁运行时建视觉元素**（结构脚手架扫描）：8 格全部 prefab 预置，本驱动只显隐/写值；
    /// - 测试注：镜像与本地 Id 走构造注入（同面板驱动）——EditMode 确定性驱动。
    /// </summary>
    public sealed class BattleRankboardDriver : IDisposable
    {
        /// <summary>格数（= <c>eight</c> 房间模板上限——>8 人裁剪，见审计登记）。</summary>
        public const int CellCount = 8;

        private readonly SimWorldState _state;
        private readonly Func<long> _localEntityId;
        private readonly TextMeshProUGUI[] _names;
        private readonly TextMeshProUGUI[] _kills;
        private readonly GameObject[] _highlights;
        private readonly TextMeshProUGUI _teamScore;

        // 每帧采集缓冲（零分配——容量上界 = MaxEntities）；_ids/_killCounts 与 _slots 同索引
        private readonly int[] _slots;
        private readonly long[] _ids;
        private readonly int[] _killCounts;
        private int _count;

        // 排序用索引（两轮：规范序 Id 升 / 展示序 Kills 降）
        private readonly int[] _canonical;
        private readonly int[] _display;

        // 格内脏位（值变化才写）——_cellId[i]==0 表示该格当前为空（隐藏）
        private readonly long[] _cellId = new long[CellCount];
        private readonly string[] _cellName = new string[CellCount];
        private readonly int[] _cellKills = new int[CellCount];

        // 上一帧指纹（规范序 (id,kills) 序列 + 数量 + 本地 Id + Team）——不变即整体跳过
        private readonly long[] _printIds;
        private readonly int[] _printKills;
        private int _printCount = -1;
        private long _printLocalId = -1;
        private int _printTeam = int.MinValue;

        private bool _disposed;

        public BattleRankboardDriver(SimWorldState state, Func<long> localEntityId,
            TextMeshProUGUI[] names, TextMeshProUGUI[] kills, GameObject[] highlights,
            TextMeshProUGUI teamScore = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _names = RequireCells(names, nameof(names));
            _kills = RequireCells(kills, nameof(kills));
            _highlights = highlights ?? throw new ArgumentNullException(nameof(highlights));
            if (_highlights.Length != CellCount)
                throw new ArgumentException($"高亮件数须为 {CellCount}", nameof(highlights));
            _teamScore = teamScore;                       // null = 无预留节点（静默跳过）

            int capacity = SimConfig.MaxEntities;
            _slots = new int[capacity];
            _ids = new long[capacity];
            _killCounts = new int[capacity];
            _canonical = new int[capacity];
            _display = new int[capacity];
            _printIds = new long[capacity];
            _printKills = new int[capacity];
            for (int i = 0; i < CellCount; i++) _cellKills[i] = int.MinValue;
        }

        private static TextMeshProUGUI[] RequireCells(TextMeshProUGUI[] cells, string param)
        {
            if (cells == null) throw new ArgumentNullException(param);
            if (cells.Length != CellCount)
                throw new ArgumentException($"格件数须为 {CellCount}", param);
            return cells;
        }

        /// <summary>每渲染帧（视图状态的消费者——同面板/准心/激光同拍）。</summary>
        public void Tick()
        {
            if (_disposed) return;

            TickTeamScore();

            CollectPlayers();
            if (_count == 0)
            {
                ClearAllCells();
                MarkPrint();
                return;
            }

            SortByCanonical();                            // Id 升序 → 序号/指纹共用规范序
            if (!FingerprintChanged())
            {
                MarkPrint();
                return;                                   // 击杀分布未变——零写
            }

            SortByDisplay();                              // Kills 降序 → EntityId 升序（全序防同分抖动）
            FillCells();
            MarkPrint();
        }

        // ---- 采集 ----

        /// <summary>收参赛玩家：<see cref="EntityFlags.Player"/> 且槽位在册（阵亡/断线保留槽位——不看存活）。</summary>
        private void CollectPlayers()
        {
            _count = 0;
            EntitySlot[] entities = _state.Entities;
            for (int slot = 0; slot < entities.Length; slot++)
            {
                ref EntitySlot e = ref entities[slot];
                if (!PlayerIdentity.IsPlayer(in e)) continue;   // 空槽/非参赛（bot 以外的道具/投射物等）
                _slots[_count] = slot;
                _ids[_count] = e.Id;
                _killCounts[_count] = e.Kills;
                _count++;
            }
        }

        /// <summary>规范序：EntityId 升序（序号/名字与指纹的稳定基准）。</summary>
        private void SortByCanonical()
        {
            for (int i = 0; i < _count; i++) _canonical[i] = i;
            for (int i = 1; i < _count; i++)
            {
                int cur = _canonical[i];
                int j = i - 1;
                while (j >= 0 && _ids[_canonical[j]] > _ids[cur])
                {
                    _canonical[j + 1] = _canonical[j];
                    j--;
                }
                _canonical[j + 1] = cur;
            }
        }

        /// <summary>展示序：Kills 降序 → EntityId 升序（与规范序同集合；左＝第一名）。</summary>
        private void SortByDisplay()
        {
            Array.Copy(_canonical, _display, _count);
            for (int i = 1; i < _count; i++)
            {
                int cur = _display[i];
                int j = i - 1;
                while (j >= 0 && Less(cur, _display[j]))
                {
                    _display[j + 1] = _display[j];
                    j--;
                }
                _display[j + 1] = cur;
            }
        }

        /// <summary>展示序比较：击杀多者在前；同击杀按 EntityId 升序（全序，防同分每帧互换）。</summary>
        private bool Less(int left, int right)
        {
            if (_killCounts[left] != _killCounts[right]) return _killCounts[left] > _killCounts[right];
            return _ids[left] < _ids[right];
        }

        // ---- 写入（脏位）----

        private void FillCells()
        {
            long localId = _localEntityId();
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (cell >= _count)
                {
                    HideCell(cell);
                    continue;
                }

                int idx = _display[cell];
                long id = _ids[idx];
                int kills = _killCounts[idx];
                string name = PlayerIdentity.PlaceholderName(_state, id);   // 身份序号（与阵亡归属共用单源）

                if (_cellId[cell] != id || _cellName[cell] != name)
                {
                    _cellId[cell] = id;
                    _cellName[cell] = name;
                    _names[cell].gameObject.SetActive(true);
                    SetText(_names[cell], name);
                    _highlights[cell].SetActive(id == localId);          // 自己那格点亮
                }
                if (_cellKills[cell] != kills)
                {
                    _cellKills[cell] = kills;
                    SetText(_kills[cell], kills.ToString());
                }
            }
        }

        private void HideCell(int cell)
        {
            if (_cellId[cell] == 0L && !_names[cell].gameObject.activeSelf) return;
            _cellId[cell] = 0L;
            _cellName[cell] = null;
            _cellKills[cell] = int.MinValue;
            _highlights[cell].SetActive(false);
            _names[cell].gameObject.SetActive(false);
            _kills[cell].gameObject.SetActive(false);
        }

        private void ClearAllCells()
        {
            for (int cell = 0; cell < CellCount; cell++) HideCell(cell);
        }

        private static void SetText(TextMeshProUGUI text, string value)
        {
            if (text.text != value) text.text = value;
        }

        private void TickTeamScore()
        {
            if (_teamScore == null) return;
            bool team = _state.Match.Team != 0;
            if (_teamScore.gameObject.activeSelf != team) _teamScore.gameObject.SetActive(team);
            if (!team) return;
            SetText(_teamScore, _state.Match.Score.ToString());
        }

        // ---- 指纹 ----

        private bool FingerprintChanged()
        {
            if (_printCount != _count) return true;
            if (_printLocalId != _localEntityId()) return true;
            if (_printTeam != _state.Match.Team) return true;
            for (int i = 0; i < _count; i++)
            {
                int idx = _canonical[i];
                if (_printIds[i] != _ids[idx] || _printKills[i] != _killCounts[idx]) return true;
            }
            return false;
        }

        private void MarkPrint()
        {
            _printCount = _count;
            _printLocalId = _localEntityId();
            _printTeam = _state.Match.Team;
            for (int i = 0; i < _count; i++)
            {
                int idx = _canonical[i];
                _printIds[i] = _ids[idx];
                _printKills[i] = _killCounts[idx];
            }
        }

        /// <summary>离场收尾（HUD 实例随视图域销毁——本件只把格归位，幂等）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearAllCells();
        }
    }
}
