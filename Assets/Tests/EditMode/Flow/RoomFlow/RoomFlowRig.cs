using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.Config;
using LiteGame.Modules.Map;
using LiteGame.Modules.UI;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>当前生产接线：真实 StageMachine/ProcedureMatch/Battle/ProcedureFlow + 账户域/门面/票据源。</summary>
    internal sealed class RoomFlowRig
    {
        public readonly ClientScope RootScope = new ClientScope("Root");
        public readonly ServiceContainer RootServices = new ServiceContainer();
        public readonly ControlledMetaClient Client = new();
        public readonly ControlledAccountReentry ReentryPort = new();
        public readonly ControlledMatchSessionFactory Accounts;
        public readonly ControlledBattleRunFactory Runs = new();
        public readonly ControlledViewGroupFactory Views = new();
        public readonly ControlledSceneService Scenes = new();
        public readonly ControlledRoomSessionBridge RoomBridge = new();
        public readonly NoopStage Main = new("Main");
        public readonly NoopStage Error = new("Error");
        public readonly RoomFlowLifetime Lifetime;
        public readonly ProcedureMatch Match;
        public readonly ProcedureBattle Battle;
        public readonly StageMachine<ProcedureId, ProcedureArgs> Machine;
        public readonly AccountDomain Domain;
        public readonly MetaLoginService LoginService;
        public readonly AccountTicketSource Tickets;
        public readonly MetaSessionFacade Session;
        public readonly ProcedureFlow Flow;
        public readonly RecordingLoginSurface Surface = new();
        public readonly AccountReentry Reentry;

        /// <summary>默认装配全用本类假件；feedback/pages/scenes 非空时替换对应生产接线（反馈面/页面流/场景组）。</summary>
        public RoomFlowRig(ControlledMatchSessionFactory accounts = null, FeedbackService feedback = null,
            PageFlow pages = null, ISceneService scenes = null, CancellationToken rootToken = default)
        {
            Lifetime = new RoomFlowLifetime(rootToken);
            RootServices.RegisterInstance<IdentityContext>(Identity());
            RootServices.RegisterInstance<IMetaClient>(Client);
            RootServices.RegisterInstance<IAccountReentry>(ReentryPort);
            LoginService = new MetaLoginService(Client, () => "device-roomflow", "http://meta.test", Identity());
            RootServices.RegisterInstance<IMetaConfigFacts>(LoginService);
            Domain = NewDomain(RootScope, RootServices, ReentryPort);
            RootServices.RegisterInstance<IAccountDomain>(Domain);
            RootServices.Seal();
            RootScope.Register(Domain);
            Tickets = new AccountTicketSource(Domain);

            Accounts = accounts ?? new ControlledMatchSessionFactory(RootScope);
            Match = new ProcedureMatch(Accounts, Lifetime, feedback);
            Battle = new ProcedureBattle(null, null, new ControlledBattleCommands(), Views, RoomBridge,
                Lifetime, default, false, Runs, pages, scenes ?? Scenes, new ControlledMapProvider(), feedback, null);
            Machine = new StageMachine<ProcedureId, ProcedureArgs>("RoomFlowTest",
                (ProcedureId.Main, Main),
                (ProcedureId.Error, Error),
                (ProcedureId.Match, Match),
                (ProcedureId.Battle, Battle));
            Machine.Start(ProcedureId.Main);

            Flow = new ProcedureFlow(Machine, Tickets, Lifetime, rootToken);
            Session = new MetaSessionFacade(Domain, LoginService, Flow);
            Reentry = new AccountReentry(Flow, Flow, Domain, Surface);
        }

        public void Login()
        {
            Session.GuestLoginAsync().GetAwaiter().GetResult();                 // 真实门面登录路径
            if (!Domain.IsOpen) Domain.Open("acc-roomflow", "tok-roomflow");    // 账户域直开（装配等价于登录收口）
            Assert.IsTrue(Domain.IsOpen, "登录后账户域必须开启（前置）");
        }

        public void EnterMatch()
        {
            Session.QuickEnterAsync().GetAwaiter().GetResult();
            Machine.Advance();
        }

        public void AdvanceUntilNoPending(int max = 8)
        {
            for (int i = 0; i < max && Machine.HasPending; i++) Machine.Advance();
        }

        public void LeaveToMain()
        {
            if (!Machine.Started || Machine.Current == ProcedureId.Main) return;
            Machine.Request(ProcedureId.Main);
            Machine.Advance();
        }

        /// <summary>建账户域：优先注入 <see cref="IAccountReentry"/>（401 单点）；无该形参时退回最小构造。</summary>
        public static AccountDomain NewDomain(ClientScope rootScope, ServiceContainer rootServices,
            IAccountReentry reentry)
        {
            var ports = new Dictionary<Type, object>
            {
                [typeof(ClientScope)] = rootScope,
                [typeof(ServiceContainer)] = rootServices,
                [typeof(IAccountReentry)] = reentry,
            };

            ConstructorInfo best = null;
            object[] bestArgs = null;
            foreach (ConstructorInfo ctor in typeof(AccountDomain).GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                var args = new object[ps.Length];
                bool usable = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!ports.TryGetValue(ps[i].ParameterType, out object arg))
                    {
                        if (!ps[i].HasDefaultValue)
                        {
                            usable = false;
                            break;
                        }
                        args[i] = ps[i].DefaultValue;
                        continue;
                    }
                    args[i] = arg;
                }
                if (!usable) continue;
                if (best == null || ps.Length > best.GetParameters().Length)
                {
                    best = ctor;
                    bestArgs = args;
                }
            }

            Assert.IsNotNull(best, "AccountDomain 缺少可用构造（需要 ClientScope + ServiceContainer；401 需 IAccountReentry）");
            return (AccountDomain)best.Invoke(bestArgs);
        }

        public static IdentityContext Identity() => new IdentityContext(
            new BuildIdentity(LiteNet.BuildHash.Value, "0.1.0", "WindowsPlayer", string.Empty, 5),
            new SchemaIdentity(0, 0, 0, ActivationRecord.CurrentSchemaVersion));
    }

    internal sealed class NoopStage : IStage<ProcedureId, ProcedureArgs>
    {
        public NoopStage(string name) { }
        public int Enters;

        public void OnInit(IStageHost<ProcedureId, ProcedureArgs> m) { }
        public void OnEnter(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req) => Enters++;
        public void OnUpdate(IStageHost<ProcedureId, ProcedureArgs> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<ProcedureId, ProcedureArgs> m) { }
    }

    internal static class RoomFlowTestSupport
    {
        /// <summary>把已完成的可控任务收口；取消是允许的观测结果，成功/忙结果原样通过。</summary>
        public static void Drain(UniTask task, string message)
        {
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, message);
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
    }
}
