using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using LiteGame.Adapters.Dev.LocalServer;
using LiteGameNext.Platform.Dev;
using LiteSim;
using UnityEngine;

namespace LiteGameNext.Dev
{
    /// <summary>开发面：本地服会话 + GM 命令字典；不进发布包（独立程序集，发布构建用 NullDevHooks）。</summary>
    public sealed class DevFeature : IDevHooks
    {
        private readonly Dictionary<string, Func<string[], string>> _commands =
            new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase);

        private ILocalRoomSession _session;
        private LiteGameNext.Platform.Config.ConfigService _config;
        private bool _shutdown;

        public DevFeature()
        {
            RegisterCommands();
        }

        public bool Enabled => !_shutdown;

        /// <summary>注入配置服务：本地服据此取真实地图、判定数值与规则；未注入则回落内置默认。</summary>
        public void Configure(LiteGameNext.Platform.Config.ConfigService config) => _config = config;

        /// <summary>已登记的 GM 命令数（诊断）。</summary>
        public int CommandCount => _commands.Count;

        /// <summary>逐帧驱动本地服（活动会话）。</summary>
        public void Tick(float deltaTime) => _session?.Tick(deltaTime);

        /// <summary>开始本地房间会话：进程内真 RoomRuntime，无 KCP。</summary>
        public UniTask<ILocalRoomSession> StartLocalRoomAsync(string roomId, int expectedPlayers,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _session?.Dispose();
            var session = new LocalRoomSession(roomId, expectedPlayers, _config);
            _session = session;
            Log.Info("[Dev] 本地房间已启动：" + roomId + " expected=" + expectedPlayers
                + " config=" + (_config == null ? "无" : "已注入"));
            return UniTask.FromResult<ILocalRoomSession>(session);
        }

        /// <summary>执行 GM 命令（首词为命令名，其余为参数）。</summary>
        public string ExecuteCommand(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "空命令";
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!_commands.TryGetValue(parts[0], out Func<string[], string> handler))
                return "未知命令：" + parts[0] + "（可用：" + string.Join("/", _commands.Keys) + "）";
            try
            {
                return handler(parts);
            }
            catch (Exception ex)
            {
                Log.Error("[Dev] 命令失败：" + line + " → " + ex.Message);
                return "命令失败：" + ex.Message;
            }
        }

        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            _session?.Dispose();
            _session = null;
            _commands.Clear();
        }

        private void RegisterCommands()
        {
            _commands["help"] = _ => "可用命令：" + string.Join("/", _commands.Keys);
            _commands["local"] = args => StartLocalCommand(args);
            _commands["teleport"] = args => "传送需对局活动世界（暂未接）";
            _commands["time"] = args => "时间缩放 " + Time.timeScale;
        }

        private string StartLocalCommand(string[] args)
        {
            string roomId = args.Length > 1 ? args[1] : "local-dev";
            int players = args.Length > 2 && int.TryParse(args[2], out int p) ? p : 2;
            StartLocalRoomAsync(roomId, players, CancellationToken.None).Forget(ex => Log.Error("[Dev] " + ex));
            return "本地房间启动中：" + roomId + " expected=" + players;
        }
    }
}
