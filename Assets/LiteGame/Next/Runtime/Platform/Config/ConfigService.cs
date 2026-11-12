using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGame.Modules.Config;
using LiteSim;
using Luban;

namespace LiteGameNext.Platform.Config
{
    /// <summary>
    /// 配置表服务：按 <see cref="TableLayout.Files"/> 逐表装载字节 → Luban `Tables`；
    /// 表清单与判定面域由共享单源提供，本类不另维护清单。
    /// </summary>
    public sealed class ConfigService
    {
        private readonly Func<string, CancellationToken, UniTask<byte[]>> _bytesProvider;
        private readonly Dictionary<string, byte[]> _byTableName = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        private cfg.Tables _tables;
        private CombatValues? _combatValues;

        /// <param name="bytesProvider">(域相对路径, ct) → 表字节；由内容服务按 `TableLayout.DataDir + 路径 + ".bytes"` 提供。</param>
        public ConfigService(Func<string, CancellationToken, UniTask<byte[]>> bytesProvider)
        {
            _bytesProvider = bytesProvider ?? throw new ArgumentNullException(nameof(bytesProvider));
        }

        /// <summary>是否已装载完成。</summary>
        public bool Loaded => _tables != null;

        /// <summary>已装载表数（诊断）。</summary>
        public int LoadedTableCount => _byTableName.Count;

        /// <summary>表数据目录（单源透传）。</summary>
        public static string DataDir => TableLayout.DataDir;

        /// <summary>表清单（单源透传）。</summary>
        public static IReadOnlyList<string> TableFiles => TableLayout.Files;

        /// <summary>Luban 全表面；未装载抛（fail-fast，不静默给空表）。</summary>
        public cfg.Tables Tables =>
            _tables ?? throw new InvalidOperationException("配置表未装载：先 LoadAsync");

        /// <summary>战斗规则（表投影；缺表回落给定默认）。</summary>
        public MatchRules Rules(MatchRules fallback)
        {
            cfg.Tables tables = Tables;
            return tables.Tbbattlestrategy != null
                ? BattleStrategyTableReader.ReadRules(tables.Tbbattlestrategy, fallback)
                : fallback;
        }

        /// <summary>判定数值（表投影；缺表回落 `CombatValues.Default`）。</summary>
        public CombatValues TableCombatValues
        {
            get
            {
                if (_combatValues.HasValue) return _combatValues.Value;
                cfg.Tables tables = Tables;
                _combatValues = tables.Tbcombatnum == null || tables.Tbmovementconfig == null
                    || tables.Tbentityconfig == null || tables.Tbcombatgeom == null
                    ? CombatValues.Default
                    : SimConfigMapper.BuildCombatValues(tables.Tbcombatnum, tables.Tbmovementconfig,
                        tables.Tbentityconfig, tables.Tbcombatgeom);
                return _combatValues.Value;
            }
        }

        /// <summary>按 `TableLayout.Files` 逐表装载；任一表缺字节即抛（表链不完整比缺表更危险）。</summary>
        public async UniTask LoadAsync(CancellationToken ct)
        {
            _byTableName.Clear();
            for (int i = 0; i < TableLayout.Files.Length; i++)
            {
                string file = TableLayout.Files[i];
                byte[] bytes = await _bytesProvider(file, ct);
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidDataException("配置表缺字节：" + file + "（跑 Luban/gen.bat 并检查收集组）");
                string tableName = file.Substring(file.LastIndexOf('/') + 1);
                _byTableName[tableName] = bytes;
            }

            _tables = new cfg.Tables(name =>
                _byTableName.TryGetValue(name, out byte[] data)
                    ? new ByteBuf(data)
                    : throw new InvalidDataException("Luban 表缺数据：" + name));
            _combatValues = null;
            Log.Info("配置表已装载：" + _byTableName.Count + " 张");
        }

        /// <summary>按表名取已装载原始字节（诊断/摘要用）。</summary>
        public bool TryGetRaw(string tableName, out byte[] bytes) => _byTableName.TryGetValue(tableName, out bytes);
    }

    /// <summary>配置表数据缺失（表链不完整）。</summary>
    public sealed class InvalidDataException : Exception
    {
        public InvalidDataException(string message) : base(message)
        {
        }
    }
}
