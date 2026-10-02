using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using LiteGame.UI;
using TMPro;

namespace LiteGame.Editor
{
    /// <summary>
    /// 控件模板 prefab 构建器（确定性生成 + 可重复执行）。
    /// 约定：
    ///  · 根 = 控件组件本体；内部接线**全部落在序列化引用**上（Fill/Template/MinMax 等已接好）
    ///  · 结构 Root → Bg / Content / Interaction 三层缺省
    ///  · **模板不预设 BindName**：多个实例同名会炸界面级索引（BindIndexBuilder 重名即抛），
    ///    命名归界面作者——模板根带一个空名 BindNode 作为"可暴露位"提示
    ///  · 模板不需要 BindRoot：其为界面级生成物，且默认类名会与运行时控件类同名冲突
    /// </summary>
    public static class WidgetPrefabBuilder
    {
        private const string OutDir = "Assets/UI/Widgets";   // UI 控件模板目录（顶层 Assets/UI）

        [MenuItem("LiteGame/UI/构建控件模板 Prefabs")]
        private static void BuildAll()
        {
            EnsureFolder(OutDir);
            var done = new List<string>();
            done.Add(Save(BuildStateButton(), "StateButton"));
            done.Add(Save(BuildDialog(), "Dialog"));
            done.Add(Save(BuildVirtualList(), "VirtualList"));
            done.Add(Save(BuildHpBar(), "HpBar"));
            done.Add(Save(BuildToast(), "Toast"));
            done.Add(Save(BuildBubble(), "Bubble"));
            done.Add(Save(BuildFlyText(), "FlyText"));
            done.Add(Save(BuildRedDot(), "RedDot"));
            done.Add(Save(BuildTabGroup(), "TabGroup"));
            done.Add(Save(BuildBottomNav(), "BottomNav"));
            done.Add(Save(BuildProgressBar(), "ProgressBar"));
            done.Add(Save(BuildStarRating(), "StarRating"));
            done.Add(Save(BuildCountText(), "CountText"));
            done.Add(Save(BuildCountdown(), "Countdown"));
            done.Add(Save(BuildAnimatedImage(), "AnimatedImage"));
            done.Add(Save(BuildAvatarFrame(), "AvatarFrame"));
            done.Add(Save(BuildStepper(), "Stepper"));
            done.Add(Save(BuildInputField(), "InputField"));
            done.Add(Save(BuildSlider(), "Slider"));
            done.Add(Save(BuildToggle(), "Toggle"));
            done.Add(Save(BuildDropdown(), "Dropdown"));
            done.Add(Save(BuildEventRelay(), "EventRelay"));
            done.Add(Save(BuildGuideHighlight(), "GuideHighlight"));
            done.Add(Save(BuildSafeArea(), "SafeArea"));
            done.Add(Save(BuildSimpleList(), "SimpleList"));
            // 反馈面
            done.Add(Save(BuildLoading(), "Loading"));
            done.Add(Save(BuildBaseline(), "Baseline"));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            LiteFramework.Log.Info("[WidgetPrefab] 模板构建完成：" + string.Join(" / ", done), "UI");
        }

        // ================= 余件 =================

        /// <summary>轻提示：根挂 Toast，子放非激活模板（Show 时实例化到同父）。
        /// 根为**透明容器**（无 Image，否则多条叠底），
        /// 纵向排布由 Toast.Spacing/StackDirection 定义，条数上限 MaxVisible 超出淘汰最旧。</summary>
        private static GameObject BuildToast()
        {
            var root = Node("Toast", null, new Vector2(360f, 48f));
            var toast = root.AddComponent<Toast>();
            toast.Duration = 2f;
            toast.MaxVisible = 4;
            toast.Spacing = 8f;
            toast.StackDirection = 1;   // 沿 +Y 向上生长（Spread 语义：旧条在下，新条依次上移）

            var tpl = Node("_Template", root, new Vector2(320f, 40f));
            var tplBg = tpl.AddComponent<Image>();
            tplBg.color = new Color(0.1f, 0.1f, 0.12f, 0.92f);
            var tplText = Text("Label", tpl, "轻提示");
            Stretch(tplText.rectTransform, 8f);
            tpl.SetActive(false);
            toast.Template = (RectTransform)tpl.transform;

            Expose(root, "界面级命名：如 ToastHost");
            return root;
        }

        /// <summary>加载遮罩控件：全屏阻断面 + 居中文案。
        /// 阻断面 raycastTarget=true 吃掉射线（§6.2"输入禁用/射线阻断/焦点是不同职责"——
        /// 阻断靠阻断面，不靠停组，故可与 Toast 同层并存不互斥）。</summary>
        private static GameObject BuildLoading()
        {
            var root = Node("Loading", null, new Vector2(1920f, 1080f));
            Stretch(root.GetComponent<RectTransform>(), 0f);

            var mask = root.AddComponent<LoadingMask>();

            var blocker = Node("Blocker", root, new Vector2(1920f, 1080f));
            Stretch(blocker.GetComponent<RectTransform>(), 0f);
            var bg = blocker.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;                       // 阻断面吃掉射线
            mask.Blocker = bg;

            var message = Text("Message", root, "加载中…");
            message.fontSize = 28f;
            var rt = message.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 80f);
            rt.anchoredPosition = Vector2.zero;
            mask.Message = message;

            Expose(root, "界面级命名：如 LoadingRoot");
            return root;
        }

        /// <summary>基准页控件：纯全屏底 + 文案，无业务逻辑。
        /// 用途是给"页面 A / 页面 B / 弹窗"这类**层级与状态语义**（覆盖、暂停、导航）提供
        /// 最小真实页面——它没有业务内容，不是业务页模板。</summary>
        private static GameObject BuildBaseline()
        {
            var root = Node("Baseline", null, new Vector2(1920f, 1080f));
            Stretch(root.GetComponent<RectTransform>(), 0f);

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.08f, 0.92f);
            bg.raycastTarget = false;                      // 纯底，不挡射线

            var label = Text("Label", root, "基准页");
            label.fontSize = 24f;
            Stretch(label.rectTransform, 40f);

