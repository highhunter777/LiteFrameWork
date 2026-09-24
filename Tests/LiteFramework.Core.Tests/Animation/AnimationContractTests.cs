using System.Collections.Generic;
using LiteFramework;
using LiteFramework.Animation;
using Xunit;

namespace LiteFramework.Core.Tests.Animation
{
    /// <summary>
    /// 动画播放契约验收（《动画模块专项设计》§13 L1 行"Resolver、覆盖顺序、通道提交、Handle 代次、
    /// 终态与事件去重的纯规则 | 失败不抢占；终态恰好一次；旧请求无写入权；容量受限"）。
    /// 全替身后端、零引擎——本层不碰 Animator/PlayableGraph。
    /// </summary>
    public sealed class AnimationContractTests
    {
        private static AnimationProfile Profile(FallbackPolicy policy = FallbackPolicy.Reject)
        {
            var p = new AnimationProfile(policy);
            p.Register(new AnimationDefinition(new AnimationId("idle"), AnimationChannel.Locomotion, "Base Layer.Idle", loop: true));
            p.Register(new AnimationDefinition(new AnimationId("run"), AnimationChannel.Locomotion, "Base Layer.Run", loop: true));
            p.Register(new AnimationDefinition(new AnimationId("reload"), AnimationChannel.UpperBody, "Upper.Reload"));
            p.Register(new AnimationDefinition(new AnimationId("death"), AnimationChannel.FullBody, "Full.Death"));
            return p;
        }

        // ---- 替身后端 ----

        private sealed class FakeBackend : IAnimationBackend
        {
            public AnimationBackendCapabilities Capabilities { get; set; } =
                AnimationBackendCapabilities.Looping | AnimationBackendCapabilities.SpeedOverride |
                AnimationBackendCapabilities.StartAtNormalized | AnimationBackendCapabilities.LayeredChannels;

            public readonly List<AnimationResolvedPlayback> Played = new List<AnimationResolvedPlayback>();
            public readonly List<AnimationChannel> Stopped = new List<AnimationChannel>();
            public readonly Dictionary<AnimationChannel, bool> FinishOnTick = new Dictionary<AnimationChannel, bool>();
            public bool PlaySucceeds = true;
            public bool Disposed;
            /// <summary>每通道已被 Tick 的累计秒数（验证单一驱动入口与时钟缩放归属）。</summary>
            public readonly Dictionary<AnimationChannel, float> TickedSeconds = new Dictionary<AnimationChannel, float>();

            public bool TryPlay(in AnimationResolvedPlayback playback)
            {
                if (!PlaySucceeds) return false;
                Played.Add(playback);
                return true;
            }

            public bool TryStop(AnimationChannel channel) { Stopped.Add(channel); return true; }

            public bool IsChannelActive(AnimationChannel channel) => Played.Count > 0;

            public bool Tick(AnimationChannel channel, float deltaSeconds)
            {
                TickedSeconds.TryGetValue(channel, out float acc);
                TickedSeconds[channel] = acc + deltaSeconds;
                return FinishOnTick.TryGetValue(channel, out bool f) && f;
            }

            public void Dispose() => Disposed = true;
        }

        private static AnimationStartResult Play(CharacterAnimationPlayer player, string id,
            AnimationChannel channel = AnimationChannel.Locomotion, float start = 0f, float speed = 1f)
            => player.Play(new AnimationRequest(new AnimationId(id), channel, start, speed));

        // ---- Resolver / 字段校验 ----

        [Fact]
        public void 解析_登记后可按ID解析出绑定()
        {
            var profile = Profile();
            bool ok = profile.TryResolve(
                new AnimationRequest(new AnimationId("run"), AnimationChannel.Locomotion),
                out var resolved, out var reason);

            Assert.True(ok);
            Assert.Equal("Base Layer.Run", resolved.Binding);
            Assert.Equal(AnimationChannel.Locomotion, resolved.Channel);
            Assert.Equal(AnimationStartResult.Reason.None, reason);
        }

        [Fact]
        public void 解析_未登记ID_拒绝为InvalidDefinition()
        {
            var profile = Profile();
            bool ok = profile.TryResolve(
                new AnimationRequest(new AnimationId("no-such"), AnimationChannel.Locomotion),
                out _, out var reason);

            Assert.False(ok);
            Assert.Equal(AnimationStartResult.Reason.InvalidDefinition, reason);
        }

