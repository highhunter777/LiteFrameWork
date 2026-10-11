using UnityEditor;
using UnityEngine;

namespace LiteGame.Editor
{
    /// <summary>
    /// GM 工具箱·测试模式页：进入/退出/传送到准心 + 现场开关（即时生效）+ 爆头区/持枪幅度实时调。
    /// **静态启动配置不在本页**——在场景 DebugTuner（Inspector 随场景保存，进房快照）；本页只管进入/退出与局内可调项。
    /// 分层裁决：现场开关清单/显示名/写口全部经 <see cref="TestModeOptions"/>（新增开关只改那张表——本页零改动）；
    /// 进入/退出/传送 = 一次性请求桥（<see cref="TestModeRuntime"/> 标志——**pre-Play 容器尚不存在**，不走命令中心；
    /// 传送的实际派发在对局内经 ProcedureBattle → <c>SendQueued(TeleportToAimCommand)</c> 进审计环）；
    /// 爆头带覆写写通道 = <see cref="MatchTestRules"/> 活动局实例 + <see cref="TestModeRuntime"/> 待用值双落
    /// （判定路径读注入实例——确定性判定不读静态，含 dev 宏内）。
    /// </summary>
    internal sealed class GmTestModePage : IGmPage
    {
        public GUIContent Title => new GUIContent("测试模式");

        public void OnShow() { }
        public void OnHide() { }
        public void Poll() { }

        public void Draw()
        {
            DrawTestModeSection();
            DrawHeadTuneBlock();
            DrawGripTuneBlock();
        }

        // ---- 测试模式（进入/退出/传送 + 现场开关；启动配置在场景 DebugTuner）----

        private void DrawTestModeSection()
        {
            EditorGUILayout.LabelField("测试模式", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(TestModeRuntime.Active ? "进行中（本地服 Room-Test）" : "未进入");

            using (new EditorGUILayout.HorizontalScope())
            {
                // 进入前把场景 DebugTuner 的启动配置快照进运行时（未挂则套默认口径）；F10 同一条链
                if (GUILayout.Button("进入测试模式")) { DebugTuner.ApplySnapshotOrDefaults(); TestModeRuntime.EnterRequested = true; }
                if (GUILayout.Button("退出测试模式")) TestModeRuntime.ExitRequested = true;              // 对局内 = 离场并关模式；主菜单 = 只关模式
                if (GUILayout.Button("传送到准心")) TestModeRuntime.TeleportRequested = true;
            }
            EditorGUILayout.HelpBox(
                "启动项（免死/bot 数/冻结/传送）与表现类开关的**进房初值**都在场景 DebugTuner 上编辑（Inspector，随场景保存）；"
                + "本节以下是**局内可调**项，即时生效。", MessageType.Info);

            EditorGUILayout.LabelField("现场开关（即时生效，不必重进房）", EditorStyles.miniBoldLabel);
            // 开关清单/显示名/写点全部经 TestModeOptions（新增开关只改那张表——本面板零改动）
            foreach (TestModeOptions.Info option in TestModeOptions.All)
            {
                if (option.Group != TestModeOptions.Group.Live) continue;      // 启动项在 DebugTuner（下方只读一览）
                bool value = GUILayout.Toggle(TestModeOptions.Get(option.Id), "  " + option.Label);
                if (value != TestModeOptions.Get(option.Id)) TestModeOptions.Set(option.Id, value);
            }

            var startup = new System.Text.StringBuilder("启动项（进房时快照）：");
            foreach (TestModeOptions.Info option in TestModeOptions.All)
            {
                if (option.Group != TestModeOptions.Group.Startup) continue;
                startup.Append(' ').Append(option.Id).Append('=').Append(TestModeOptions.Get(option.Id) ? "开" : "关");
            }
            startup.Append(" bot=").Append(TestModeRuntime.BotCount);
            EditorGUILayout.LabelField(startup.ToString(), EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("选中场景 DebugTuner", GUILayout.Width(150)))
                {
                    var tuner = UnityEngine.Object.FindAnyObjectByType<DebugTuner>(FindObjectsInactive.Include);
                    if (tuner != null) Selection.activeObject = tuner;
                }
                EditorGUILayout.LabelField("启动配置在 Inspector 改（随场景保存）；改完再进房生效。", EditorStyles.miniLabel);
            }
            if (Application.isPlaying
                && UnityEngine.Object.FindObjectsByType<DebugTuner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 0)
                EditorGUILayout.HelpBox("场景未挂 DebugTuner：启动配置不可用（进房套默认口径），时钟旋钮也不生效。", MessageType.Warning);
        }

        // ---- 爆头区实时调（运行态覆写；判定与身位可视化同读注入实例）----

