using System;
using LiteFramework;
using LiteFramework;
using LiteSim;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 传送处理器（dev 专用，GmOnly 注册）：Execute = 解析准心点 → 记录出发位 → 交权威侧分发
    /// （本地服宿主钩子，见 <see cref="ILocalTestHooks.TeleportDispatch"/>）；Undo = 回出发位（同通道）。
    /// 捕获的委托全部由注册方（ProcedureBattle）按本局装配注入——准心解析、本地位置、权威分发。
    /// 门（测试模式开启/传送启用）在入队侧裁决；Execute 不再复查（命令即已过入队门）。
    /// </summary>
    public sealed class TeleportToAimHandler : IUndoableCommandHandler<TeleportToAimCommand>
    {
        private readonly Func<Vector3?> _aimOf;              // 准心点（null = 未解算）
        private readonly Func<SimVector3?> _originOf;        // 本地实体当前位置（null = 未就绪）
        private readonly Action<SimVector3> _dispatch;       // 交权威侧执行

        public TeleportToAimHandler(Func<Vector3?> aimOf, Func<SimVector3?> originOf, Action<SimVector3> dispatch)
        {
            _aimOf = aimOf ?? throw new ArgumentNullException(nameof(aimOf));
            _originOf = originOf ?? throw new ArgumentNullException(nameof(originOf));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        }

        /// <summary>派发传送请求；出发位随命令留档（撤销回传用）。目标即当前位置 = 拒绝（重入守卫）。</summary>
        public CommandResult Execute(TeleportToAimCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            SimVector3? origin = _originOf();
            if (origin == null)
                return CommandResult.Fail(CommandReject.HandlerRejected, "本地实体未就绪——无从记录出发位");

            Vector3? aim = _aimOf();
            if (aim == null)
                return CommandResult.Fail(CommandReject.HandlerRejected, "无瞄准点（准心未解算）");

            command.Origin = origin.Value;
            if (aim.Value.x == origin.Value.X && aim.Value.y == origin.Value.Y && aim.Value.z == origin.Value.Z)
                return CommandResult.Fail(CommandReject.HandlerRejected, "目标即当前位置——无需传送");

            _dispatch(new SimVector3(aim.Value.x, aim.Value.y, aim.Value.z));
            return CommandResult.Success("传送请求已交权威侧");
        }

        /// <summary>撤销 = 回出发位（同一条权威通道——一致性由同一分发闭包保证）。</summary>
        public void Undo(TeleportToAimCommand command) => _dispatch(command.Origin);
    }
}