        [Theory]
        [InlineData(0f)]          // 速度零
        [InlineData(-1f)]         // 负速度
        public void 解析_非法速度_拒绝为InvalidRequest(float speed)
        {
            var profile = Profile();
            bool ok = profile.TryResolve(
                new AnimationRequest(new AnimationId("run"), AnimationChannel.Locomotion, 0f, speed),
                out _, out var reason);

            Assert.False(ok);
            Assert.Equal(AnimationStartResult.Reason.InvalidRequest, reason);
        }

        [Fact]
        public void 解析_NaN速度_拒绝而非静默()
        {
            var profile = Profile();
            bool ok = profile.TryResolve(
                new AnimationRequest(new AnimationId("run"), AnimationChannel.Locomotion, 0f, float.NaN),
                out _, out var reason);

            Assert.False(ok);
            Assert.Equal(AnimationStartResult.Reason.InvalidRequest, reason);
        }

        [Fact]
        public void 解析_越界起点_拒绝()
        {
            var profile = Profile();
            Assert.False(profile.TryResolve(new AnimationRequest(new AnimationId("run"), AnimationChannel.Locomotion, 1.5f),
                out _, out var reason));
            Assert.Equal(AnimationStartResult.Reason.InvalidRequest, reason);
        }

        [Fact]
        public void 解析_速度超出定义区间_拒绝为能力不支持_不静默夹取()
        {
            var profile = new AnimationProfile();
            profile.Register(new AnimationDefinition(new AnimationId("slow"), AnimationChannel.Locomotion, "B.Slow",
                minSpeed: 0.5f, maxSpeed: 1.5f));

            bool ok = profile.TryResolve(new AnimationRequest(new AnimationId("slow"), AnimationChannel.Locomotion, 0f, 3f),
                out _, out var reason);

            Assert.False(ok);
            Assert.Equal(AnimationStartResult.Reason.UnsupportedCapability, reason);
        }

        // ---- 回退（§4：限制深度、禁止环）----

        [Fact]
        public void 回退_策略为Reject时缺失定义直接拒绝()
        {
            var profile = Profile(FallbackPolicy.Reject);
            profile.RegisterFallback(new AnimationId("missing"), new AnimationId("idle"));

            Assert.False(profile.TryResolve(new AnimationRequest(new AnimationId("missing"), AnimationChannel.Locomotion),
                out _, out var reason));
            Assert.Equal(AnimationStartResult.Reason.InvalidDefinition, reason);
        }

        [Fact]
        public void 回退_策略为UseFallback时落到已登记的回退定义()
        {
            var profile = new AnimationProfile(FallbackPolicy.UseFallback);
            profile.Register(new AnimationDefinition(new AnimationId("idle"), AnimationChannel.Locomotion, "Base.Idle", loop: true));
            profile.RegisterFallback(new AnimationId("missing"), new AnimationId("idle"));

            Assert.True(profile.TryResolve(new AnimationRequest(new AnimationId("missing"), AnimationChannel.Locomotion),
                out var resolved, out _));
            Assert.Equal("Base.Idle", resolved.Binding);
        }

        [Fact]
        public void 回退_成环_登记即拒绝()
        {
            var profile = new AnimationProfile(FallbackPolicy.UseFallback);
            var a = new AnimationId("a");
            var b = new AnimationId("b");
            profile.RegisterFallback(a, b);      // a → b：此刻尚未成环

            // 回指（补上 b → a 即构成 a⇄b）与自指都要在登记期拒绝
            Assert.Throws<System.ArgumentException>(() => profile.RegisterFallback(b, a));
            Assert.Throws<System.ArgumentException>(() => profile.RegisterFallback(a, a));
        }

