using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteFramework;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·测试模式页：进入/退出/传送到准心 + 现场开关（即时生效）+ 爆头区/持枪幅度实时调。
    /// **静态启动配置不在本页**——在场景 DevStartupConfig（Inspector 随场景保存，进房快照）；本页只管进入/退出与局内可调项。
    /// 分层裁决：读数与动作全部经 <see cref="DebugKernel"/> 门面（《GM 与调试工具分层专项设计》§7——投影不另拼读数、
    /// 不另写动作）；内核之下开关单表/请求桥仍是唯一写口（新增开关只改那张表——本页零改动）；
    /// 进入/退出/传送 = 一次性请求桥（传送的实际派发在对局内经 ProcedureBattle → <c>SendQueued(TeleportToAimCommand)</c> 进审计环）；
    /// 爆头带覆写写通道 = <see cref="MatchTestRules"/> 活动局实例 + TestModeRuntime 待用值双落
    /// （判定路径读注入实例——确定性判定不读静态，含 dev 宏内）。
    /// </summary>
    internal sealed class GmTestModePage : IGmPage
    {
        public GUIContent Title => new GUIContent("本地测试模式");

        public void OnShow() => RefreshTestRoomMaps();
        public void OnHide() { }
        public void Poll()
        {
            if (_testRoomMaps.Count == 0) RefreshTestRoomMaps();
        }

        public void Draw()
        {
            DrawTestModeSection();
            DrawCloudGmBlock();
            DrawBotOpsBlock();
            DrawHeadExportBlock();
            DrawLookFovBlock();
        }

        // ---- 联机测试房（云端 GM；联机形态下上方"现场开关"只作用于进程内本地服）----

        private string _gmUrl;
        private string _gmKey;
        private string _gmRoom;
        private int _gmFlags;
        private string _gmStatus = "未下发";
        private bool _gmInit;

        private readonly List<(int Id, string Name)> _testRoomMaps = new List<(int Id, string Name)>();
        private int _testRoomMapIndex;
        private bool _testRoomBusy;
        private string _testRoomStatus = "(未创建)";

        /// <summary>
        /// 云端测试房与 GM 下发：普通房 = standard（大厅建房，正常结算）；云端测试房 = sandbox
        /// （隐藏、不进大厅、不结算，地图随建房票据盖章）。测试房由本页显式创建；免死/无限弹药
        /// 由服务端权威生效，只对带模板建成的隐藏房有效，普通房 409。
        /// </summary>
        private void DrawCloudGmBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("云端测试房（sandbox）", EditorStyles.boldLabel);

            MetaEntryConfig deploy = MetaDeployConfig.LoadWithBaked();
            if (!_gmInit)
            {
                _gmUrl = deploy.ResolvedGmUrl;
                _gmKey = deploy.GmKey;
                _gmInit = true;
            }

            MetaEntryService meta = GmEditorResolve.ResolveFromContainer<MetaEntryService>();
            bool configured = meta != null && meta.IsConfigured;
            bool loggedIn = configured && !string.IsNullOrEmpty(meta.AccountId);
            bool roomKeyReady = !string.IsNullOrEmpty(deploy.RoomGmKey);

            EditorGUILayout.HelpBox(
                "普通房 = standard（大厅选图/人数，正常结算）；云端测试房 = sandbox（隐藏、不进大厅、不结算）。"
                + "测试房由本页显式创建，地图随建房票据盖章。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_testRoomMaps.Count > 0)
                {
                    var names = new string[_testRoomMaps.Count];
                    for (int i = 0; i < _testRoomMaps.Count; i++) names[i] = _testRoomMaps[i].Name;
                    _testRoomMapIndex = EditorGUILayout.Popup("测试房地图", _testRoomMapIndex, names, GUILayout.Width(260));
                }
                else
                {
                    EditorGUILayout.LabelField("测试房地图", "地图表未装载", GUILayout.Width(260));
                    if (GUILayout.Button("↻", GUILayout.Width(24))) RefreshTestRoomMaps();
                }
            }

            if (!configured) EditorGUILayout.HelpBox("Meta 未装配/未配置——先进入 Play 并完成登录。", MessageType.Warning);
            else if (!loggedIn) EditorGUILayout.HelpBox("当前未登录——先在大厅完成游客登录。", MessageType.Warning);
            if (!roomKeyReady) EditorGUILayout.HelpBox("缺测试房口令：deploy.args/命令行加 -room.gmkey=<口令>。", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!loggedIn || !roomKeyReady || _testRoomMaps.Count == 0 || _testRoomBusy))
            {
                if (GUILayout.Button(_testRoomBusy ? "创建中……" : "创建并进入云端测试房"))
                    CreateTestRoomAsync().ForgetLogged("GmTestModePage");
            }
            EditorGUILayout.LabelField("测试房状态：" + _testRoomStatus, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("GM 规则下发（仅云端测试房）", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "免死/无限弹药由服务端权威生效，客户端预测按快照 gm_flags 同值；只对云端测试房生效，普通房返回 409。",
                MessageType.Info);

            _gmUrl = EditorGUILayout.TextField("GM 地址", _gmUrl ?? "");
            _gmKey = EditorGUILayout.PasswordField("GM 密钥", _gmKey ?? "");
            _gmRoom = EditorGUILayout.TextField("房号（留空 = 唯一测试房）", _gmRoom ?? "");

            using (new EditorGUILayout.HorizontalScope())
            {
                bool noDeath = (_gmFlags & LiteSim.MatchTestRules.FlagNoDeath) != 0;
                if (GUILayout.Button((noDeath ? "✓ " : "") + "免死"))
                {
                    _gmFlags ^= LiteSim.MatchTestRules.FlagNoDeath;
                    SendGmRules();
                }
                bool infinite = (_gmFlags & LiteSim.MatchTestRules.FlagInfiniteAmmo) != 0;
                if (GUILayout.Button((infinite ? "✓ " : "") + "无限弹药"))
                {
                    _gmFlags ^= LiteSim.MatchTestRules.FlagInfiniteAmmo;
                    SendGmRules();
                }
                if (GUILayout.Button("关闭全部 GM"))
                {
                    _gmFlags = 0;
                    SendGmRules();
                }
            }
            EditorGUILayout.LabelField("状态：" + _gmStatus, EditorStyles.wordWrappedLabel);
        }

        /// <summary>地图候选 = 当前配置表 tbmap 全行（与大厅开房选图同源）；表未装载时留空，进入 Play 后由 Poll 刷新。</summary>
        private void RefreshTestRoomMaps()
        {
            _testRoomMaps.Clear();
            IConfigService config = GmEditorResolve.ResolveFromContainer<IConfigService>();
            if (config == null || !config.Loaded || config.Tables?.Tbmap == null)
            {
                _testRoomMapIndex = 0;
                return;
            }

            var rows = config.Tables.Tbmap.DataList;
            for (int i = 0; i < rows.Count; i++)
                _testRoomMaps.Add((rows[i].MapId, rows[i].Name));
            _testRoomMapIndex = Mathf.Clamp(_testRoomMapIndex, 0, Mathf.Max(0, _testRoomMaps.Count - 1));
        }

        /// <summary>显式 sandbox 建房：模板固定 <see cref="MetaEntryService.TestRoomTemplate"/>，口令取部署注入；
        /// 成功后服务端票据即达、流程机进房，房号回填到 GM 规则区。</summary>
        private async UniTask CreateTestRoomAsync()
        {
            MetaEntryService meta = GmEditorResolve.ResolveFromContainer<MetaEntryService>();
            if (meta == null || !meta.IsConfigured) { _testRoomStatus = "Meta 未装配/未配置"; return; }
            if (string.IsNullOrEmpty(meta.AccountId)) { _testRoomStatus = "未登录"; return; }
            if (_testRoomMaps.Count == 0) { _testRoomStatus = "地图表未装载"; return; }

            MetaEntryConfig deploy = MetaDeployConfig.LoadWithBaked();
            if (string.IsNullOrEmpty(deploy.RoomGmKey)) { _testRoomStatus = "缺 room.gmkey"; return; }

            int mapId = _testRoomMaps[_testRoomMapIndex].Id;
            _testRoomBusy = true;
            _testRoomStatus = $"创建中（{MetaEntryService.TestRoomTemplate}，地图 #{mapId}）……";
            try
            {
                MetaCall<JoinTicketInfo> call = await meta.CreateRoomAsync(
                    mapId, 0, MetaEntryService.TestRoomTemplate, deploy.RoomGmKey);
                if (call.Ok)
                {
                    _gmRoom = call.Value.RoomId;
                    _testRoomStatus = $"已创建并进入：{call.Value.RoomId}（地图 #{mapId}）";
                }
                else
                {
                    _testRoomStatus = "失败：" + call.Failure.Code + " " + call.Failure.Detail;
                }
            }
            catch (Exception ex)
            {
                _testRoomStatus = "异常：" + ex.Message;
            }
            finally
            {
                _testRoomBusy = false;
            }
        }

        /// <summary>同步下发（编辑器低频操作；超时 5 s，不阻塞到无响应）。口令与响应正文只进状态行、不落日志。</summary>
        private void SendGmRules()
        {
            if (string.IsNullOrEmpty(_gmUrl) || string.IsNullOrEmpty(_gmKey))
            {
                _gmStatus = "缺 GM 地址/密钥——deploy.args 或命令行加 -gm.url / -gm.key";
                return;
            }

            try
            {
                string body = string.IsNullOrEmpty(_gmRoom)
                    ? "{\"flags\":" + _gmFlags + "}"
                    : "{\"room\":\"" + _gmRoom + "\",\"flags\":" + _gmFlags + "}";

                using var client = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(5) };
                using var content = new System.Net.Http.StringContent(
                    body, System.Text.Encoding.UTF8, "application/json");
                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Post, _gmUrl + "/gm/rules");
                request.Headers.Add("X-GM-Key", _gmKey);
                request.Content = content;
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                using var reader = new System.IO.StreamReader(
                    response.Content.ReadAsStreamAsync().GetAwaiter().GetResult());
                _gmStatus = ((int)response.StatusCode) + " " + reader.ReadToEnd();
            }
            catch (System.Exception ex)
            {
                _gmStatus = "失败：" + ex.Message;
            }
        }

        // ---- 测试模式（进入/退出/传送 + 现场开关；启动配置在场景 DevStartupConfig）----

        private void DrawTestModeSection()
        {
            EditorGUILayout.LabelField("本地测试模式（Room-Test）", EditorStyles.boldLabel);
            DebugKernel kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            if (kernel == null)
            {
                EditorGUILayout.HelpBox("DebugKernel 未装配——先进入 Play。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(kernel.IsTestModeActive ? "进行中（本地服 Room-Test）" : "未进入");

                using (new EditorGUILayout.HorizontalScope())
                {
                    // 启动快照由 ProcedureMain 在消费进房请求时单点落值（未挂 DevStartupConfig 则套默认口径）
                    if (GUILayout.Button("进入本地测试模式")) kernel.RequestEnterTestMode();
                    if (GUILayout.Button("退出本地测试模式")) kernel.RequestExitTestMode();   // 对局内 = 离场并关模式；主菜单 = 只关模式
                    if (GUILayout.Button("传送到准心")) kernel.RequestTeleport();
                }

                EditorGUILayout.LabelField("现场开关（即时生效，不必重进房）", EditorStyles.miniBoldLabel);
                // 清单/显示名经内核发现面（选项表单源）；写点唯一（TrySetOption → 单表 Set）
                foreach (TestModeOptions.Info option in kernel.Options)
                {
                    if (option.Group != TestModeOptions.Group.Live) continue;      // 启动项在 DevStartupConfig（下方只读一览）
                    bool value = GUILayout.Toggle(kernel.GetOption(option.Id), "  " + option.Label);
                    if (value != kernel.GetOption(option.Id))
                        kernel.TrySetOption(option.Id.ToString(), value, out _);
                }

                var startup = new System.Text.StringBuilder("启动项（进房时快照）：");
                foreach (TestModeOptions.Info option in kernel.Options)
                {
                    if (option.Group != TestModeOptions.Group.Startup) continue;
                    startup.Append(' ').Append(option.Id).Append('=').Append(kernel.GetOption(option.Id) ? "开" : "关");
                }
                startup.Append(" bot=").Append(kernel.StartupBotCount);
                EditorGUILayout.LabelField(startup.ToString(), EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.HelpBox(
                "本地测试模式＝本进程内本地服（Room-Test，无 Socket/票据），用于单机逻辑与表现；云端测试房见下方「云端测试房」块。\n"
                + "启动项（免死/bot 数/冻结/传送）与表现类开关的**进房初值**都在场景 DevStartupConfig 上编辑（Inspector，随场景保存）；"
                + "本节以下是**局内可调**项，即时生效。", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选中场景 DevStartupConfig", GUILayout.Width(150)))
                {
                    var config = UnityEngine.Object.FindAnyObjectByType<DevStartupConfig>(FindObjectsInactive.Include);
                    if (config != null) Selection.activeObject = config;
                }
                EditorGUILayout.LabelField("启动配置在 Inspector 改（随场景保存）；改完再进房生效。", EditorStyles.miniLabel);
            }
            if (Application.isPlaying
                && UnityEngine.Object.FindObjectsByType<DevStartupConfig>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                EditorGUILayout.HelpBox("场景未挂 DevStartupConfig：启动配置不可用（进房套默认口径）。", MessageType.Warning);
            if (Application.isPlaying
                && UnityEngine.Object.FindObjectsByType<DebugRuntimeTuner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                EditorGUILayout.HelpBox("场景未挂 DebugRuntimeTuner：时钟旋钮不生效。", MessageType.Warning);
        }

        // ---- bot 操作（测试房补位假人：重置 + 无限生命开关指引）----

        private void DrawBotOpsBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("bot 操作", EditorStyles.boldLabel);
            DebugKernel kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            using (new EditorGUI.DisabledScope(kernel == null))
            {
                if (GUILayout.Button("重置假人（满血回出生点）") && kernel != null) kernel.RequestBotReset();
            }
            bool botInfiniteHp = kernel != null && kernel.GetOption(TestModeOptions.Id.BotInfiniteHp);
            EditorGUILayout.LabelField(
                "「bot 无限生命」开关在上方「现场开关」清单（当前 "
                + (botInfiniteHp ? "开" : "关")
                + "）——开启后假人每帧回满、永不死（命中/飘字反馈照常）；两项都只作用于补位假人。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox(
                "重置假人：对局内即点即生效（本地服权威侧满血回出生点 + 动画按新生命退出死亡叶，不用重进房）；"
                + "未在对局中点击会挂起到进房后执行。",
                MessageType.Info);
        }

        // ---- 爆头区实时调（运行态覆写；判定与身位可视化同读注入实例）----

        /// <summary>爆头区导出（调参滑杆在「调试参数」页；本块只保留编辑器独占的导出动作）。当前值读数经内核调参发现面。</summary>
        private static void DrawHeadExportBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("爆头区导出（滑杆在「调试参数」页）", EditorStyles.boldLabel);
            DebugKernel kernel = GmEditorResolve.ResolveFromContainer<DebugKernel>();
            if (kernel == null)
            {
                EditorGUILayout.HelpBox("DebugKernel 未装配——先进入 Play（当前值读数经内核调参发现面）。", MessageType.Warning);
                return;
            }
            float height = LiteSim.CombatConfig.HitscanHeight;
            float shown = TunableValue(kernel, "head.line");
            float radiusShown = TunableValue(kernel, "head.radius");
            EditorGUILayout.LabelField($"比例 {shown / height:F3}    带高 {height - shown:F3} m    当前导出值：下沿 {LiteSim.CombatConfig.HeadHitLine:F3} m / 半径 {LiteSim.CombatConfig.HeadshotRadius:F3} m");
            if (GUILayout.Button("导出当前爆头区到 HeadBake.g.cs"))
            {
                string report = LiteGame.EditorTools.HeadHitLineTuner.Export(shown / height, radiusShown);
                EditorUtility.DisplayDialog("爆头区导出", report, "OK");
            }
            EditorGUILayout.HelpBox(
                "对局中点导出会触发重编译并中断对局（domain reload）——先记下数值，出对局后再导。",
                MessageType.Info);
        }

        /// <summary>按 Id 读调参当前值（经内核发现面——投影不自拼读数；未登记返回 0 并不再深究）。</summary>
        private static float TunableValue(DebugKernel kernel, string id)
        {
            IReadOnlyList<DebugTunable> tunables = kernel.Tunables;
            for (int i = 0; i < tunables.Count; i++)
                if (string.Equals(tunables[i].Id, id, StringComparison.Ordinal)) return tunables[i].Get();
            return 0f;
        }

        // ---- 视角 FOV（编辑器独占场景值；灵敏度/ADS 收敛在「调试参数」页）----

        /// <summary>FOV 直写运行时 vcam lens（场景值单源在 BattleRig.unity——Play 退出即回场景值，定档后写回场景）。</summary>
        private static void DrawLookFovBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("视角 FOV 试调（场景值，定档写回场景）", EditorStyles.boldLabel);
            const float refFov = 75f;         // Player 机场景 FOV（BattleRig.unity）
            const float refAdsFov = 55f;      // Aim 机场景 FOV
            EditorGUI.BeginChangeCheck();
            float fov = EditorGUILayout.Slider("腰射 FOV（写 Player 机）", FindVcamFov("Player Camera", refFov), 60f, 100f);
            float adsFov = EditorGUILayout.Slider("ADS FOV（写 Aim 机）", FindVcamFov("Aim Camera", refAdsFov), 45f, 70f);
            if (EditorGUI.EndChangeCheck())
            {
                SetVcamFov("Player Camera", fov);
                SetVcamFov("Aim Camera", adsFov);
            }
            if (GUILayout.Button("复位（回场景值）"))
            {
                SetVcamFov("Player Camera", refFov);
                SetVcamFov("Aim Camera", refAdsFov);
            }
            EditorGUILayout.HelpBox(
                "FOV 直写运行时 vcam（Play 退出回场景值）；灵敏度/ADS 收敛在「调试参数」页。",
                MessageType.Info);
        }

        /// <summary>按名找运行时 vcam（CinemachineVirtualCamera 组件——按串取，Editor 程序集不引
        /// Cinemachine 装配，lens 经 SerializedObject 读写，避免为调参面扩装配依赖）。
        /// 找不到（BattleRig 未加载/非 Play）= null。</summary>
        private static UnityEngine.Component FindVcam(string vcamName)
        {
            var go = UnityEngine.GameObject.Find(vcamName);
            return go != null ? go.GetComponent("CinemachineVirtualCamera") : null;
        }

        /// <summary>读 vcam FOV（找不到 = 参照值兜底显示）。</summary>
        private static float FindVcamFov(string vcamName, float fallback)
        {
            var vcam = FindVcam(vcamName);
            if (vcam == null) return fallback;
            var lens = new UnityEditor.SerializedObject(vcam).FindProperty("m_Lens");
            var fov = lens?.FindPropertyRelative("FieldOfView");
            return fov != null ? fov.floatValue : fallback;
        }

        private static void SetVcamFov(string vcamName, float fov)
        {
            var vcam = FindVcam(vcamName);
            if (vcam == null) return;
            var so = new UnityEditor.SerializedObject(vcam);
            var fovProp = so.FindProperty("m_Lens")?.FindPropertyRelative("FieldOfView");
            if (fovProp == null) return;
            fovProp.floatValue = fov;
            so.ApplyModifiedPropertiesWithoutUndo();   // 运行时内存值——Play 退出即回场景值
        }

    }
}
