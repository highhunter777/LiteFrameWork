﻿using System;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using LiteGame;
using LiteGame.Modules.Input;

namespace LiteGame.Modules.Dev
{

    /// <summary>装配期交给开发层的服务面（只读范围：容器/命令/场景/输入/会话）。</summary>
    public sealed class DevServices
    {
        public SegmentContext Context;
        public CommandCenter Commands;
        public ServiceContainer Services;
        public IDevSession Session;
        public ISceneService Scenes;
        public IInputService Input;
    }

    /// <summary>
    /// 开发装配钩子（**产品侧唯一静态缝**，M3-6b 3b）：`LiteGame.Dev` 在
    /// `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 绑定，早于场景引导；release 构建没有 Dev 程序集
    /// → 恒为 null，产品走 `NullDevSession` / 跳过开发装配（与 `LocalTransportRegistry` 同款接缝纪律：
    /// 只存一个实现引用、不做服务定位；新增能力一律走接口）。
    /// </summary>
    public static class DevHooks
    {
        private static IDevAssembly s_current;

        /// <summary>当前开发装配（null = 发布形态/Dev 未绑定）。</summary>
        public static IDevAssembly Current => s_current;

        /// <summary>对局诊断端口（null = 发布形态）。</summary>
        public static IBattleDevDiagnostics Diagnostics => s_current != null ? s_current.BattleDiagnostics : null;

        /// <summary>绑定实现（Dev 程序集引导期调用一次）。</summary>
        public static void Bind(IDevAssembly assembly) => s_current = assembly;

        /// <summary>解绑（编辑器域重载/测试收尾）。</summary>
        public static void Unbind() => s_current = null;
    }
}