        /// <summary>爆头区实时调（运行态覆写，不落盘；判定与身位可视化同读注入实例——本地服同进程同值，滑杆一动实弹即见 Crit 档变化）。
        /// 有活动局直写活动局并同步 TestModeRuntime 待用值（下次进房延续），无活动局只记待用值。</summary>
        private static void DrawHeadTuneBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("爆头区实时调（运行时覆写，不落盘）", EditorStyles.boldLabel);
            float height = LiteSim.CombatConfig.HitscanHeight;
            float shown = CurrentHeadLineShown();
            float radiusShown = CurrentHeadRadiusShown();
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider("爆头线下沿（m）", shown, 0.9f, height);
            float nextRadius = EditorGUILayout.Slider("爆头柱半径（m，窄于命中柱 0.45）", radiusShown, 0.15f, 0.45f);
            if (EditorGUI.EndChangeCheck())
                SetHeadTune(next, nextRadius);
            EditorGUILayout.LabelField($"比例 {shown / height:F3}    带高 {height - shown:F3} m    当前导出值：下沿 {LiteSim.CombatConfig.HeadHitLine:F3} m / 半径 {LiteSim.CombatConfig.HeadshotRadius:F3} m");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复位（回导出值）"))
                    SetHeadTune(-1f, -1f);
                if (GUILayout.Button("导出当前爆头区到 HeadBake.g.cs"))
                {
                    string report = LiteGame.EditorTools.HeadHitLineTuner.Export(shown / height, radiusShown);
                    EditorUtility.DisplayDialog("爆头区导出", report, "OK");
                }
            }
            EditorGUILayout.HelpBox(
                "对局内滑动即生效（判定与身位可视化的黄/红圈实时随动，可实弹试 Crit 档——黄红圈之间的窄柱切片才是爆头区）；"
                + "定型才导出。对局中点导出会触发重编译并中断对局（domain reload）——可先记下数值，出对局后再导。",
                MessageType.Info);
        }

        /// <summary>面板显示的当前爆头线下沿：有活动局取实例解析值，否则 TestModeRuntime 待用值（&lt; 0 回退导出值）。</summary>
        private static float CurrentHeadLineShown()
        {
            var match = LiteSim.SimTestRules.ActiveMatch;
            if (match != null) return LiteSim.MatchTestRules.ResolveHeadHitLine(match);
            return TestModeRuntime.HeadHitLineOverride >= 0f ? TestModeRuntime.HeadHitLineOverride : LiteSim.CombatConfig.HeadHitLine;
        }

        /// <summary>面板显示的当前爆头柱半径：取值口径同 <see cref="CurrentHeadLineShown"/>。</summary>
        private static float CurrentHeadRadiusShown()
        {
            var match = LiteSim.SimTestRules.ActiveMatch;
            if (match != null) return LiteSim.MatchTestRules.ResolveHeadshotRadius(match);
            return TestModeRuntime.HeadshotRadiusOverride >= 0f ? TestModeRuntime.HeadshotRadiusOverride : LiteSim.CombatConfig.HeadshotRadius;
        }

        /// <summary>爆头带覆写写通道：活动局实例（即时生效）+ TestModeRuntime 待用值（下次进房延续）双落。</summary>
        private static void SetHeadTune(float line, float radius)
        {
            TestModeRuntime.HeadHitLineOverride = line;
            TestModeRuntime.HeadshotRadiusOverride = radius;
            var match = LiteSim.SimTestRules.ActiveMatch;
            if (match != null)
            {
                match.HeadHitLineOverride = line;
                match.HeadshotRadiusOverride = radius;
            }
        }

        // ---- 持枪幅度实时调（表现沙箱件，纯观感）----

        /// <summary>持枪幅度实时调（运行时覆写，不落盘；换弹期武器骨跟随强度——约束每帧读 Live，滑杆一动观感即变）。</summary>
        private static void DrawGripTuneBlock()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("持枪幅度实时调（运行时覆写，不落盘）", EditorStyles.boldLabel);
            float shown = LiteView.Animation.GripTuning.TorsoWeightDevOverride >= 0f
                ? LiteView.Animation.GripTuning.TorsoWeightDevOverride
                : LiteView.Animation.GripBake.TorsoWeight;
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider("换弹期跟随强度（0=不约束 1=完全跟随）", shown, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                LiteView.Animation.GripTuning.TorsoWeightDevOverride = next;
            EditorGUILayout.LabelField($"当前烘焙值：{LiteView.Animation.GripBake.TorsoWeight:R}（重烘 = 烘焙工具箱「持枪偏移」页）");
            if (GUILayout.Button("复位（回烘焙值）"))
                LiteView.Animation.GripTuning.TorsoWeightDevOverride = -1f;
            EditorGUILayout.HelpBox(
                "对局内拖动即生效（换弹上半身叠加期的武器骨跟随强度实时随动）；release 无此面（覆写面随三宏剥离，恒烘焙值）。",
                MessageType.Info);
        }
    }
}
