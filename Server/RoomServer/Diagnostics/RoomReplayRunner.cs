using System;
using System.Collections.Generic;
using LiteSim;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Diagnostics
{
    /// <summary>重放结论（退出语义见 ReplayTool：0 一致 / 2 首分歧 / 3 身份或格式 / 4 IO）。</summary>
    public sealed class RoomReplayResult
    {
        public bool Matches { get; set; }
        public bool Truncated { get; set; }
        public bool IdentityError { get; set; }
        public int FirstDivergenceFrame { get; set; } = -1;
        public long DivergenceEventSequence { get; set; } = -1;
        public int DivergenceCommandIndex { get; set; } = -1;
        public uint ExpectedChecksum { get; set; }
        public uint ActualChecksum { get; set; }
        public int ReplayedSteps { get; set; }

        /// <summary>
        /// 分歧帧的字段级差异报告（录制世界 vs 重放世界）。null = 记录未启用世界环，
        /// 或分歧帧已被环窗口覆盖——此时只有帧号与 checksum（<see cref="Message"/> 会提示）。
        /// </summary>
        public SimWorldDiff.Report DivergenceReport { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 在线房间重放器：用真实 <see cref="RoomRuntime"/> 消费录制命令，逐步比对录制 checksum。
    ///
    /// **为什么不复用 <see cref="SimReplayRunner"/> 的输入回放**：权威链在 Step 后执行延迟补偿与
    /// MatchSystem.Tick，且输入携带 viewFrame/ackSnapshot；这些上下文不在 SimInputFrame 里。
    /// 复制一份 Step 编排 = 第二套执行链，必然漂移。本类只做“重建同配置房间 + 按序喂命令”。
    /// </summary>
    public static class RoomReplayRunner
    {
        public static RoomReplayResult Replay(RoomRunRecord record, RoomConfig config, SimMapData map,
            CombatValues values, WeaponTable weapons, uint gameplayDigestBase = 0,
            uint? expectedFixedConfigDigest = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (map == null) throw new ArgumentNullException(nameof(map));

            var replayConfig = new RoomConfig
            {
                RoomId = record.RoomId,
                ExpectedPlayers = record.ExpectedPlayers,
                Seed = record.Seed == 0 ? config.Seed : (long)record.Seed,
                MatchTimeLimitMs = config.MatchTimeLimitMs,
                WaitingTimeoutMs = config.WaitingTimeoutMs,
                Rules = config.Rules,
                MapId = config.MapId,
                Map = map,
                Port = config.Port,
            };

            RoomRuntime runtime;
            try
            {
                runtime = new RoomRuntime(replayConfig, combat: values, weapons: weapons,
                    matchId: record.MatchId, testRules: null,
                    gameplayDigestBase: gameplayDigestBase, recorder: null);
            }
            catch (Exception ex)
            {
                return new RoomReplayResult
                {
                    IdentityError = true,
                    Message = "重建房间失败：" + ex.Message,
                };
            }

            if (expectedFixedConfigDigest.HasValue
                && runtime.FixedConfig.Digest != expectedFixedConfigDigest.Value)
            {
                return new RoomReplayResult
                {
                    IdentityError = true,
                    Message = $"fixedConfigDigest 不符：录制=0x{expectedFixedConfigDigest.Value:x8} " +
                              $"当前=0x{runtime.FixedConfig.Digest:x8}（表/规则初值不同源，拒绝重放）",
                };
            }

            var outputs = new List<RoomOutput>();
            int commandIndex = 0;
            int replayedSteps = 0;

            for (int i = 0; i < record.Events.Count; i++)
            {
                RoomRunEvent e = record.Events[i];
                switch (e.Kind)
                {
                    case RoomRunEventKind.Command:
                        outputs.Clear();
                        try
                        {
                            runtime.Execute(e.Command.ToCommand(), outputs);
                        }
                        catch (Exception ex)
                        {
                            return new RoomReplayResult
                            {
                                Matches = false,
                                Truncated = record.Truncated,
                                FirstDivergenceFrame = runtime.AuthSim.Frame,
                                DivergenceEventSequence = e.Sequence,
                                DivergenceCommandIndex = commandIndex,
                                ReplayedSteps = replayedSteps,
                                Message = $"命令重放异常（事件 #{e.Sequence}，命令 {e.Command.Kind}）：{ex.Message}",
                            };
                        }
                        commandIndex++;
                        break;

                    case RoomRunEventKind.TestRules:
                        runtime.SetTestRules(e.TestRules.ToRules());
                        break;

                    case RoomRunEventKind.Step:
                    {
                        int actualFrame = runtime.AuthSim.Frame;
                        uint actualChecksum = SimChecksum.ComputeChecksum(runtime.AuthSim);
                        if (actualFrame != e.Step.Frame || actualChecksum != e.Step.Checksum)
                        {
                            // 分歧帧的字段级报告：录制世界（环内）vs 重放世界（当前 AuthSim）。
                            // 两侧在上一帧仍逐位一致（checksum 已证），故差异全部来自本帧执行——
                            // 这正是"哪里开始不同"的直接答案。
                            SimWorldDiff.Report divergenceReport = null;
                            string diffNote;
                            if (record.TryGetWorld(e.Step.Frame, out SimWorldState recordedWorld))
                            {
                                divergenceReport = SimWorldDiff.Compare(recordedWorld, runtime.AuthSim);
                                diffNote = "\n" + divergenceReport.ToText();
                            }
                            else
                            {
                                diffNote = record.HasWorlds
                                    ? "\n（该帧不在世界环窗口内——只有帧号与 checksum；调大 diagnostics.world_ring_frames 可得字段级差异）"
                                    : "\n（记录未启用世界环——只有帧号与 checksum；启用 diagnostics.world_ring_frames 可得字段级差异）";
                            }

                            return new RoomReplayResult
                            {
                                Matches = false,
                                Truncated = record.Truncated,
                                FirstDivergenceFrame = e.Step.Frame,
                                DivergenceEventSequence = e.Sequence,
                                DivergenceCommandIndex = commandIndex,
                                ExpectedChecksum = e.Step.Checksum,
                                ActualChecksum = actualChecksum,
                                ReplayedSteps = replayedSteps,
                                DivergenceReport = divergenceReport,
                                Message = $"**首个分歧帧 {e.Step.Frame}**（事件 #{e.Sequence}）："
                                          + $"录制 checksum=0x{e.Step.Checksum:x8}，重放=0x{actualChecksum:x8}，"
                                          + $"重放实际帧={actualFrame}，已一致 {replayedSteps} 步"
                                          + diffNote,
                            };
                        }
                        replayedSteps++;
                        break;
                    }
                }
            }

            return new RoomReplayResult
            {
                Matches = true,
                Truncated = record.Truncated,
                ReplayedSteps = replayedSteps,
                Message = record.Truncated
                    ? $"记录截断：仅覆盖并重放前 {replayedSteps} 步（不能宣称整局一致）"
                    : $"重放一致：{replayedSteps} 步逐帧 checksum 相符",
            };
        }

        public static RoomReplayResult ReplayEnvelope(RoomRunEnvelope envelope, RoomConfig config,
            SimMapData map, CombatValues values, WeaponTable weapons, string currentBuildHash,
            string currentMapDigest, uint? gameplayDigestBase = null)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.Record == null) throw new ArgumentException("记录信封缺 Record", nameof(envelope));

            RoomRunIdentityResult identity = RoomRunIdentityVerifier.Verify(envelope, currentBuildHash,
                envelope.FixedConfigDigest, currentMapDigest);
            if (!identity.Ok)
            {
                return new RoomReplayResult
                {
                    IdentityError = true,
                    Message = identity.Message,
                };
            }

            RoomRunRecord record = envelope.Record.ToRecord();
            RoomConfig replayConfig = envelope.Config != null
                ? envelope.Config.ToConfig(map)
                : config;
            uint baseDigest = gameplayDigestBase ?? envelope.GameplayDigestBase;
            return Replay(record, replayConfig, map, values, weapons,
                baseDigest, envelope.FixedConfigDigest);
        }
    }
}
