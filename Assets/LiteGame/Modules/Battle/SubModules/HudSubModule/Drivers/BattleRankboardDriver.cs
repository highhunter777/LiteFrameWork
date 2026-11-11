using LiteFramework.UI;
using System;
using LiteFramework;
using LiteSim;
using TMPro;
using UnityEngine;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Battle.Abstractions.Read;

namespace LiteGame.Modules.Battle.SubModules.HudSubModule.Drivers
{
    /// <summary>
    /// 局内排行榜驱动（排行榜批）。
    /// </summary>
    public sealed class BattleRankboardDriver : IDisposable, IHudDriver
    {
        /// <summary>格数（= <c>eight</c> 房间模板上限——>8 人裁剪，见审计登记）。</summary>
        public const int CellCount = 8;

        private readonly IBattleReadModel _readModel;
        private readonly Func<long> _localEntityId;
        private readonly GameObject[] _cells;        // 格根＝单元（隐藏/显示只关它；格内子件保持激活）
        private readonly TextMeshProUGUI[] _names;
        private readonly TextMeshProUGUI[] _kills;
        private readonly GameObject[] _highlights;
        private readonly TextMeshProUGUI _teamScore;
        private readonly BattleRankPolicy _order;      // 排行策略（策略表投影；缺省 = 击杀降序→EntityId 升序）

        // 每帧采集缓冲（零分配——容量上界 = MaxEntities；索引即规范序）。
        private readonly long[] _ids;
        private readonly int[] _killCounts;
        private int _count;

        // 展示序索引（Kills 降序 → Id 升序）。
        private readonly int[] _display;

        // 格内脏位（值变化才写）——_cellId[i]==0 表示该格当前为空（隐藏）。
        private readonly long[] _cellId = new long[CellCount];
        private readonly string[] _cellName = new string[CellCount];
        private readonly int[] _cellKills = new int[CellCount];

        // 上一帧指纹（规范序 (id,kills) 序列 + 数量 + 本地 Id + Team）——不变即整体跳过。
        private readonly long[] _printIds;
        private readonly int[] _printKills;
        private int _printCount = -1;
        private long _printLocalId = -1;
        private int _printTeam = int.MinValue;

        private bool _disposed;

        /// <param name="cells">格根（单元）——**隐藏/显示只关它**；格内名字/击杀件保持激活
        /// （prefab 里可直接读/调，不必先逐个点亮）。</param>
        /// <summary>读源为只读事实端口（生产：权威镜像优先的 `BattleReadModel`；测试可传替身）。</summary>
        public BattleRankboardDriver(IBattleReadModel readModel, Func<long> localEntityId,
            GameObject[] cells, TextMeshProUGUI[] names, TextMeshProUGUI[] kills, GameObject[] highlights,
            TextMeshProUGUI teamScore = null, BattleRankPolicy order = default)
        {
            _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
            _localEntityId = localEntityId ?? throw new ArgumentNullException(nameof(localEntityId));
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
            if (_cells.Length != CellCount)
                throw new ArgumentException($"格根数须为 {CellCount}", nameof(cells));
            _names = RequireCells(names, nameof(names));
            _kills = RequireCells(kills, nameof(kills));
            _highlights = highlights ?? throw new ArgumentNullException(nameof(highlights));
            if (_highlights.Length != CellCount)
                throw new ArgumentException($"高亮件数须为 {CellCount}", nameof(highlights));
            _teamScore = teamScore;                       // null = 无预留节点（静默跳过）
            _order = order;                               // default = 历史全序（策略表缺行同值）

            int capacity = SimConfig.MaxEntities;
            _ids = new long[capacity];
            _killCounts = new int[capacity];
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
        void IHudDriver.Tick(float deltaSeconds) => Tick();

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

            if (!FingerprintChanged())                     // 规范序（EntityId 升序）由事实端口给出
            {
                MarkPrint();
                return;                                   // 击杀分布未变——零写
            }

            SortByDisplay();                              // Kills 降序 → EntityId 升序（全序防同分抖动）
            FillCells();
            MarkPrint();
        }

        // ---- 采集 ----。

        /// <summary>从事实端口收参赛玩家（规范序：EntityId 升序——序号/名字与指纹的稳定基准）。</summary>
        private void CollectPlayers()
        {
            _count = _readModel.PlayerCount;
            for (int i = 0; i < _count; i++)
            {
                if (!_readModel.TryGetPlayerAt(i, out BattlePlayerFacts facts)) { _count = i; break; }
                _ids[i] = facts.Id;
                _killCounts[i] = facts.Kills;
            }
        }

        /// <summary>展示序：Kills 降序 → EntityId 升序（与结算页共用 <see cref="BattleRankPolicy"/> 单源；左＝第一名）。</summary>
        private void SortByDisplay()
            => _order.SortDisplay(_count, _ids, _killCounts, _display);

        // ---- 写入（脏位）----。

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
                string name = _readModel.DisplayName(id);          // 身份序号（与阵亡归属共用单源）

                if (_cellId[cell] != id || _cellName[cell] != name)
                {
                    _cellId[cell] = id;
                    _cellName[cell] = name;
                    _cells[cell].SetActive(true);                        // 单元级显隐：只关/开格根
                    SetText(_names[cell], name);
                    _highlights[cell].SetActive(id == localId);          // 自己那格点亮（格内子件按位开关）
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
            if (_cellId[cell] == 0L && !_cells[cell].activeSelf) return;
            _cellId[cell] = 0L;
            _cellName[cell] = null;
            _cellKills[cell] = int.MinValue;
            _highlights[cell].SetActive(false);              // 高亮件按位归零（格根关掉前先清，防复用残留）
            _cells[cell].SetActive(false);                   // 单元级隐藏：只关格根，子件保持激活
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
            bool team = _readModel.TeamMode != 0;
            if (_teamScore.gameObject.activeSelf != team) _teamScore.gameObject.SetActive(team);
            if (!team) return;
            SetText(_teamScore, _readModel.TeamScore.ToString());
        }

        // ---- 指纹 ----。

        private bool FingerprintChanged()
        {
            if (_printCount != _count) return true;
            if (_printLocalId != _localEntityId()) return true;
            if (_printTeam != _readModel.TeamMode) return true;
            for (int i = 0; i < _count; i++)
            {
                if (_printIds[i] != _ids[i] || _printKills[i] != _killCounts[i]) return true;
            }
            return false;
        }

        private void MarkPrint()
        {
            _printCount = _count;
            _printLocalId = _localEntityId();
            _printTeam = _readModel.TeamMode;
            for (int i = 0; i < _count; i++)
            {
                _printIds[i] = _ids[i];
                _printKills[i] = _killCounts[i];
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