        [Fact]
        public void 回退_解析期受深度上限约束_不无限跟随()
        {
            // 造一条比 MaxFallbackDepth 更长的链，只有链尾登记了定义：
            // 解析必须停在深度上限内（找不到定义 → 拒绝），不能一路走到链尾
            var profile = new AnimationProfile(FallbackPolicy.UseFallback);
            var ids = new AnimationId[AnimationProfile.MaxFallbackDepth + 3];
            for (int i = 0; i < ids.Length; i++) ids[i] = new AnimationId("f" + i);

            for (int i = 0; i + 1 < ids.Length; i++) profile.RegisterFallback(ids[i], ids[i + 1]);
            profile.Register(new AnimationDefinition(ids[ids.Length - 1], AnimationChannel.Locomotion, "B.Tail"));

            Assert.False(profile.TryResolve(new AnimationRequest(ids[0], AnimationChannel.Locomotion), out _, out var reason),
                "回退深度受限：超出上限即拒绝");
            Assert.Equal(AnimationStartResult.Reason.InvalidDefinition, reason);
        }

        [Fact]
        public void 登记_非法定义_显性拒绝()
        {
            var profile = new AnimationProfile();
            Assert.Throws<System.ArgumentException>(() =>
                profile.Register(new AnimationDefinition(new AnimationId("x"), AnimationChannel.Locomotion, binding: "")));
            Assert.Throws<System.ArgumentException>(() =>
                profile.Register(new AnimationDefinition(default, AnimationChannel.Locomotion, "B.X")));
        }

        // ---- 终态恰好一次 / 句柄身份 ----

        [Fact]
        public void 终态_正常结束_Completed恰好一次()
        {
            var backend = new FakeBackend { FinishOnTick = { [AnimationChannel.Locomotion] = true } };
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            AnimationTerminalState last = AnimationTerminalState.None;
            player.OnTerminal += (h, t) => { terminals++; last = t; };

            var result = Play(player, "run");
            Assert.True(result.Accepted);
            player.Tick(0.016f);

            Assert.Equal(1, terminals);
            Assert.Equal(AnimationTerminalState.Completed, last);

            player.Tick(0.016f);          // 已终态：不再产生第二次
            Assert.Equal(1, terminals);
        }

        [Fact]
        public void 终态_循环播放不自然Completed()
        {
            var backend = new FakeBackend { FinishOnTick = { [AnimationChannel.Locomotion] = false } };
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;

            Play(player, "idle");          // idle 定义为 loop=true
            player.Tick(1f);
            player.Tick(1f);

            Assert.Equal(0, terminals);    // §5"循环播放不会自然 Completed"
        }

        [Fact]
        public void 终态_重复Stop不重复通知()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;

            var r = Play(player, "run");
            Assert.True(player.Stop(r.Handle, AnimationStopReason.Cancelled));
            Assert.False(player.Stop(r.Handle, AnimationStopReason.Cancelled));   // 幂等

            Assert.Equal(1, terminals);
        }

        [Fact]
        public void 句柄_旧Handle不能停止新播放()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;

            var first = Play(player, "run");                       // 同通道
            var second = Play(player, "idle");                     // 替换 first（first 得 Interrupted）

