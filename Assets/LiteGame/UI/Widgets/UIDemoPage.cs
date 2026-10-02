using Cysharp.Threading.Tasks;
using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 控件库 Demo 页（一页全展 + 最小断言）。
    /// 模板来自 `Assets/UI/Widgets/`（由 `LiteGame.Editor/WidgetPrefabBuilder` 确定性生成，25 件）；
    /// 模板件的结构/行为断言归 `WidgetPrefabCheck`——本页只保留
    /// **与模板无关**的两条（UIDataBinder 去重、绑定/命令式所有权互斥），并负责"一页全展"。
    /// 加载：编辑器用 AssetDatabase（dev 页快路径）；**真机走 YooAsset 运行时加载**（收集组 `LiteGameWidgets`）。
    /// 异步化走 UniTask（项目红线：禁原生协程）。
    /// </summary>
    public class UIDemoPage : MonoBehaviour
    {
        /// <summary>模板清单（与构建器产物一一对应）。</summary>
        private static readonly string[] Templates =
        {
            "StateButton", "Dialog", "Toast", "Bubble",
            "VirtualList", "SimpleList", "TabGroup", "BottomNav",
            "ProgressBar", "HpBar", "StarRating", "CountText", "Countdown", "AnimatedImage", "AvatarFrame",
            "InputField", "Slider", "Toggle", "Dropdown", "Stepper",
            "RedDot", "FlyText", "GuideHighlight", "EventRelay", "SafeArea",
        };

        private const string WidgetDir = "Assets/UI/Widgets";   // UI 控件模板目录（顶层 Assets/UI）

        /// <summary>
        /// 模板加载器（**装配方注入**——真机分支用）。
        ///
        /// **为什么是注入而不是直调 `AssetService`**（《客户端总设计》§5.1 逻辑边界）：
        /// 本页在通用 UI 层，直调静态资源门面会让 UI 层反向依赖 YooAsset 适配器，
        /// 拆 asmdef 时就是 `UI → Adapter` 的硬依赖。编辑器菜单装配时注入实现即可；
        /// 未注入时编辑器路径（AssetDatabase）照常工作，真机分支返回 null（本页是开发验收页，非产品入口）。
        /// </summary>
        public Func<string, System.Threading.CancellationToken, UniTask<GameObject>> LoadPrefab { get; set; }

        private int _pass, _fail;

        private void Awake() => BuildAsync().Forget();       // 模板加载是异步的（真机分支），断言排在构建之后

        private async UniTaskVoid BuildAsync()
        {
            await BuildFromTemplatesAsync();
            RunChecks();
        }

        // ---------------- 一页全展（模板实例化） ----------------

        private async UniTask BuildFromTemplatesAsync()
        {
            var content = CreateNode("Content", new Vector2(20f, -20f));
            content.sizeDelta = new Vector2(1280f, 2800f);

            const float colW = 420f;
            const float rowH = 330f;
            int col = 0, row = 0, missing = 0;

            foreach (var name in Templates)
            {
                var inst = await LoadTemplateAsync(name);
                if (inst == null) { missing++; continue; }
                var rt = (RectTransform)inst.transform;
                rt.SetParent(content, false);
                rt.anchoredPosition = new Vector2(col * colW, -row * rowH);
                if (++col >= 3) { col = 0; row++; }
            }

            Log($"模板实例化 {Templates.Length - missing}/{Templates.Length}"
                + (missing > 0 ? $"（缺 {missing} 件——先跑菜单 LiteGame/UI/构建控件模板 Prefabs）" : ""));

            DriveSampleState(content);
        }

        /// <summary>少量"看得见状态"的驱动（纯展示；断言在 WidgetPrefabCheck 里跑）。</summary>
        private void DriveSampleState(Transform content)
        {
            var hp = content.GetComponentInChildren<HpBar>(true);
            if (hp != null) hp.Set(65f, 100f);

            var bar = content.GetComponentInChildren<ProgressBar>(true);
            if (bar != null) bar.Set(0.45f);

            var stars = content.GetComponentInChildren<StarRating>(true);
            if (stars != null) stars.Set(4);

            var step = content.GetComponentInChildren<Stepper>(true);
            if (step != null) step.Set(3);

            var vl = content.GetComponentInChildren<VirtualList>(true);
            if (vl != null) vl.SetSource(new DemoSource(12));

            var dot = content.GetComponentInChildren<RedDot>(true);
            if (dot != null)
            {
                var tree = new RedDotTree();
                dot.Bind(tree, "mail");
                tree.Node("mail").SetCount(3);
            }

            var fly = content.GetComponentInChildren<FlyTextPool>(true);
            if (fly != null) fly.Show("飘字演示", new Vector2(0f, 40f));

            var cd = content.GetComponentInChildren<Countdown>(true);
            if (cd != null) cd.StartCountdown(60f);
        }

        /// <summary>模板加载：编辑器走 AssetDatabase（快路径）；真机走 YooAsset 运行时加载（收集组 LiteGameWidgets）。
        /// 编辑器下 AssetDatabase 未命中（例如资源刚生成未导入）时同样落到运行时路径——两条路都不通才返回 null。</summary>
        private async UniTask<GameObject> LoadTemplateAsync(string name)
        {
            string path = $"{WidgetDir}/{name}.prefab";
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null) return UnityEngine.Object.Instantiate(asset);
#endif
            if (LoadPrefab == null) return null;          // 未注入 = 真机分支不可用（开发页，非产品入口）
            try
            {
                var prefab = await LoadPrefab(path, default(System.Threading.CancellationToken));
                return prefab != null ? UnityEngine.Object.Instantiate(prefab) : null;
            }
            catch (Exception ex)
            {
                Log($"模板 {name} 运行时加载失败（核对收集组 LiteGameWidgets）：{ex.GetType().Name}:{ex.Message}");
                return null;
            }
        }

        /// <summary>同步加载模板（自检用；编辑器快路径）。真机无自检路径，返回 null。</summary>
        private static GameObject LoadTemplateSync(string name)
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{WidgetDir}/{name}.prefab");
            return asset != null ? UnityEngine.Object.Instantiate(asset) : null;
