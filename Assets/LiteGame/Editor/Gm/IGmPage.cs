using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱页件契约（<see cref="GmPanel"/> 壳的四页承载——烘焙工具箱页件范式同款：一页一职责，
    /// 说明→操作→报告自包含）。页件零业务逻辑：运行态读数经 <see cref="GmEditorResolve.ResolveFromContainer{T}"/>
    /// 取容器服务，开关写口唯一走 TestModeOptions / TestModeRuntime（分层裁决见各页类注释）。
    /// 生命周期：壳在翻页/开窗时调 <see cref="OnShow"/>、离场/关窗调 <see cref="OnHide"/>；
    /// <see cref="Poll"/> 仅活动页在 Play 中按 0.5s 节拍被调（非活动页不轮询——诊断读数只在页在场时刷新）。
    /// </summary>
    internal interface IGmPage
    {
        /// <summary>页签文本。</summary>
        GUIContent Title { get; }

        /// <summary>页在场期首帧（进入页签/开窗）——重读清单、恢复记忆值等。</summary>
        void OnShow();

        /// <summary>页离场（翻页/关窗）——持久化记忆值、摘场景挂载等。</summary>
        void OnHide();

        /// <summary>绘制页体（壳 OnGUI 内调用）。</summary>
        void Draw();

        /// <summary>Play 中 0.5s 轮询（仅活动页）——刷新状态读数/自动列表。</summary>
        void Poll();
    }
}