            Expose(root, "界面级命名：如 PageA");
            return root;
        }

        // ================= 反馈面页面级包装 =================
        // Widget 模板无 Canvas，不能当 form 打开——反馈面要经 UIService 打开，必须有页面级包装：
        // Canvas + CanvasGroup + 嵌套控件实例。

        private const string ScreenDir = "Assets/UI/Screens";

        /// <summary>构建反馈面页面级包装（Loading/Toast）。错误弹窗复用既有 Screens/Dialog.prefab。
        /// 幂等：重复执行覆盖同名文件。</summary>
        [MenuItem("LiteGame/UI/构建反馈面 Screens")]
        private static void BuildFeedbackScreens()
        {
            EnsureFolder(ScreenDir);
            var done = new List<string>();
            done.Add(SaveScreen(BuildLoadingScreen(), "Loading"));
            done.Add(SaveScreen(BuildToastScreen(), "Toast"));
            Debug.Log($"[WidgetPrefab] 反馈面页面构建完成:{string.Join(",", done)}");
        }

        /// <summary>Loading 页面级包装：Canvas + CanvasGroup + 嵌套 Loading 控件。</summary>
        private static GameObject BuildLoadingScreen()
        {
            var root = ScreenNode("Loading");
            NestWidget(root, "Loading");
            return root;
        }

        /// <summary>基准页 A/B：层级与状态语义（覆盖/暂停/导航）的最小真实页面。
        /// 两件结构相同、各自独立——用于验证"另一页打开"这类跨页语义（同页无法验证覆盖）。</summary>
        [MenuItem("LiteGame/UI/构建基准页 Screens")]
        private static void BuildBaselineScreens()
        {
            EnsureFolder(ScreenDir);
            var done = new List<string>();
            done.Add(SaveScreen(BuildBaselineScreen("BaselineA"), "BaselineA"));
            done.Add(SaveScreen(BuildBaselineScreen("BaselineB"), "BaselineB"));
            Debug.Log($"[WidgetPrefab] 基准页构建完成:{string.Join(",", done)}");
        }

        private static GameObject BuildBaselineScreen(string name)
        {
            var root = ScreenNode(name);
            NestWidget(root, "Baseline");
            return root;
        }

        /// <summary>列表页：页面根 + 嵌套 VirtualList 模板。
        /// 「可扩展」接入示例的载体——新增页面只动构建器与目录条目，框架零改动（§8.2 列表段 PlayMode 用例消费）。</summary>
        [MenuItem("LiteGame/UI/构建列表页 Screens")]
        private static void BuildListScreens()
        {
            EnsureFolder(ScreenDir);
            var done = new List<string>();
            done.Add(SaveScreen(BuildListScreen(), "ListScreen"));
            Debug.Log($"[WidgetPrefab] 列表页构建完成:{string.Join(",", done)}");
        }

        private static GameObject BuildListScreen()
        {
            var root = ScreenNode("ListScreen");
            NestWidget(root, "VirtualList");
            return root;
        }

        /// <summary>Toast 页面级包装：Canvas + CanvasGroup + 嵌套 Toast 控件（容器定位在下方居中）。</summary>
        private static GameObject BuildToastScreen()
        {
            var root = ScreenNode("Toast");
            GameObject widget = NestWidget(root, "Toast");
            // 容器下移，给向上堆叠留出空间（新条沿 +Y 生长，不会被顶部裁掉）
            ((RectTransform)widget.transform).anchoredPosition = new Vector2(0f, -320f);
            return root;
        }

        /// <summary>页面级根：Canvas(Overlay) + CanvasGroup（UIService 的 UIForm 要求两者存在）。</summary>
        private static GameObject ScreenNode(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;   // 层内排序由 UIForm/统一排序器接管
            return root;
        }

        /// <summary>把 Widgets 下已生成的控件模板作为嵌套 prefab 实例挂进页面（保留 Prefab 引用关系）。</summary>
        private static GameObject NestWidget(GameObject parent, string widgetName)
        {
            var widget = AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/{widgetName}.prefab");
            if (widget == null)
                throw new InvalidOperationException($"控件模板缺失:{widgetName}.prefab（先跑“构建控件模板 Prefabs”）");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(widget);
            instance.transform.SetParent(parent.transform, false);
            return instance;
        }

        private static string SaveScreen(GameObject root, string name)
        {
            var path = $"{ScreenDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return name + (prefab != null ? "✓" : "✗");
        }

        /// <summary>气泡：挂点旁短命提示（朝上小三角由美术补）。</summary>
        private static GameObject BuildBubble()
        {
            var root = Node("Bubble", null, new Vector2(220f, 56f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
            var bubble = root.AddComponent<UIBubble>();

            var label = Text("Label", root, "气泡文本");
            Stretch(label.rectTransform, 8f);
            bubble.Label = label;

            Expose(root, "界面级命名：如 EquipBubble");
            return root;
        }

        /// <summary>飘字池：根挂池，子放非激活项模板。</summary>
        private static GameObject BuildFlyText()
        {
            var root = Node("FlyText", null, new Vector2(240f, 40f));
            var pool = root.AddComponent<FlyTextPool>();
            pool.RiseDistance = 80f;
            pool.Duration = 0.8f;
            pool.PoolDepth = 16;

            var tpl = Node("_Template", root, new Vector2(240f, 36f));
            var tplText = tpl.AddComponent<TextMeshProUGUI>();
            tplText.text = "飘字";
            tplText.color = new Color(1f, 0.9f, 0.4f);
            tplText.alignment = TextAlignmentOptions.Center;
            tplText.fontSize = 20f;
            tplText.raycastTarget = false;
            tpl.SetActive(false);
            pool.Template = (RectTransform)tpl.transform;

            Expose(root, "界面级命名：如 DamageFlyText");
            return root;
        }

        /// <summary>红点：Dot 子节点由红点树计数驱动显隐。</summary>
        private static GameObject BuildRedDot()
        {
            var root = Node("RedDot", null, new Vector2(28f, 28f));
            var dot = root.AddComponent<RedDot>();

            var img = Node("Dot", root, new Vector2(18f, 18f));
            var imgComp = img.AddComponent<Image>();
            imgComp.color = new Color(0.9f, 0.2f, 0.2f);
            imgComp.raycastTarget = false;
            dot.Dot = img;

            Expose(root, "界面级命名：如 MailRedDot");
            return root;
        }

        /// <summary>页签组：三个页签按钮（Tabs 列表已接）。</summary>
        private static GameObject BuildTabGroup()
        {
            var root = Node("TabGroup", null, new Vector2(420f, 56f));
            var group = root.AddComponent<TabGroup>();
            group.NormalColor = Color.white;
            group.SelectedColor = new Color(0.9f, 0.7f, 0.2f);

            var tabs = new List<Button>(3);
            for (int i = 0; i < 3; i++)
            {
                var tabGo = Node($"Tab{i}", root, new Vector2(130f, 48f));
                Place(tabGo, new Vector2((i - 1) * 140f, 0f));
                var tabBg = tabGo.AddComponent<Image>();
                tabBg.color = Color.white;
                var btn = tabGo.AddComponent<Button>();
                btn.targetGraphic = tabBg;
                var label = Text("Label", tabGo, $"页签{i + 1}");
                Stretch(label.rectTransform, 6f);
                tabs.Add(btn);
            }
            group.Tabs = tabs;

            Expose(root, "界面级命名：如 MainTabs");
            return root;
        }

        /// <summary>底部导航：内含页签组（尺寸更大、贴底）。</summary>
        private static GameObject BuildBottomNav()
        {
            var root = Node("BottomNav", null, new Vector2(720f, 88f));
            var nav = root.AddComponent<BottomNav>();

            var tabsGo = Node("_Tabs", root, new Vector2(680f, 72f));
            var group = tabsGo.AddComponent<TabGroup>();
            group.NormalColor = new Color(0.75f, 0.75f, 0.78f);
            group.SelectedColor = new Color(0.95f, 0.8f, 0.3f);
            var tabs = new List<Button>(4);
            for (int i = 0; i < 4; i++)
            {
                var item = Node($"Nav{i}", tabsGo, new Vector2(150f, 64f));
                Place(item, new Vector2((i - 1.5f) * 160f, 0f));
                var itemBg = item.AddComponent<Image>();
                itemBg.color = new Color(0.12f, 0.12f, 0.16f);
                var btn = item.AddComponent<Button>();
                btn.targetGraphic = itemBg;
                var label = Text("Label", item, $"入口{i + 1}");
                Stretch(label.rectTransform, 6f);
                tabs.Add(btn);
            }
            group.Tabs = tabs;
            nav.Tabs = group;

            Expose(root, "界面级命名：如 MainNav");
            return root;
        }

        /// <summary>进度条：Fill 是 Filled Image（直线/环形由精灵决定）。</summary>
        private static GameObject BuildProgressBar()
        {
            var root = Node("ProgressBar", null, new Vector2(240f, 20f));
            var bar = root.AddComponent<ProgressBar>();
            bar.ValueFormat = "{0}/{1}";

            var bg = Node("_Bg", root, Vector2.zero);
            Stretch(bg.GetComponent<RectTransform>(), 0f);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);

            var fill = Node("Fill", root, Vector2.zero);
            Stretch(fill.GetComponent<RectTransform>(), 0f);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.75f, 0.95f);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 0.7f;

            var value = Text("ValueText", root, "70/100");
            Stretch(value.rectTransform, 2f);
            value.fontSize = 14f;

            bar.Fill = fillImg;
            bar.ValueText = value;

            Expose(root, "界面级命名：如 ExpBar");
            return root;
        }

        /// <summary>星级评分：固定 5 星（数量由模板定，不改运行时逻辑）。</summary>
        private static GameObject BuildStarRating()
        {
            var root = Node("StarRating", null, new Vector2(300f, 52f));
            var rating = root.AddComponent<StarRating>();

            var stars = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                var star = Node($"Star{i}", root, new Vector2(48f, 48f));
                Place(star, new Vector2((i - 2) * 56f, 0f));
                var img = star.AddComponent<Image>();
                img.color = new Color(0.85f, 0.7f, 0.25f);
                stars[i] = img;
            }
            rating.Stars = stars;

            Expose(root, "界面级命名：如 LevelStars");
            return root;
        }

        /// <summary>数值滚动文本：文本挂根，Label 指向自身。</summary>
        private static GameObject BuildCountText()
        {
            var root = Node("CountText", null, new Vector2(200f, 40f));
            var label = root.AddComponent<TextMeshProUGUI>();
            label.text = "0";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 24f;
            label.raycastTarget = false;
            var counter = root.AddComponent<CountText>();
            counter.Label = label;
            counter.Duration = 0.5f;

            Expose(root, "界面级命名：如 GoldCount");
            return root;
        }

        /// <summary>倒计时文本：文本挂根，到点触发 OnDone。</summary>
        private static GameObject BuildCountdown()
        {
            var root = Node("Countdown", null, new Vector2(200f, 40f));
            var label = root.AddComponent<TextMeshProUGUI>();
            label.text = "00:00";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 24f;
            label.raycastTarget = false;
            var cd = root.AddComponent<Countdown>();
            cd.Label = label;

            Expose(root, "界面级命名：如 RoundCountdown");
            return root;
        }

        /// <summary>帧序列图（灰盒无帧数据；美术接入时填 Frames）。</summary>
        private static GameObject BuildAnimatedImage()
        {
            var root = Node("AnimatedImage", null, new Vector2(120f, 120f));
            root.AddComponent<Image>().color = new Color(0.4f, 0.4f, 0.45f);
            var anim = root.AddComponent<AnimatedImage>();
            anim.Fps = 10f;
            anim.Loop = true;
            anim.PlayOnEnable = true;

            Expose(root, "界面级命名：如 LoadingAnim");
            return root;
        }

        /// <summary>头像框：头像 + 边框 + 等级角标。</summary>
        private static GameObject BuildAvatarFrame()
        {
            var root = Node("AvatarFrame", null, new Vector2(104f, 104f));
            var frame = root.AddComponent<AvatarFrame>();

            var avatar = Node("Avatar", root, new Vector2(88f, 88f));
            var avatarImg = avatar.AddComponent<Image>();
            avatarImg.color = new Color(0.3f, 0.35f, 0.45f);

            var border = Node("Frame", root, new Vector2(100f, 100f));
            var borderImg = border.AddComponent<Image>();
            borderImg.color = new Color(0.85f, 0.7f, 0.25f, 0.9f);

            var badge = Text("LevelBadge", root, "1");
            Place(badge.gameObject, new Vector2(38f, -38f));
            badge.rectTransform.sizeDelta = new Vector2(30f, 22f);
            badge.fontSize = 14f;

            frame.Avatar = avatarImg;
            frame.Frame = borderImg;
            frame.LevelBadge = badge;

            Expose(root, "界面级命名：如 PlayerAvatar");
            return root;
        }

        /// <summary>步进器：− / 值 / ＋。</summary>
        private static GameObject BuildStepper()
        {
            var root = Node("Stepper", null, new Vector2(300f, 52f));
            var stepper = root.AddComponent<Stepper>();
            stepper.Min = 0;
            stepper.Max = 10;
            stepper.Step = 1;

            var minus = Node("Minus", root, new Vector2(64f, 48f));
            Place(minus, new Vector2(-118f, 0f));
            var minusBg = minus.AddComponent<Image>();
            minusBg.color = new Color(0.3f, 0.3f, 0.34f);
            var minusBtn = minus.AddComponent<Button>();
            minusBtn.targetGraphic = minusBg;
            var minusText = Text("Label", minus, "−");
            Stretch(minusText.rectTransform, 4f);

            var value = Text("Value", root, "0");
            value.rectTransform.sizeDelta = new Vector2(120f, 48f);
            value.fontSize = 22f;

            var plus = Node("Plus", root, new Vector2(64f, 48f));
            Place(plus, new Vector2(118f, 0f));
            var plusBg = plus.AddComponent<Image>();
            plusBg.color = new Color(0.3f, 0.3f, 0.34f);
            var plusBtn = plus.AddComponent<Button>();
            plusBtn.targetGraphic = plusBg;
            var plusText = Text("Label", plus, "＋");
            Stretch(plusText.rectTransform, 4f);

            stepper.Minus = minusBtn;
            stepper.Plus = plusBtn;
            stepper.ValueLabel = value;

            Expose(root, "界面级命名：如 VolumeStepper");
            return root;
        }

        /// <summary>输入框封装：背景 + InputField（文本/占位已接）。</summary>
        private static GameObject BuildInputField()
        {
            var root = Node("InputField", null, new Vector2(320f, 52f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.13f, 0.9f);
            var field = root.AddComponent<TMP_InputField>();

            var text = Text("_Text", root, "");
            text.alignment = TextAlignmentOptions.Left;
            Stretch(text.rectTransform, 12f);

            var placeholder = Text("_Placeholder", root, "请输入…");
            placeholder.alignment = TextAlignmentOptions.Left;
            placeholder.color = new Color(0.6f, 0.6f, 0.65f);
            Stretch(placeholder.rectTransform, 12f);

            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = bg;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.textViewport = (RectTransform)text.transform.parent;

            var wrap = root.AddComponent<UIInputFieldWrap>();
            wrap.Field = field;

            Expose(root, "界面级命名：如 RoomCodeInput");
            return root;
        }

        /// <summary>滑条封装：背景 / 填充 / 手柄（fillRect/handleRect 已接）。</summary>
        private static GameObject BuildSlider()
        {
            var root = Node("Slider", null, new Vector2(260f, 28f));
            var slider = root.AddComponent<Slider>();

            var bg = Node("_Background", root, Vector2.zero);
            Stretch(bg.GetComponent<RectTransform>(), 0f);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.12f, 0.12f, 0.15f);

            var fillArea = Node("_FillArea", root, Vector2.zero);
            Stretch(fillArea.GetComponent<RectTransform>(), 4f);
            var fill = Node("Fill", fillArea, Vector2.zero);
            Stretch(fill.GetComponent<RectTransform>(), 0f);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.7f, 0.95f);

            var handleArea = Node("_HandleArea", root, Vector2.zero);
            Stretch(handleArea.GetComponent<RectTransform>(), 4f);
            var handle = Node("Handle", handleArea, new Vector2(22f, 22f));
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.7f;

            var wrap = root.AddComponent<UISliderWrap>();
            wrap.Slider = slider;

            Expose(root, "界面级命名：如 VolumeSlider");
            return root;
        }

        /// <summary>开关封装：背景 / 勾 / 文本。</summary>
        private static GameObject BuildToggle()
        {
            var root = Node("Toggle", null, new Vector2(160f, 40f));
            var toggle = root.AddComponent<Toggle>();

            var bg = Node("_Background", root, new Vector2(32f, 32f));
            Place(bg, new Vector2(-58f, 0f));
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.2f);

            var check = Node("Checkmark", bg, new Vector2(20f, 20f));
            var checkImg = check.AddComponent<Image>();
            checkImg.color = new Color(0.35f, 0.8f, 0.45f);

            var label = Text("Label", root, "开关");
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(40f, 0f);
            label.rectTransform.offsetMax = Vector2.zero;

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = true;

            var wrap = root.AddComponent<UIToggleWrap>();
            wrap.Toggle = toggle;

            Expose(root, "界面级命名：如 AutoReadyToggle");
            return root;
        }

        /// <summary>下拉封装：标题 + 非激活模板（标题/项文本已接）。</summary>
        private static GameObject BuildDropdown()
        {
            var root = Node("Dropdown", null, new Vector2(260f, 48f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.16f);
            var dropdown = root.AddComponent<TMP_Dropdown>();

            var caption = Text("_Caption", root, "选项一");
            caption.alignment = TextAlignmentOptions.Left;
            Stretch(caption.rectTransform, 12f);

            var tpl = Node("_Template", root, new Vector2(240f, 160f));
            var tplBg = tpl.AddComponent<Image>();
            tplBg.color = new Color(0.1f, 0.1f, 0.13f, 0.98f);
            var viewport = Node("_Viewport", tpl, Vector2.zero);
            Stretch(viewport.GetComponent<RectTransform>(), 0f);
            viewport.AddComponent<RectMask2D>();
            var content = Node("_Content", viewport, new Vector2(240f, 40f));
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 40f);
            var item = Node("_Item", content, new Vector2(240f, 40f));
            var itemToggle = item.AddComponent<Toggle>();
            var itemBg = item.AddComponent<Image>();
            itemBg.color = new Color(0.16f, 0.16f, 0.2f);
            itemToggle.targetGraphic = itemBg;
            var itemLabel = Text("_ItemLabel", item, "选项");
            itemLabel.alignment = TextAlignmentOptions.Left;
            Stretch(itemLabel.rectTransform, 10f);
            tpl.SetActive(false);

            dropdown.targetGraphic = bg;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.template = (RectTransform)tpl.transform;

            var wrap = root.AddComponent<UIDropdownWrap>();
            wrap.Dropdown = dropdown;

            Expose(root, "界面级命名：如 RegionDropdown");
            return root;
        }

        /// <summary>事件中继：透明接受点击区域（OnClicked 由界面接线）。</summary>
        private static GameObject BuildEventRelay()
        {
            var root = Node("EventRelay", null, new Vector2(120f, 120f));
            var img = root.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.01f);   // 近似透明但可接收射线
            root.AddComponent<UIEventRelay>();

            Expose(root, "界面级命名：如 SceneTouchArea");
            return root;
        }

        /// <summary>引导高亮框：Frame 子节点对齐目标控件。</summary>
        private static GameObject BuildGuideHighlight()
        {
            var root = Node("GuideHighlight", null, new Vector2(140f, 140f));
            root.AddComponent<GuideHighlight>();

            var frame = Node("Frame", root, new Vector2(120f, 120f));
            var frameImg = frame.AddComponent<Image>();
            frameImg.color = new Color(1f, 0.85f, 0.3f, 0.25f);
            frameImg.raycastTarget = false;

            root.GetComponent<GuideHighlight>().Frame = (RectTransform)frame.transform;

            Expose(root, "界面级命名：如 StepGuide");
            return root;
        }

        /// <summary>刘海屏安全区接收器：根铺满（锚点由 receiver 按 safeArea 收紧）。</summary>
        private static GameObject BuildSafeArea()
        {
            var root = Node("SafeArea", null, new Vector2(1080f, 1920f));
            Stretch(root.GetComponent<RectTransform>(), 0f);
            root.AddComponent<SafeAreaReceiver>();

            Expose(root, "界面级命名：如 SafeRoot");
            return root;
        }

        /// <summary>简易列表（非虚拟化，数据源 C# 直实现）。</summary>
        private static GameObject BuildSimpleList()
        {
            var root = Node("SimpleList", null, new Vector2(360f, 320f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);
            var list = root.AddComponent<SimpleListView>();
            list.Spacing = 8;

            var item = Node("_ItemTemplate", root, new Vector2(340f, 44f));
            var itemRt = (RectTransform)item.transform;
            itemRt.anchorMin = new Vector2(0f, 1f);
            itemRt.anchorMax = new Vector2(1f, 1f);
            itemRt.pivot = new Vector2(0.5f, 1f);
            itemRt.anchoredPosition = Vector2.zero;
            itemRt.sizeDelta = new Vector2(0f, 44f);
            var itemBg = item.AddComponent<Image>();
            itemBg.color = new Color(0.2f, 0.3f, 0.45f);
            var itemText = Text("Label", item, "条目");
            Stretch(itemText.rectTransform, 8f);
            item.SetActive(false);
            list.Template = itemRt;

            Expose(root, "界面级命名：如 FriendList");
            return root;
        }

        // ---------------- 模板：多态按钮 ----------------
        private static GameObject BuildStateButton()
        {
            var root = Node("StateButton", null, new Vector2(240f, 60f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.45f, 0.75f);
            var btn = root.AddComponent<StateButton>();
            btn.NormalColor = bg.color;

            var label = Text("Label", root, "按钮");
            Stretch(label.rectTransform, 8f);

            Expose(root, "界面级命名：如 BtnStart");
            return root;
        }

        // ---------------- 模板：对话框 ----------------
        private static GameObject BuildDialog()
        {
            var root = Node("Dialog", null, new Vector2(480f, 280f));
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.14f, 0.96f);
            var dlg = root.AddComponent<UIDialog>();

            var title = Text("TitleText", root, "标题");
            Anchor(title.rectTransform, new Vector2(0f, 104f), new Vector2(420f, 44f));
            title.fontStyle = FontStyles.Bold;

            var msg = Text("MessageText", root, "内容文本");
            Anchor(msg.rectTransform, new Vector2(0f, 10f), new Vector2(420f, 96f));

            var okGo = Node("OkButton", root, new Vector2(160f, 52f));
            Place(okGo, new Vector2(110f, -96f));
            var okBg = okGo.AddComponent<Image>();
            okBg.color = new Color(0.25f, 0.45f, 0.75f);
            var okBtn = okGo.AddComponent<Button>();
            okBtn.targetGraphic = okBg;
            var okText = Text("Label", okGo, "确定");
            Stretch(okText.rectTransform, 6f);

            var cancelGo = Node("CancelButton", root, new Vector2(160f, 52f));
            Place(cancelGo, new Vector2(-110f, -96f));
            var cancelBg = cancelGo.AddComponent<Image>();
            cancelBg.color = new Color(0.3f, 0.3f, 0.34f);
            var cancelBtn = cancelGo.AddComponent<Button>();
            cancelBtn.targetGraphic = cancelBg;
            var cancelText = Text("Label", cancelGo, "取消");
            Stretch(cancelText.rectTransform, 6f);

            dlg.Title = title;
            dlg.Message = msg;
            dlg.OkButton = okBtn;
            dlg.CancelButton = cancelBtn;

            Expose(root, "界面级命名：如 PopupConfirm");
            return root;
        }

        // ---------------- 模板：虚拟列表（ScrollRect + Content + 非激活项模板）----------------
        private static GameObject BuildVirtualList()
        {
            var root = Node("VirtualList", null, new Vector2(360f, 400f));
            var rootBg = root.AddComponent<Image>();
            rootBg.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewport = Node("Viewport", root, Vector2.zero);
            Stretch(viewport.GetComponent<RectTransform>(), 0f);
            viewport.AddComponent<RectMask2D>();

            var content = Node("Content", viewport, Vector2.zero);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            var list = content.AddComponent<VirtualList>();

            var item = Node("_ItemTemplate", content, new Vector2(340f, 48f));
            var itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 1f);
            itemRt.anchorMax = new Vector2(1f, 1f);
            itemRt.pivot = new Vector2(0.5f, 1f);
            itemRt.anchoredPosition = Vector2.zero;
            itemRt.sizeDelta = new Vector2(0f, 48f);
            var itemBg = item.AddComponent<Image>();
            itemBg.color = new Color(0.2f, 0.3f, 0.45f);
            var itemText = Text("Label", item, "条目");
            Stretch(itemText.rectTransform, 8f);
            item.SetActive(false);

            list.Template = itemRt;
            list.Direction = VirtualList.Axis.Vertical;
            list.Spacing = 8f;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRt;

            Expose(root, "界面级命名：如 RoomList");
            return root;
        }

        // ---------------- 模板：血条（后条延迟滑落）----------------
        private static GameObject BuildHpBar()
        {
            var root = Node("HpBar", null, new Vector2(240f, 20f));
            var hp = root.AddComponent<HpBar>();

            var back = Node("_Bg", root, Vector2.zero);
            Stretch(back.GetComponent<RectTransform>(), 0f);
            var backBg = back.AddComponent<Image>();
            backBg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);

            var backFillGo = Node("BackFill", root, Vector2.zero);
            Stretch(backFillGo.GetComponent<RectTransform>(), 0f);
            var backFill = backFillGo.AddComponent<Image>();
            backFill.color = new Color(0.85f, 0.35f, 0.25f);
            backFill.type = Image.Type.Filled;
            backFill.fillMethod = Image.FillMethod.Horizontal;
            backFill.fillAmount = 1f;

            var frontFillGo = Node("FrontFill", root, Vector2.zero);
            Stretch(frontFillGo.GetComponent<RectTransform>(), 0f);
            var frontFill = frontFillGo.AddComponent<Image>();
            frontFill.color = new Color(0.4f, 0.85f, 0.4f);
            frontFill.type = Image.Type.Filled;
            frontFill.fillMethod = Image.FillMethod.Horizontal;
            frontFill.fillAmount = 1f;

            var value = Text("ValueText", root, "100/100");
            Stretch(value.rectTransform, 2f);
            value.alignment = TextAlignmentOptions.Center;
            value.fontSize = 14f;

            hp.BackFill = backFill;
            hp.FrontFill = frontFill;
            hp.ValueText = value;

            Expose(root, "界面级命名：如 PlayerHp");
            return root;
        }

        // ---------------- 基础件 ----------------

        private static GameObject Node(string name, GameObject parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return go;
        }

        private static TMP_Text Text(string name, GameObject parent, string content)
        {
            var go = Node(name, parent, new Vector2(200f, 40f));
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.color = new Color(0.92f, 0.92f, 0.94f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = 20f;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt, float padding)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        private static void Anchor(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Place(GameObject go, Vector2 pos)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
        }

        /// <summary>模板根的空名 BindNode：作为"可暴露位"提示（命名归界面作者，避免多实例重名）。</summary>
        private static void Expose(GameObject root, string comment)
        {
            var node = root.AddComponent<LiteCodeGen.BindNode>();
            node.Comment = comment;
        }

        private static Font UIFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { }
            }
            return f;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string Save(GameObject root, string name)
        {
            var path = $"{OutDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return name + (prefab != null ? "✓" : "✗");
        }
    }

    /// <summary>控件模板自检：模板 prefab 实例化 + 驱动 + 断言。
    /// 自动化清单口径同《测试开发方案》：可代码驱动的行为面才断言（指针交互动画不在范围）。</summary>
    public static class WidgetPrefabCheck
    {
        private const string Dir = "Assets/UI/Widgets";    // UI 控件模板目录（顶层 Assets/UI）
        private const string Tag = "WidgetTemplateCheck";

        [MenuItem("LiteGame/UI/校验控件模板")]
        private static void Run()
        {
            var (pass, fail) = RunAll();
            Debug.Log($"[{Tag}] 模板自检完成 PASS={pass} FAIL={fail}");
        }

        /// <summary>
        /// 自检主体（菜单与 EditMode 用例共用）：返回 通过数 / 失败数。
        /// 门禁经 `Assets/Tests/EditMode` 的用例调用它——**不重复实现**，菜单与 CI 同源。
        /// </summary>
        public static (int pass, int fail) RunAll()
        {
            int pass = 0, fail = 0;
            pass += Check("StateButton：组件+底图+可交互默认", () =>
            {
                var go = Instantiate("StateButton");
                var ok = go.GetComponent<LiteGame.UI.StateButton>() != null
                         && go.GetComponent<UnityEngine.UI.Image>() != null
                         && go.GetComponent<LiteGame.UI.StateButton>().Interactable;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Dialog：四接线 + Ok 回调一次性", () =>
            {
                var go = Instantiate("Dialog");
                var dlg = go.GetComponent<LiteGame.UI.UIDialog>();
                if (dlg == null) { Kill(go); return false; }
                int fired = 0;
                dlg.Configure("标题", "内容", () => fired++);
                var wired = dlg.Title != null && dlg.Message != null && dlg.OkButton != null && dlg.CancelButton != null;
                dlg.OkButton.onClick.Invoke();
                dlg.OkButton.onClick.Invoke();                 // 一次性：第二次不应再触发
                var once = fired == 1;
                Kill(go);
                return wired && once;
            }, ref fail);

            pass += Check("VirtualList：Template 接线 + SetSource 落地计数", () =>
            {
                var go = Instantiate("VirtualList");
                var list = go.GetComponentInChildren<LiteGame.UI.VirtualList>(true);
                if (list == null || list.Template == null) { Kill(go); return false; }
                list.SetSource(new CountSource(7));
                var ok = list.RealizedCount == 7;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("HpBar：三接线 + Set 驱动 fillAmount", () =>
            {
                var go = Instantiate("HpBar");
                var hp = go.GetComponent<LiteGame.UI.HpBar>();
                if (hp == null || hp.FrontFill == null || hp.BackFill == null || hp.ValueText == null) { Kill(go); return false; }
                hp.Set(50f, 100f);
                var ok = Mathf.Abs(hp.FrontFill.fillAmount - 0.5f) < 0.001f && hp.ValueText.text == "50/100";
                Kill(go);
                return ok;
            }, ref fail);

            // ---------- 余件断言 ----------

            pass += Check("Toast：Template 接线且为非激活", () =>
            {
                var go = Instantiate("Toast");
                var t = go.GetComponent<LiteGame.UI.Toast>();
                var ok = t != null && t.Template != null && !t.Template.gameObject.activeSelf;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Loading：阻断面吃射线 + 文案接线", () =>
            {
                var go = Instantiate("Loading");
                var m = go.GetComponent<LiteGame.UI.LoadingMask>();
                var ok = m != null && m.Blocker != null && m.Blocker.raycastTarget && m.Message != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Toast：多条并存（3 条各自计时，先到者独立消失）", () =>
            {
                var go = Instantiate("Toast");
                var t = go.GetComponent<LiteGame.UI.Toast>();
                t.Show("A", 1f);
                t.Show("B", 5f);
                t.Show("C", 5f);
                bool three = t.VisibleCount == 3;
                t.Tick(1.5f);                       // A 到期；B/C 各自还剩 3.5s
                bool aGone = t.VisibleCount == 2;
                t.Tick(3.6f);                       // B/C 一并到期
                bool allGone = t.VisibleCount == 0;
                Kill(go);
                return three && aGone && allGone;
            }, ref fail);

            pass += Check("Toast：超上限淘汰最旧并计数", () =>
            {
                var go = Instantiate("Toast");
                var t = go.GetComponent<LiteGame.UI.Toast>();
                for (int i = 0; i < 6; i++) t.Show("T" + i, 5f);
                bool capped = t.VisibleCount == t.MaxVisible;
                bool counted = t.DroppedCount == 2;
                Kill(go);
                return capped && counted;
            }, ref fail);

            pass += Check("Bubble：Label 接线", () =>
            {
                var go = Instantiate("Bubble");
                var b = go.GetComponent<LiteGame.UI.UIBubble>();
                var ok = b != null && b.Label != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("FlyText：模板非激活 + 池参数默认", () =>
            {
                var go = Instantiate("FlyText");
                var p = go.GetComponent<LiteGame.UI.FlyTextPool>();
                var ok = p != null && p.Template != null && !p.Template.gameObject.activeSelf
                         && Mathf.Abs(p.RiseDistance - 80f) < 0.01f && p.PoolDepth == 16;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("RedDot：绑定树节点后计数驱动显隐", () =>
            {
                var go = Instantiate("RedDot");
                var rd = go.GetComponent<LiteGame.UI.RedDot>();
                if (rd == null || rd.Dot == null) { Kill(go); return false; }
                var tree = new LiteGame.UI.RedDotTree();
                rd.Bind(tree, "mail");
                bool hiddenAtZero = !rd.Dot.activeSelf;
                tree.Node("mail").SetCount(2);
                bool shownAfter = rd.Dot.activeSelf;
                tree.Node("mail").SetCount(0);
                bool hiddenAgain = !rd.Dot.activeSelf;
                Kill(go);
                return hiddenAtZero && shownAfter && hiddenAgain;
            }, ref fail);

            pass += Check("TabGroup：三页签（编辑态结构 / Play 态含点击切页回调）", () =>
            {
                var go = Instantiate("TabGroup");
                var g = go.GetComponent<LiteGame.UI.TabGroup>();
                if (g == null || g.Tabs == null || g.Tabs.Count != 3) { Kill(go); return false; }
                if (!Application.isPlaying) { Kill(go); return true; }   // 编辑态 Instantiate 不跑 Awake → 接线未注册，点击断言留 Play 态
                int got = -1;
                g.OnTabChanged += i => got = i;
                g.Tabs[1].onClick.Invoke();
                var ok = got == 1;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("BottomNav：内含页签组且四入口", () =>
            {
                var go = Instantiate("BottomNav");
                var nav = go.GetComponent<LiteGame.UI.BottomNav>();
                var ok = nav != null && nav.Tabs != null && nav.Tabs.Tabs != null && nav.Tabs.Tabs.Count == 4;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("ProgressBar：Fill 接线 + Set 驱动 fillAmount 与文本", () =>
            {
                var go = Instantiate("ProgressBar");
                var bar = go.GetComponent<LiteGame.UI.ProgressBar>();
                if (bar == null || bar.Fill == null) { Kill(go); return false; }
                bar.Set(3f, 4f);
                var ok = Mathf.Abs(bar.Fill.fillAmount - 0.75f) < 0.001f && bar.ValueText.text == "3/4";
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("StarRating：五星接线 + Set 钳制", () =>
            {
                var go = Instantiate("StarRating");
                var sr = go.GetComponent<LiteGame.UI.StarRating>();
                if (sr == null || sr.Stars == null || sr.Stars.Length != 5) { Kill(go); return false; }
                sr.Set(3);
                var ok = sr.Value == 3;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("CountText：Label 指向自身文本", () =>
            {
                var go = Instantiate("CountText");
                var c = go.GetComponent<LiteGame.UI.CountText>();
                var ok = c != null && c.Label != null && c.Label == go.GetComponent<TMPro.TMP_Text>();
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Countdown：到点回调（确定性 Tick 驱动）", () =>
            {
                var go = Instantiate("Countdown");
                var cd = go.GetComponent<LiteGame.UI.Countdown>();
                if (cd == null || cd.Label == null) { Kill(go); return false; }
                bool done = false;
                cd.OnDone.AddListener(() => done = true);
                cd.StartCountdown(0.05f);
                cd.Tick(0.1f);
                var ok = done && !cd.Running;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("AnimatedImage：组件 + 播放参数默认", () =>
            {
                var go = Instantiate("AnimatedImage");
                var a = go.GetComponent<LiteGame.UI.AnimatedImage>();
                var ok = a != null && a.Loop && a.PlayOnEnable && Mathf.Abs(a.Fps - 10f) < 0.01f;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("AvatarFrame：头像/边框/等级三接线", () =>
            {
                var go = Instantiate("AvatarFrame");
                var a = go.GetComponent<LiteGame.UI.AvatarFrame>();
                var ok = a != null && a.Avatar != null && a.Frame != null && a.LevelBadge != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Stepper：三接线 + Set 上限钳制", () =>
            {
                var go = Instantiate("Stepper");
                var s = go.GetComponent<LiteGame.UI.Stepper>();
                if (s == null || s.Minus == null || s.Plus == null || s.ValueLabel == null) { Kill(go); return false; }
                s.Set(99);
                var ok = s.Value == s.Max;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("InputField：Field 接线（文本/占位）", () =>
            {
                var go = Instantiate("InputField");
                var w = go.GetComponent<LiteGame.UI.UIInputFieldWrap>();
                var ok = w != null && w.Field != null && w.Field.textComponent != null && w.Field.placeholder != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Slider：fillRect/handleRect 接线", () =>
            {
                var go = Instantiate("Slider");
                var w = go.GetComponent<LiteGame.UI.UISliderWrap>();
                var ok = w != null && w.Slider != null && w.Slider.fillRect != null && w.Slider.handleRect != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Toggle：Toggle 接线（graphic 指向勾）", () =>
            {
                var go = Instantiate("Toggle");
                var w = go.GetComponent<LiteGame.UI.UIToggleWrap>();
                var ok = w != null && w.Toggle != null && w.Toggle.graphic != null && w.Toggle.targetGraphic != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("Dropdown：标题/项文本/模板三接线", () =>
            {
                var go = Instantiate("Dropdown");
                var w = go.GetComponent<LiteGame.UI.UIDropdownWrap>();
                var ok = w != null && w.Dropdown != null && w.Dropdown.captionText != null
                         && w.Dropdown.itemText != null && w.Dropdown.template != null
                         && !w.Dropdown.template.gameObject.activeSelf;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("EventRelay：OnClicked 事件就绪", () =>
            {
                var go = Instantiate("EventRelay");
                var r = go.GetComponent<LiteGame.UI.UIEventRelay>();
                var ok = r != null && r.OnClicked != null;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("GuideHighlight：Frame 接线 + Target 对齐", () =>
            {
                var go = Instantiate("GuideHighlight");
                var gh = go.GetComponent<LiteGame.UI.GuideHighlight>();
                if (gh == null || gh.Frame == null) { Kill(go); return false; }
                var probe = new GameObject("probe", typeof(RectTransform));
                var probeRt = (RectTransform)probe.transform;
                probeRt.position = new Vector3(123f, 45f, 0f);
                probeRt.sizeDelta = new Vector2(88f, 66f);
                gh.Target(probeRt);
                var ok = Vector3.Distance(gh.Frame.position, probeRt.position) < 0.01f
                         && Vector2.Distance(gh.Frame.sizeDelta, probeRt.rect.size) < 0.01f;
                Kill(probe);
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("SafeArea：组件就绪 + 换策略不抛", () =>
            {
                var go = Instantiate("SafeArea");
                var sa = go.GetComponent<SafeAreaReceiver>();
                if (sa == null) { Kill(go); return false; }
                sa.SetStrategy(new DefaultSafeAreaStrategy());
                Kill(go);
                return true;
            }, ref fail);

            pass += Check("SimpleList：Template 接线", () =>
            {
                var go = Instantiate("SimpleList");
                var l = go.GetComponent<SimpleListView>();
                var ok = l != null && l.Template != null && !l.Template.gameObject.activeSelf;
                Kill(go);
                return ok;
            }, ref fail);

            // ---------- 受控 API 扩展断言 ----------

            pass += Check("G1：SetInteractable 打 StateButton 不抛（回退 UIWidget.Interactable）", () =>
            {
                var go = Instantiate("StateButton");
                var btn = go.GetComponent<LiteGame.UI.StateButton>();
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Btn"] = btn });
                index.SetInteractable("Btn", false);
                var offOk = !btn.Interactable;
                index.SetInteractable("Btn", true);
                var onOk = btn.Interactable;
                Kill(go);
                return offOk && onOk;
            }, ref fail);

            pass += Check("G7：SetProgress 归一化 / 当前-上限 / SetHp", () =>
            {
                var barGo = Instantiate("ProgressBar");
                var bar = barGo.GetComponent<LiteGame.UI.ProgressBar>();
                var barIndex = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Bar"] = bar });
                barIndex.SetProgress("Bar", 0.25f);
                bool normOk = Mathf.Abs(bar.Fill.fillAmount - 0.25f) < 0.001f;
                barIndex.SetProgress("Bar", 3f, 4f);
                bool rangeOk = Mathf.Abs(bar.Fill.fillAmount - 0.75f) < 0.001f;
                Kill(barGo);

                var hpGo = Instantiate("HpBar");
                var hp = hpGo.GetComponent<LiteGame.UI.HpBar>();
                var hpIndex = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Hp"] = hp });
                hpIndex.SetHp("Hp", 50f, 100f);
                bool hpOk = Mathf.Abs(hp.FrontFill.fillAmount - 0.5f) < 0.001f && hp.ValueText.text == "50/100";
                Kill(hpGo);
                return normOk && rangeOk && hpOk;
            }, ref fail);

            pass += Check("G10：StartCountdown / StopCountdown", () =>
            {
                var go = Instantiate("Countdown");
                var cd = go.GetComponent<LiteGame.UI.Countdown>();
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Cd"] = cd });
                index.StartCountdown("Cd", 5f);
                bool started = cd.Running;
                index.StopCountdown("Cd");
                bool stopped = !cd.Running;
                Kill(go);
                return started && stopped;
            }, ref fail);

            pass += Check("G3a：ShowToast（场景内 Toast 实例 + 子实例出现）", () =>
            {
                var go = Instantiate("Toast");                       // Toast.Instance 会随场景查找命中它
                var index = LiteGame.UIBindIndex.Empty;
                index.ShowToast("提示测试");
                var spawned = go.transform.childCount == 2;           // 模板(0) + 实例(1)
                Kill(go);
                return spawned;
            }, ref fail);

            pass += Check("G3b：ShowBubble 激活气泡", () =>
            {
                var go = Instantiate("Bubble");
                var bubble = go.GetComponent<LiteGame.UI.UIBubble>();
                go.SetActive(false);
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["B"] = bubble });
                index.ShowBubble("B", "气泡测试", 1.5f);
                var ok = go.activeSelf;
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("G3c：ShowFlyText 生成飘字实例", () =>
            {
                var go = Instantiate("FlyText");
                var pool = go.GetComponent<LiteGame.UI.FlyTextPool>();
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["F"] = pool });
                index.ShowFlyText("F", "飘字测试");
                var ok = go.transform.childCount == 2;                // 模板(0) + 实例(1)
                Kill(go);
                return ok;
            }, ref fail);

            // ---------- 动效口断言（EditMode 只验解析层与错误语义；DOTween 行为留 Play）----------
            // 为何不在 EditMode 断言 tween 状态：① LiteGame.Editor.asmdef 不引用 DOTween.Modules（写不了 DOTween API）
            // ② EditMode 无播放循环 tween 不推进，且 DOTween.Init() 会 spawn 对象污染当前打开的场景。

            pass += Check("G20a：ResolveGraphic 对 Button 节点回退 targetGraphic（不抛）", () =>
            {
                var go = new GameObject("G20Btn", typeof(RectTransform), typeof(Image), typeof(Button));
                var img = go.GetComponent<Image>();
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;                              // 模拟 BindNode 对 Button 节点的检测结果（存的是 Button）
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Btn"] = btn });
                var g = index.ResolveGraphic("Btn");
                var ok = ReferenceEquals(g, img);
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("G20b：ResolveGraphic 直取 Graphic / ResolveRect 走 transform", () =>
            {
                var go = new GameObject("G20Icon", typeof(RectTransform), typeof(Image));
                var img = go.GetComponent<Image>();
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Icon"] = img });
                var ok = ReferenceEquals(index.ResolveGraphic("Icon"), img)
                         && ReferenceEquals(index.ResolveRect("Icon"), (RectTransform)go.transform);
                Kill(go);
                return ok;
            }, ref fail);

            pass += Check("G20c：未命中 = KeyNotFoundException（与既有受控 API 同语义）", () =>
            {
                bool threw;
                try { LiteGame.UIBindIndex.Empty.Pulse("Nope", 1.2f); threw = false; }
                catch (KeyNotFoundException) { threw = true; }
                catch { threw = false; }
                return threw;
            }, ref fail);

            pass += Check("G20d：命中但无 Graphic = InvalidOperationException", () =>
            {
                var go = new GameObject("G20Cg", typeof(RectTransform), typeof(CanvasGroup));
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["X"] = go.GetComponent<CanvasGroup>() });
                bool threw;
                try { index.Flash("X", 0.3f); threw = false; }
                catch (InvalidOperationException) { threw = true; }
                catch { threw = false; }
                Kill(go);
                return threw;
            }, ref fail);

            pass += Check("G20e：Pulse/Flash/Slide 真实调用不抛（DOTween 仅 Play 态成立）", () =>
            {
                var go = new GameObject("G20Fx", typeof(RectTransform), typeof(Image));
                var index = new LiteGame.UIBindIndex(new Dictionary<string, Component> { ["Fx"] = go.GetComponent<Image>() });
                if (!Application.isPlaying) { Kill(go); return true; } // 同 TabGroup 先例：EditMode 不跑 tween
                var ok = index.Pulse("Fx", 1.2f) && index.Flash("Fx", 0.3f)
                         && index.Slide("Fx", new Vector2(0f, 40f), 0.25f);
                Kill(go);
                return ok;
            }, ref fail);

            return (pass, fail);
        }

        private sealed class CountSource : IVirtualListSource
        {
            private readonly int _n;
            public CountSource(int n) => _n = n;
            public int Count => _n;
            public void Bind(int index, Component item) { }
        }

        private static GameObject Instantiate(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{name}.prefab");
            if (prefab == null) throw new InvalidOperationException($"模板缺失:{name}.prefab（先跑“构建控件模板 Prefabs”）");
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        private static void Kill(GameObject go) => UnityEngine.Object.DestroyImmediate(go);

        private static int Check(string name, Func<bool> assert, ref int fail)
        {
            bool ok;
            try { ok = assert(); }
            catch (Exception ex)
            {
                Debug.LogError($"[{Tag}] FAIL {name}（{ex.GetType().Name}:{ex.Message}）");
                fail++;
                return 0;
            }
            if (!ok) { fail++; Debug.LogError($"[{Tag}] FAIL {name}"); return 0; }
            Debug.Log($"[{Tag}] PASS {name}");
            return 1;
        }
    }
}
