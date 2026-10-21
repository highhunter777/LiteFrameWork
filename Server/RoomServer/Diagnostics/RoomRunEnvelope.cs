using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using LiteSim;
using RoomServer.Runtime;
using RoomServer.Runtime.Diagnostics;

namespace RoomServer.Diagnostics
{
    /// <summary>房间规则快照（MatchRules 是只读 struct，JSON 用可写 DTO 承载）。</summary>
    public sealed class RoomRunRulesDto
    {
        public int DurationFrames { get; set; }
        public int KillLimit { get; set; }
        public int RespawnDelayFrames { get; set; }
        public int SpawnProtectionFrames { get; set; }

        public static RoomRunRulesDto From(in MatchRules rules) => new RoomRunRulesDto
        {
            DurationFrames = rules.DurationFrames,
            KillLimit = rules.KillLimit,
            RespawnDelayFrames = rules.RespawnDelayFrames,
            SpawnProtectionFrames = rules.SpawnProtectionFrames,
        };

        public MatchRules ToRules() => new MatchRules(DurationFrames, KillLimit, RespawnDelayFrames, SpawnProtectionFrames);
    }

    /// <summary>房间配置快照（重放只需要影响权威步进的字段）。</summary>
    public sealed class RoomRunConfigDto
    {
        public string RoomId { get; set; }
        public int ExpectedPlayers { get; set; }
        public long Seed { get; set; }
        public long MatchTimeLimitMs { get; set; }
        public long WaitingTimeoutMs { get; set; }
        public int MapId { get; set; }
        public RoomRunRulesDto Rules { get; set; }

        public static RoomRunConfigDto From(RoomConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new RoomRunConfigDto
            {
                RoomId = config.RoomId,
                ExpectedPlayers = config.ExpectedPlayers,
                Seed = config.Seed,
                MatchTimeLimitMs = config.MatchTimeLimitMs,
                WaitingTimeoutMs = config.WaitingTimeoutMs,
                MapId = config.MapId,
                Rules = RoomRunRulesDto.From(config.Rules),
            };
        }

        public RoomConfig ToConfig(SimMapData map)
        {
            return new RoomConfig
            {
                RoomId = RoomId,
                ExpectedPlayers = ExpectedPlayers,
                Seed = Seed,
                MatchTimeLimitMs = MatchTimeLimitMs,
                WaitingTimeoutMs = WaitingTimeoutMs,
                MapId = MapId,
                Rules = Rules?.ToRules() ?? MatchRules.Default,
                Map = map,
            };
        }
    }

