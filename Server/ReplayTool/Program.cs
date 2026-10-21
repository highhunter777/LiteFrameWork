using System;
using System.IO;
using System.Text.Json;
using LiteNet;
using LiteSim;
using RoomServer;
using RoomServer.Diagnostics;
using RoomServer.Runtime;

namespace ReplayTool
{
    /// <summary>
    /// 房间记录重放 CLI：退出码 0=一致 / 2=首分歧 / 3=身份或格式 / 4=IO / 5=记录截断。
    /// 本刀只加载记录自带的配置/地图与默认数值表；表身份不符会由 fixedConfigDigest 闸拒绝。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0 || !string.Equals(args[0], "replay", StringComparison.Ordinal))
            {
                PrintUsage();
                return 3;
            }

            string file = null;
            bool json = false;
            bool useDefaults = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--json", StringComparison.Ordinal)) { json = true; continue; }
                if (string.Equals(args[i], "--defaults", StringComparison.Ordinal)) { useDefaults = true; continue; }
                if (file == null) { file = args[i]; continue; }
                PrintUsage();
                return 3;
            }
            if (string.IsNullOrEmpty(file))
            {
                PrintUsage();
                return 3;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                RoomRunEnvelope envelope = RoomRunCodec.Decode(bytes);
                SimMapData map = envelope.Map?.ToMap();
                if (map == null)
                    throw new InvalidDataException("记录缺地图快照（Map）——本刀只支持自包含记录");

                RoomConfig config = envelope.Config?.ToConfig(map)
                    ?? new RoomConfig
                    {
                        RoomId = envelope.Record.RoomId,
                        ExpectedPlayers = envelope.Record.ExpectedPlayers,
                        Seed = (long)envelope.Record.Seed,
                        Rules = MatchRules.Default,
                        Map = map,
                    };

                CombatValues values;
                WeaponTable weapons;
                uint baseDigest = envelope.GameplayDigestBase;
                if (useDefaults)
                {
                    values = CombatValues.Default;
                    weapons = WeaponTable.Default;
                }
                else
                {
                    ServerTableLoad load = ServerTableLoader.LoadFromRepo();
                    values = load.Combat.ToValues();
                    weapons = load.Weapons;
                    baseDigest = load.Digest;
                    if (load.Digest != envelope.GameplayDigestBase)
                        throw new InvalidDataException(
                            $"表基摘要不符：录制=0x{envelope.GameplayDigestBase:x8} 当前=0x{load.Digest:x8}");
                }

                RoomReplayResult result = RoomReplayRunner.ReplayEnvelope(
                    envelope, config, map, values, weapons,
                    BuildHash.Value, SimMapDigest.Compute(map), baseDigest);

                if (json) Console.WriteLine(ToJson(result));
                else Console.WriteLine(result.Message);

                if (result.IdentityError) return 3;
                if (!result.Matches) return 2;
                if (result.Truncated) return 5;
                return 0;
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine("记录格式/身份错误: " + ex.Message);
                return 3;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("记录格式/身份错误: " + ex.Message);
                return 3;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("表装载/身份校验失败: " + ex.Message);
                return 3;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine("记录读取失败: " + ex.Message);
                return 4;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine("记录读取失败: " + ex.Message);
                return 4;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("重放失败: " + ex.Message);
                return 4;
            }
        }

        private static string ToJson(RoomReplayResult r) => JsonSerializer.Serialize(new
        {
            matches = r.Matches,
            truncated = r.Truncated,
            identityError = r.IdentityError,
            firstDivergenceFrame = r.FirstDivergenceFrame,
            divergenceEventSequence = r.DivergenceEventSequence,
            divergenceCommandIndex = r.DivergenceCommandIndex,
            expectedChecksum = r.ExpectedChecksum,
            actualChecksum = r.ActualChecksum,
            replayedSteps = r.ReplayedSteps,
            message = r.Message,
        });

        private static void PrintUsage()
        {
            Console.Error.WriteLine("用法: ReplayTool replay <record.json.gz> [--json] [--defaults]");
            Console.Error.WriteLine("  --defaults  仅用于默认表/离线测试记录（跳过仓库表装载）");
            Console.Error.WriteLine("退出码: 0=一致 2=首分歧 3=身份/格式 4=IO 5=记录截断");
        }
    }
}