#else
            return null;
#endif
        }

        // ---------------- 断言（只留与模板无关者） ----------------

        private void RunChecks()
        {
            // StarRating 钳制 / Stepper 钳制 / RedDot 树传播 / Countdown 到点 等**模板相关**断言
            // 归 WidgetPrefabCheck（模板实例化路径）；此处只保留模板无关的两条。
            Check("UIDataBinder 去重", () =>
            {
                int calls = 0;
                using (var b = new UIDataBinder<int>(v => calls++))
                {
                    b.Set(1); b.Set(1); b.Set(2);
                }
                return calls == 2;
            });
            Check("所有权互斥：金币走绑定 + 命令式违例抛（§4.7 共存验收）", () =>
            {
                // 复用一个真实模板（CountText → Label）：视觉取模板，不在自检里拼装（§7）。
                GameObject inst = LoadTemplateSync("CountText");
                if (inst == null) return false;
                TMP_Text label = inst.GetComponentInChildren<TMP_Text>(true);
                if (label == null) { Destroy(inst); return false; }

                var index = new UIBindIndex(new Dictionary<string, Component> { ["GoldText"] = label });
                var binder = index.BindText<int>("GoldText", v => "金币 " + v);   // 绑定驱动
                binder.Set(100);
                bool boundWrite = label.text == "金币 100";

                bool violationCaught = false;
                try { index.SetText("GoldText", "命令式改写"); }
                catch (InvalidOperationException) { violationCaught = true; }
                bool textKept = label.text == "金币 100";

                Destroy(inst);
                return boundWrite && violationCaught && textKept;
            });
            LogSummary($"控件自检完成 PASS={_pass} FAIL={_fail}（模板件断言见 WidgetTemplateCheck）");
        }

        private sealed class DemoSource : IVirtualListSource
        {
            private readonly int _count;
            public DemoSource(int count) => _count = count;
            public int Count => _count;
            public void Bind(int index, Component item)
            {
                var t = item.GetComponentInChildren<TMP_Text>();
                if (t != null) t.text = $"条目 {index}";
            }
        }

        // ---------------- 构建辅助 ----------------

        private RectTransform CreateNode(string name, Vector2 pos, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent != null ? parent : transform, false);
            rt.anchoredPosition = pos;
            return rt;
        }

        private void Check(string name, Func<bool> assertion)
        {
            bool ok;
            try { ok = assertion(); }
            catch (Exception ex)
            {
                Log($"FAIL {name}（异常 {ex.GetType().Name}:{ex.Message}）");
                _fail++;
                return;
            }
            if (ok) { _pass++; Log($"PASS {name}"); }
            else { _fail++; Log($"FAIL {name}"); }
        }

        // 静态日志出口：真机加载分支（LoadTemplateAsync）是静态方法——Log 必须为静态。
        private static void LogSummary(string message) => LiteFramework.Log.Info(message, "WidgetCheck");
        private static void Log(string message) => LiteFramework.Log.Info(message, "WidgetCheck");
    }
}