    public sealed class RoomRunVector3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public static RoomRunVector3Dto From(in SimVector3 v) => new RoomRunVector3Dto { X = v.X, Y = v.Y, Z = v.Z };
        public SimVector3 ToVector() => new SimVector3(X, Y, Z);
    }

    public sealed class RoomRunObstacleDto
    {
        public int Kind { get; set; }
        public RoomRunVector3Dto Center { get; set; }
        public float Radius { get; set; }
        public float HalfX { get; set; }
        public float HalfZ { get; set; }
        public float Height { get; set; }
        public int Surface { get; set; }

        public static RoomRunObstacleDto From(in SimObstacle o) => new RoomRunObstacleDto
        {
            Kind = (int)o.Kind,
            Center = RoomRunVector3Dto.From(in o.Center),
            Radius = o.Radius,
            HalfX = o.HalfX,
            HalfZ = o.HalfZ,
            Height = o.Height,
            Surface = (int)o.Surface,
        };

        public SimObstacle ToObstacle() => new SimObstacle
        {
            Kind = (SimObstacleKind)Kind,
            Center = Center?.ToVector() ?? default,
            Radius = Radius,
            HalfX = HalfX,
            HalfZ = HalfZ,
            Height = Height,
            Surface = (SimObstacleSurface)Surface,
        };
    }

    /// <summary>地图快照（记录自包含，CLI 不依赖外部地图装载即可重放）。</summary>
    public sealed class RoomRunMapDto
    {
        public float GroundY { get; set; }
        public float HalfWidth { get; set; }
        public float HalfDepth { get; set; }
        public int ObstacleCount { get; set; }
        public RoomRunObstacleDto[] Obstacles { get; set; }
        public int SpawnPointCount { get; set; }
        public RoomRunVector3Dto[] SpawnPoints { get; set; }

        public static RoomRunMapDto From(SimMapData map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var obstacles = new RoomRunObstacleDto[map.ObstacleCount];
            for (int i = 0; i < obstacles.Length; i++) obstacles[i] = RoomRunObstacleDto.From(in map.Obstacles[i]);
            var spawns = new RoomRunVector3Dto[map.SpawnPointCount];
            for (int i = 0; i < spawns.Length; i++) spawns[i] = RoomRunVector3Dto.From(in map.SpawnPoints[i]);
            return new RoomRunMapDto
            {
                GroundY = map.GroundY,
                HalfWidth = map.HalfWidth,
                HalfDepth = map.HalfDepth,
                ObstacleCount = map.ObstacleCount,
                Obstacles = obstacles,
                SpawnPointCount = map.SpawnPointCount,
                SpawnPoints = spawns,
            };
        }

        public SimMapData ToMap()
        {
            var map = new SimMapData
            {
                GroundY = GroundY,
                HalfWidth = HalfWidth,
                HalfDepth = HalfDepth,
                ObstacleCount = Math.Min(Math.Max(ObstacleCount, 0), SimMapData.MaxObstacles),
                SpawnPointCount = Math.Min(Math.Max(SpawnPointCount, 0), SimMapData.MaxSpawnPoints),
            };
            if (Obstacles != null)
                for (int i = 0; i < map.ObstacleCount && i < Obstacles.Length; i++)
                    map.Obstacles[i] = Obstacles[i].ToObstacle();
            if (SpawnPoints != null)
                for (int i = 0; i < map.SpawnPointCount && i < SpawnPoints.Length; i++)
                    map.SpawnPoints[i] = SpawnPoints[i].ToVector();
            return map;
        }
    }

    public sealed class RoomRunMismatchDto
    {
        public int Frame { get; set; }
        public long Reports { get; set; }
        public DateTime? AtUtc { get; set; }
    }

    public sealed class RoomRunCommandDto
    {
        public RoomCommandKind Kind { get; set; }
        public int ConnectionId { get; set; }
        public int PlayerId { get; set; }
        public int NewConnectionId { get; set; }
        public long NowMs { get; set; }
        public ShutdownReason Reason { get; set; }
        public int InputFrame { get; set; }
        public int InputAckSnapshot { get; set; }
        public int InputViewFrame { get; set; }
        public int InputCount { get; set; }
        public SimInputFrame[] InputFrames { get; set; }

        public static RoomRunCommandDto From(in RoomRunCommand c) => new RoomRunCommandDto
        {
            Kind = c.Kind,
            ConnectionId = c.ConnectionId,
            PlayerId = c.PlayerId,
            NewConnectionId = c.NewConnectionId,
            NowMs = c.NowMs,
            Reason = c.Reason,
            InputFrame = c.InputFrame,
            InputAckSnapshot = c.InputAckSnapshot,
            InputViewFrame = c.InputViewFrame,
            InputCount = c.InputCount,
            InputFrames = c.InputFrames,
        };

        public RoomRunCommand ToCommand() => new RoomRunCommand(Kind, ConnectionId, PlayerId, NewConnectionId,
            NowMs, Reason, InputFrame, InputAckSnapshot, InputViewFrame, InputCount,
            InputFrames ?? Array.Empty<SimInputFrame>());
    }

    public sealed class RoomRunStepDto
    {
        public int Frame { get; set; }
        public uint Checksum { get; set; }

        public static RoomRunStepDto From(in RoomRunStep s) => new RoomRunStepDto { Frame = s.Frame, Checksum = s.Checksum };
        public RoomRunStep ToStep() => new RoomRunStep(Frame, Checksum);
    }

    public sealed class RoomRunTestRulesDto
    {
        public int Frame { get; set; }
        public bool HasRules { get; set; }
        public bool NoDeath { get; set; }
        public bool InfiniteAmmo { get; set; }
        public float HeadHitLineOverride { get; set; }
        public float HeadshotRadiusOverride { get; set; }

        public static RoomRunTestRulesDto From(in RoomRunTestRules r) => new RoomRunTestRulesDto
        {
            Frame = r.Frame,
            HasRules = r.HasRules,
            NoDeath = r.NoDeath,
            InfiniteAmmo = r.InfiniteAmmo,
            HeadHitLineOverride = r.HeadHitLineOverride,
            HeadshotRadiusOverride = r.HeadshotRadiusOverride,
        };

        public RoomRunTestRules ToRules() => new RoomRunTestRules(Frame, HasRules, NoDeath, InfiniteAmmo,
            HeadHitLineOverride, HeadshotRadiusOverride);
    }

    public sealed class RoomRunEventDto
    {
        public long Sequence { get; set; }
        public RoomRunEventKind Kind { get; set; }
        public RoomRunCommandDto Command { get; set; }
        public RoomRunStepDto Step { get; set; }
        public RoomRunTestRulesDto TestRules { get; set; }

        public static RoomRunEventDto From(in RoomRunEvent e)
        {
            RoomRunCommand command = e.Command;
            RoomRunStep step = e.Step;
            RoomRunTestRules rules = e.TestRules;
            return new RoomRunEventDto
            {
                Sequence = e.Sequence,
                Kind = e.Kind,
                Command = e.Kind == RoomRunEventKind.Command ? RoomRunCommandDto.From(in command) : null,
                Step = e.Kind == RoomRunEventKind.Step ? RoomRunStepDto.From(in step) : null,
                TestRules = e.Kind == RoomRunEventKind.TestRules ? RoomRunTestRulesDto.From(in rules) : null,
            };
        }

        public RoomRunEvent ToEvent()
        {
            switch (Kind)
            {
                case RoomRunEventKind.Command:
                {
                    if (Command == null) throw new InvalidDataException("命令事件缺 Command 负载");
                    RoomRunCommand command = Command.ToCommand();
                    return RoomRunEvent.ForCommand(Sequence, in command);
                }
                case RoomRunEventKind.Step:
                {
                    if (Step == null) throw new InvalidDataException("步进事件缺 Step 负载");
                    RoomRunStep step = Step.ToStep();
                    return RoomRunEvent.ForStep(Sequence, in step);
                }
                case RoomRunEventKind.TestRules:
                {
                    if (TestRules == null) throw new InvalidDataException("规则事件缺 TestRules 负载");
                    RoomRunTestRules rules = TestRules.ToRules();
                    return RoomRunEvent.ForTestRules(Sequence, in rules);
                }
                default: throw new InvalidDataException($"未登记的事件种类：{Kind}");
            }
        }
    }

    /// <summary>
    /// 权威世界快照 DTO（世界环的序列化形态）。只承载进 <see cref="SimChecksum"/> 口径的字段
    /// （<see cref="SimWorldDiff"/> 的比对面）——Cmds/Events 帧内瞬态与分配器状态不进 checksum、
    /// 不参与字段级 diff，故不序列化。数组直接复用 LiteSim 值类型（IncludeFields 序列化），
    /// 字段集演进由 SimWorldDiffCoverageTests 的覆盖契约守卫（漏字段 = 位级往返测试红）。
    /// </summary>
    public sealed class RoomRunWorldDto
    {
        public int Frame { get; set; }
        public ulong RngState { get; set; }
        public uint[] AliveBitmap { get; set; }
        public EntitySlot[] Entities { get; set; }
        public WeaponRuntime[] Weapons { get; set; }
        public ActionRuntime[] Actions { get; set; }
        public StatusSlotData[] Status { get; set; }
        public MatchBagSlot[] MatchBag { get; set; }
        public int[] Resources { get; set; }
        public ItemState[] Items { get; set; }
        public ProjectileState[] Projectiles { get; set; }
        public ZoneState[] Zones { get; set; }
        public byte[] Globals { get; set; }
        public byte[] CustomData { get; set; }
        public MatchStateData Match { get; set; }

        public static RoomRunWorldDto From(SimWorldState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return new RoomRunWorldDto
            {
                Frame = s.Frame,
                RngState = s.RngState,
                AliveBitmap = (uint[])s.AliveBitmap.Clone(),
                Entities = (EntitySlot[])s.Entities.Clone(),
                Weapons = (WeaponRuntime[])s.Weapons.Clone(),
                Actions = (ActionRuntime[])s.Actions.Clone(),
                Status = (StatusSlotData[])s.Status.Clone(),
                MatchBag = (MatchBagSlot[])s.MatchBag.Clone(),
                Resources = (int[])s.Resources.Clone(),
                Items = (ItemState[])s.Items.Clone(),
                Projectiles = (ProjectileState[])s.Projectiles.Clone(),
                Zones = (ZoneState[])s.Zones.Clone(),
                Globals = (byte[])s.Globals.Clone(),
                CustomData = (byte[])s.CustomData.Clone(),
                Match = s.Match,
            };
        }

        public SimWorldState ToWorld()
        {
            var s = new SimWorldState
            {
                Frame = Frame,
                RngState = RngState,
                Match = Match,
            };
            if (AliveBitmap != null) Array.Copy(AliveBitmap, s.AliveBitmap, Math.Min(AliveBitmap.Length, s.AliveBitmap.Length));
            if (Entities != null) Array.Copy(Entities, s.Entities, Math.Min(Entities.Length, s.Entities.Length));
            if (Weapons != null) Array.Copy(Weapons, s.Weapons, Math.Min(Weapons.Length, s.Weapons.Length));
            if (Actions != null) Array.Copy(Actions, s.Actions, Math.Min(Actions.Length, s.Actions.Length));
            if (Status != null) Array.Copy(Status, s.Status, Math.Min(Status.Length, s.Status.Length));
            if (MatchBag != null) Array.Copy(MatchBag, s.MatchBag, Math.Min(MatchBag.Length, s.MatchBag.Length));
            if (Resources != null) Array.Copy(Resources, s.Resources, Math.Min(Resources.Length, s.Resources.Length));
            if (Items != null) Array.Copy(Items, s.Items, Math.Min(Items.Length, s.Items.Length));
            if (Projectiles != null) Array.Copy(Projectiles, s.Projectiles, Math.Min(Projectiles.Length, s.Projectiles.Length));
            if (Zones != null) Array.Copy(Zones, s.Zones, Math.Min(Zones.Length, s.Zones.Length));
            if (Globals != null) Array.Copy(Globals, s.Globals, Math.Min(Globals.Length, s.Globals.Length));
            if (CustomData != null) Array.Copy(CustomData, s.CustomData, Math.Min(CustomData.Length, s.CustomData.Length));
            return s;
        }
    }

    public sealed class RoomRunRecordDto
    {
        public int FormatVersion { get; set; }
        public string RoomId { get; set; }
        public string MatchId { get; set; }
        public int ExpectedPlayers { get; set; }
        public ulong Seed { get; set; }
        public bool Truncated { get; set; }
        public List<RoomRunEventDto> Events { get; set; }

        /// <summary>世界环容量（0/缺省 = 未启用；与 WorldRing/WorldRingFrames 成对）。</summary>
        public int WorldRingCapacity { get; set; }

        /// <summary>世界环有效槽的帧号（与 <see cref="WorldRing"/> 一一对应；空槽不序列化）。</summary>
        public int[] WorldRingFrames { get; set; }

        /// <summary>世界环有效槽的世界快照。</summary>
        public RoomRunWorldDto[] WorldRing { get; set; }

        public static RoomRunRecordDto From(RoomRunRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var events = new List<RoomRunEventDto>(record.Events.Count);
            for (int i = 0; i < record.Events.Count; i++)
            {
                RoomRunEvent e = record.Events[i];
                events.Add(RoomRunEventDto.From(in e));
            }

            RoomRunRecordDto dto = new RoomRunRecordDto
            {
                FormatVersion = record.FormatVersion,
                RoomId = record.RoomId,
                MatchId = record.MatchId,
                ExpectedPlayers = record.ExpectedPlayers,
                Seed = record.Seed,
                Truncated = record.Truncated,
                Events = events,
            };

            if (record.HasWorlds)
            {
                SimWorldState[] ring = record.WorldRing;
                int[] frames = record.WorldRingFrames;
                var validFrames = new List<int>(frames.Length);
                var worlds = new List<RoomRunWorldDto>(frames.Length);
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] < 0) continue;
                    validFrames.Add(frames[i]);
                    worlds.Add(RoomRunWorldDto.From(ring[i]));
                }
                dto.WorldRingCapacity = ring.Length;
                dto.WorldRingFrames = validFrames.ToArray();
                dto.WorldRing = worlds.ToArray();
            }
            return dto;
        }

        public RoomRunRecord ToRecord()
        {
            var events = new RoomRunEvent[Events?.Count ?? 0];
            for (int i = 0; i < events.Length; i++) events[i] = Events[i].ToEvent();

            SimWorldState[] ring = null;
            int[] ringFrames = null;
            int capacity = WorldRingCapacity;
            if (capacity > 0)
            {
                ring = new SimWorldState[capacity];
                ringFrames = new int[capacity];
                for (int i = 0; i < capacity; i++)
                {
                    ring[i] = new SimWorldState();
                    ringFrames[i] = -1;
                }
                int count = Math.Min(WorldRingFrames?.Length ?? 0, WorldRing?.Length ?? 0);
                for (int i = 0; i < count; i++)
                {
                    int frame = WorldRingFrames[i];
                    if (frame < 0) continue;
                    int slot = frame % capacity;
                    WorldRing[i].ToWorld().CopyTo(ring[slot]);
                    ringFrames[slot] = frame;
                }
            }
            return new RoomRunRecord(FormatVersion, RoomId, MatchId, ExpectedPlayers, Seed, Truncated,
                events, ring, ringFrames);
        }
    }

    /// <summary>
    /// 宿主记录信封：内核记录 + 构建/配置/地图身份 + 日志尾 + mismatch 摘要。
    /// 身份三校验是“同构建才可重放”的失败闸；缺任何一项都可能跑出假分歧。
    /// </summary>
    public sealed class RoomRunEnvelope
    {
        public int SchemaVersion { get; set; }
        public string BuildHash { get; set; }
        public uint FixedConfigDigest { get; set; }
        public uint GameplayDigestBase { get; set; }
        public int MapId { get; set; }
        public string MapDigest { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime EndedAtUtc { get; set; }
        public string EndReason { get; set; }
        public RoomRunConfigDto Config { get; set; }
        public RoomRunMapDto Map { get; set; }
        public RoomRunRecordDto Record { get; set; }
        public string ServerLogTail { get; set; }
        public RoomRunMismatchDto ClientMismatch { get; set; }

        public static RoomRunEnvelope Create(RoomRunRecord record, string buildHash, uint fixedConfigDigest,
            uint gameplayDigestBase, int mapId, string mapDigest, RoomRunConfigDto config,
            DateTime startedAtUtc, DateTime endedAtUtc, string endReason, string serverLogTail,
            RoomRunMismatchDto clientMismatch, RoomRunMapDto map = null)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return new RoomRunEnvelope
            {
                SchemaVersion = RoomRunCodec.CurrentSchemaVersion,
                BuildHash = buildHash ?? string.Empty,
                FixedConfigDigest = fixedConfigDigest,
                GameplayDigestBase = gameplayDigestBase,
                MapId = mapId,
                MapDigest = mapDigest ?? string.Empty,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
                EndReason = endReason ?? string.Empty,
                Config = config,
                Map = map,
                Record = RoomRunRecordDto.From(record),
                ServerLogTail = serverLogTail ?? string.Empty,
                ClientMismatch = clientMismatch,
            };
        }
    }

    /// <summary>记录文件编解码：magic + schema 版本 + GZip(JSON)。</summary>
    public static class RoomRunCodec
    {
        public const int CurrentSchemaVersion = 1;
        private static readonly byte[] Magic = { (byte)'R', (byte)'R', (byte)'U', (byte)'N' };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        public static byte[] Encode(RoomRunEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            envelope.SchemaVersion = CurrentSchemaVersion;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

            using var output = new MemoryStream(json.Length / 2 + 64);
            output.Write(Magic, 0, Magic.Length);
            output.WriteByte((byte)CurrentSchemaVersion);
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(json, 0, json.Length);
            return output.ToArray();
        }

        public static RoomRunEnvelope Decode(byte[] data)
        {
            if (data == null || data.Length < Magic.Length + 1)
                throw new InvalidDataException("记录文件为空或过短");

            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) throw new InvalidDataException("记录文件 magic 不符（不是 RoomRun 记录）");

            int version = data[Magic.Length];
            if (version != CurrentSchemaVersion)
                throw new InvalidDataException($"记录 schema 版本不支持：文件={version} 当前={CurrentSchemaVersion}");

            using var input = new MemoryStream(data, Magic.Length + 1, data.Length - Magic.Length - 1);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = new MemoryStream();
            gzip.CopyTo(json);

            RoomRunEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<RoomRunEnvelope>(json.ToArray(), JsonOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("记录 JSON 解析失败：" + ex.Message, ex);
            }
            if (envelope == null) throw new InvalidDataException("记录 JSON 解析为空");
            if (envelope.SchemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"记录 schema 字段不匹配：{envelope.SchemaVersion}");
            return envelope;
        }
    }

    public sealed class RoomRunIdentityResult
    {
        public bool Ok { get; }
        public string Message { get; }

        public RoomRunIdentityResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }
    }

    /// <summary>重放前的身份闸：构建、固定配置摘要、地图摘要三项必须与录制端一致。</summary>
    public static class RoomRunIdentityVerifier
    {
        public static RoomRunIdentityResult Verify(RoomRunEnvelope envelope, string currentBuildHash,
            uint currentFixedConfigDigest, string currentMapDigest)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            var mismatches = new List<string>(3);
            if (!string.Equals(envelope.BuildHash, currentBuildHash, StringComparison.Ordinal))
                mismatches.Add($"buildHash 录制={envelope.BuildHash} 当前={currentBuildHash}");
            if (envelope.FixedConfigDigest != currentFixedConfigDigest)
                mismatches.Add($"fixedConfigDigest 录制=0x{envelope.FixedConfigDigest:x8} 当前=0x{currentFixedConfigDigest:x8}");
            if (!string.Equals(envelope.MapDigest, currentMapDigest, StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"mapDigest 录制={envelope.MapDigest} 当前={currentMapDigest}");

            if (mismatches.Count == 0)
                return new RoomRunIdentityResult(true, "身份一致");
            return new RoomRunIdentityResult(false, "身份不符：" + string.Join("；", mismatches));
        }

        public static RoomRunIdentityResult Verify(RoomRunEnvelope envelope, string currentBuildHash,
            string currentMapDigest) => Verify(envelope, currentBuildHash, envelope.FixedConfigDigest, currentMapDigest);
    }
}
