using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteTesting.Unity
{
    /// <summary>
    /// PlayMode 测试宿主：把帧驱动回调挂到 PlayerLoop
    /// （《UI测试开发专项设计》§7.1 "UiFixture 责任"第 2 项：页面注册表、UIService、导航栈、转场和 UI 时钟）。
    ///
    /// **为什么需要它**：生产环境里 `GameEntry.Update` 驱动容器的 Tickables——转场 runner 由
    /// `UIService.Tick` 帧末推进。PlayMode 用例若不建容器就裸 `new UIService(...)`，
    /// **没有任何东西调 Tick**——`ShowAsync` 会停在转场阶段永不完成（`IsOpen` 却已为 true，
    /// 正是 UI-U2 记录的"就绪条件错位"同一个坑）。
    ///
    /// EditMode 靠用例显式泵 Tick；PlayMode 有真实 PlayerLoop，故由本件承担。
    ///
    /// **不做接口绑定**：本程序集只依赖 `LiteTesting.Core` 与 UnityEngine（测试框架不得反向依赖
    /// 产品程序集——《测试开发框架总设计》§2 依赖方向）。故此处收 `Action&lt;float&gt;`，
    /// 由用例侧传 `ui.Tick` / `dialogs.Tick`。
    /// </summary>
    public sealed class PlayModeTicker : MonoBehaviour
    {
        /// <summary>当前实例（用例装配后取用；未挂返回 null）。</summary>
        public static PlayModeTicker Instance { get; private set; }

        /// <summary>挂载到受管对象并注册帧回调（随 Scope 销毁）。</summary>
        public static PlayModeTicker Attach(GameObject host, params Action<float>[] tickCallbacks)
        {
            var ticker = host.AddComponent<PlayModeTicker>();
            ticker.Register(tickCallbacks);
            return ticker;
        }

        private readonly List<Action<float>> _callbacks = new List<Action<float>>(4);

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(params Action<float>[] tickCallbacks)
        {
            if (tickCallbacks == null) return;
            foreach (Action<float> callback in tickCallbacks)
                if (callback != null && !_callbacks.Contains(callback)) _callbacks.Add(callback);
        }

        public void Unregister(Action<float> tickCallback) => _callbacks.Remove(tickCallback);

        private void Update()
        {
            // 与 GameEntry 同语义：变速由各服务内部时钟缩放，这里传真实帧间隔
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _callbacks.Count; i++)
                _callbacks[i](dt);
        }
    }
}