            Assert.Equal(1, terminals);
            Assert.False(player.Stop(first.Handle, AnimationStopReason.Cancelled), "旧 Handle 不得影响新播放");
            Assert.Equal(1, terminals);
            Assert.True(player.Stop(second.Handle, AnimationStopReason.Cancelled));   // 新播放仍可停
            Assert.Equal(2, terminals);
        }

        [Fact]
        public void 句柄_Owner换代后旧句柄全体失效()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            var stale = Play(player, "run");

            int generation = player.BumpOwnerGeneration();          // 对象被池化复用（§9）
            Assert.Equal(1, generation);

            var fresh = Play(player, "idle");
            Assert.False(player.Stop(stale.Handle, AnimationStopReason.Cancelled), "旧代次句柄无写入权");
            Assert.True(player.Stop(fresh.Handle, AnimationStopReason.Cancelled));
        }

        // ---- 通道仲裁 ----

        [Fact]
        public void 通道_不同通道互不替换()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;

            var move = Play(player, "run", AnimationChannel.Locomotion);
            var act = Play(player, "reload", AnimationChannel.UpperBody);

            Assert.Equal(0, terminals);                             // 两通道各自持有，无替换
            Assert.True(player.Stop(move.Handle, AnimationStopReason.Cancelled));
            Assert.Equal(1, terminals);
            Assert.True(player.Stop(act.Handle, AnimationStopReason.Cancelled));   // 上半身未受影响
            Assert.Equal(2, terminals);
        }

        [Fact]
        public void 通道_同通道替换_旧播放得Interrupted()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            AnimationTerminalState last = AnimationTerminalState.None;
            player.OnTerminal += (h, t) => last = t;

            Play(player, "run");
            Play(player, "idle");                                   // 替换

            Assert.Equal(AnimationTerminalState.Interrupted, last);
        }

        [Fact]
        public void 通道_替换加载中的请求_旧待提交Handle被终止()
        {
            var backend = new FakeBackend();
            var profile = new AnimationProfile();
            profile.Register(new AnimationDefinition(new AnimationId("loadA"), AnimationChannel.Locomotion, "B.A", requiresLoad: true));
            profile.Register(new AnimationDefinition(new AnimationId("loadB"), AnimationChannel.Locomotion, "B.B", requiresLoad: true));
            var player = new CharacterAnimationPlayer(backend, profile);

            AnimationTerminalState firstTerminal = AnimationTerminalState.None;
            var loading = Play(player, "loadA");
            player.OnTerminal += (h, t) => { if (h.Equals(loading.Handle)) firstTerminal = t; };
            Assert.Empty(backend.Played);                           // 需装载：尚未提交

            var replacement = Play(player, "loadB", AnimationChannel.Locomotion);
            Assert.Equal(AnimationTerminalState.Interrupted, firstTerminal);   // §6 旧待提交一并终止

            // 迟到装载回填：不得抢回通道
            profile.TryResolve(new AnimationRequest(new AnimationId("loadA"), AnimationChannel.Locomotion), out var stale, out _);
            player.CompleteLoad(loading.Handle, loadSucceeded: true, stale);
            Assert.Empty(backend.Played);                           // 迟到结果只释放自己的资源，不提交

            Assert.True(player.Stop(replacement.Handle, AnimationStopReason.Cancelled));
        }

        [Fact]
        public void 通道_装载失败得Failed终态()
        {
            var backend = new FakeBackend();
            var profile = new AnimationProfile();
            profile.Register(new AnimationDefinition(new AnimationId("loadA"), AnimationChannel.Locomotion, "B.A", requiresLoad: true));
            var player = new CharacterAnimationPlayer(backend, profile);

            AnimationTerminalState terminal = AnimationTerminalState.None;
            var loading = Play(player, "loadA");
            player.OnTerminal += (h, t) => terminal = t;

            profile.TryResolve(new AnimationRequest(new AnimationId("loadA"), AnimationChannel.Locomotion), out var resolved, out _);
            player.CompleteLoad(loading.Handle, loadSucceeded: false, resolved);

            Assert.Equal(AnimationTerminalState.Failed, terminal);
        }

        // ---- 拒绝不抢占（§6"提交前失败保持原播放"）----

        [Fact]
        public void 拒绝_未登记ID不影响现有播放()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) => terminals++;

            var current = Play(player, "run");
            var rejected = Play(player, "no-such");

            Assert.False(rejected.Accepted);
            Assert.Equal(AnimationStartResult.Reason.InvalidDefinition, rejected.RejectReason);
            Assert.Equal(0, terminals);                             // 现有播放未被替换
            Assert.True(player.Stop(current.Handle, AnimationStopReason.Cancelled));
        }

        [Fact]
        public void 拒绝_后端不支持的能力_显性拒绝不静默降级()
        {
            var backend = new FakeBackend { Capabilities = AnimationBackendCapabilities.Looping };   // 无 Layer/Speed/StartAt
            var player = new CharacterAnimationPlayer(backend, Profile());

            Assert.Equal(AnimationStartResult.Reason.UnsupportedCapability,
                Play(player, "run", speed: 2f).RejectReason);        // 速度不支持
            Assert.Equal(AnimationStartResult.Reason.UnsupportedCapability,
                Play(player, "run", start: 0.5f).RejectReason);      // 起点不支持
            Assert.Equal(AnimationStartResult.Reason.UnsupportedCapability,
                Play(player, "reload", AnimationChannel.UpperBody).RejectReason);   // 叠加层不支持
        }

        [Fact]
        public void 拒绝_后端执行失败_该Handle得Failed终态()
        {
            var backend = new FakeBackend { PlaySucceeds = false };
            var player = new CharacterAnimationPlayer(backend, Profile());

            AnimationTerminalState terminal = AnimationTerminalState.None;
            player.OnTerminal += (h, t) => terminal = t;

            var r = Play(player, "run");
            Assert.True(r.Accepted);                                // 已接受……
            Assert.Equal(AnimationTerminalState.Failed, terminal);  // ……但后端失败必须收 Failed，不假装在播
        }

        // ---- 释放与有界保留 ----

        [Fact]
        public void 释放_当前播放得OwnerDisposed_后端被释放_幂等()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());

            bool terminalFired = false;
            var r = Play(player, "run");
            player.OnTerminal += (h, t) => terminalFired = t == AnimationTerminalState.OwnerDisposed;

            player.Dispose();
            Assert.True(terminalFired, "释放时当前播放得 OwnerDisposed 终态（§9）");
            Assert.True(backend.Disposed, "后端最后释放");
            player.Dispose();                                       // 幂等：不抛

            Assert.False(Play(player, "run").Accepted, "释放后不再接受请求");
            Assert.Equal(AnimationStartResult.Reason.OwnerUnavailable, Play(player, "run").RejectReason);
        }

        [Fact]
        public void 终态_回调重入安全_不二次触发()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) =>
            {
                terminals++;
                player.Stop(h, AnimationStopReason.Cancelled);   // 回调里再 Stop：不得二次收终态
            };

            var r = Play(player, "run");
            player.Stop(r.Handle, AnimationStopReason.Cancelled);   // 收终态：回调里再 Stop 不得二次触发
            Assert.Equal(1, terminals);
        }

        [Fact]
        public void 释放_回调内重入Dispose不重复触发终态()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());
            int terminals = 0;
            player.OnTerminal += (h, t) =>
            {
                terminals++;
                player.Dispose();                                // 回调内释放：不得再对同一句柄收一次终态
            };

            Play(player, "run");
            player.Dispose();                                        // 外层释放（幂等）

            Assert.Equal(1, terminals);
            Assert.True(backend.Disposed);
        }

        [Fact]
        public void 终态保留_有界淘汰_过期查询返回未找到()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());

            AnimationHandle first = default;
            for (int i = 0; i < CharacterAnimationPlayer.TerminalRetentionCapacity + 5; i++)
            {
                var r = Play(player, "run");
                if (i == 0) first = r.Handle;
                player.Stop(r.Handle, AnimationStopReason.Cancelled);
            }

            Assert.True(player.EvictedTerminalRecords > 0, "超容量必须淘汰并计数（§12 容量不足要诊断）");
            Assert.False(player.TryGetState(first, out _), "过期终态记录不再可查（§5 有界保留）");
        }

        [Fact]
        public void 查询_在播与终态都可读()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());

            var r = Play(player, "run");
            Assert.True(player.TryGetState(r.Handle, out var playing));
            Assert.True(playing.IsPlaying);
            Assert.Equal(AnimationTerminalState.None, playing.Terminal);
            Assert.Equal(new AnimationId("run"), playing.Id);

            player.Stop(r.Handle, AnimationStopReason.Cancelled);
            Assert.True(player.TryGetState(r.Handle, out var done));
            Assert.False(done.IsPlaying);
            Assert.Equal(AnimationTerminalState.Cancelled, done.Terminal);
            Assert.Equal(new AnimationId("run"), done.Id);          // 终态记录保留 ID
        }

        [Fact]
        public void Tick_唯一驱动入口_按传入delta推进不自行缩放()
        {
            var backend = new FakeBackend();
            var player = new CharacterAnimationPlayer(backend, Profile());

            Play(player, "run");
            player.Tick(0.5f);                                      // 播放器不乘 TimeScale——原样交给后端
            Assert.Equal(0.5f, backend.TickedSeconds[AnimationChannel.Locomotion], 4);
        }

        [Fact]
        public void Tick_加载中不推进后端()
        {
            var backend = new FakeBackend();
            var profile = new AnimationProfile();
            profile.Register(new AnimationDefinition(new AnimationId("loadA"), AnimationChannel.Locomotion, "B.A", requiresLoad: true));
            var player = new CharacterAnimationPlayer(backend, profile);

            Play(player, "loadA");
            player.Tick(0.5f);
            Assert.False(backend.TickedSeconds.ContainsKey(AnimationChannel.Locomotion), "尚未提交的播放不推进后端");
        }
    }
}
