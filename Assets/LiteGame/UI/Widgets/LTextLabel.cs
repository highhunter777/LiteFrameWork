using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// LTextLabel：本地化文本控件的**打字机表现行为**（《UI框架总设计》§9——"LTextLabel 打字机为
    /// 表现行为，关闭/语言变化取消旧任务；不改变业务数据"；U2-⑤d）。
    ///
    /// - **数据不变**：只决定"可见多少"，不改变最终文本——完成态逐字符等于全量文本；
    ///   取消（组件禁用）同样落全量——页面关闭/隐藏后重开**不残留半截文本**；
    /// - **取消语义**：新 <see cref="Reveal"/> 即取消旧任务；组件禁用（页面关闭/隐藏）即取消（不触发
    ///   OnCompleted——取消不是完成）；语言变更经绑定层重写文本 → 再次 <see cref="Reveal"/> →
    ///   旧任务自然取消（§9"关闭/语言变化取消旧任务"）；
    /// - **未激活登记待播**：组件隐藏时 <see cref="Reveal"/> 不在暗处跑完——登记后 <c>OnEnable</c>
    ///   起播（页面打开才看得见打字过程）；
    /// - **驱动者唯一**（§8.1"AutoTick 与外部 Tick，任一时刻只有一个驱动者"）：AutoTick 由
    ///   <c>Update</c> 自驱（步进取 <see cref="UiAnimationClock.Delta"/>——UIClock 分域时钟语义）；
    ///   关闭 AutoTick 后由外部 ticker 调 <see cref="Tick"/>（双驱动 Debug 下显性报错并忽略）；
    /// - **富文本安全**：截点既不落 `<…>` 标签内部（半截标签不当字面显示），也不在开标签未闭合期间
    ///   截断（可见段富文本始终配平——中间态不留未闭合开标签；作者文本允许富文本标签——§9）；
    ///
    /// 路由：节点挂本组件时，<c>UIBindIndex.SetText</c>（命令式/本地化路径）经 <see cref="Reveal"/>
    /// 进打字机；**数据绑定路径（BindText）保持瞬时**——数值类文本不走打字机（表现行为只属于叙事文本）。
    /// </summary>
    [ExecuteAlways]
    public sealed class LTextLabel : MonoBehaviour
    {
        [Tooltip("自驱（Update 步进）——关闭后由外部 ticker 调 Tick（单驱动者）")]
        [field: SerializeField] public bool AutoTick { get; set; } = true;
        [Tooltip("打字速率（字符/秒）——≤0 = 不做表现，直接全量")]
        [SerializeField] private float charsPerSecond = 30f;

        private string _full = string.Empty;
        private int[] _cutPoints;              // 安全截点（可见字符数；不在标签内）——升序，末位 = 全长
        private int _cutIndex;                 // 当前进度（截点表下标；0 = 未显示）
        private float _progress;                // 自上一截点累计的字符数（浮点节拍）
        private bool _playing;
        private bool _pending;                // 未激活时登记待播（OnEnable 起播）

        // 文本目标（延迟解析——组件齐全性不保证于 Awake；TMP 优先、回退 UGUI Text，与绑定层同序）
        private TMPro.TMP_Text _tmp;
        private Text _legacy;
        private bool _targetResolved;

        /// <summary>完成事件（全量可见时恰好一次；Complete() 跳到位也按完成触发；取消不触发）。</summary>
        public event Action OnCompleted;

        /// <summary>是否在播（完成/取消即 false）。</summary>
        public bool IsPlaying => _playing;

        /// <summary>打字速率（字符/秒）；≤0 = 直接全量。运行时可调。</summary>
        public float CharsPerSecond
        {
            get => charsPerSecond;
            set => charsPerSecond = value;
        }

        /// <summary>
        /// 打字机起播（取消旧任务）。速率 ≤0 / 空文本 / 单截点 = 直接全量（完成语义）；
        /// 组件未激活 = 登记待播（OnEnable 起播——不在暗处跑完）。起播立即显示首个安全截点。
        /// </summary>
        public void Reveal(string fullText)
        {
            _full = fullText ?? string.Empty;
            _cutPoints = BuildCutPoints(_full);
            _cutIndex = 0;
            _progress = 0f;
            _pending = false;

            // 截点表 = [0 哨兵, 首截点, …, 末截点]（BuildCutPoints）：≤2 项即无步进意义——
            // 空文本 [0]、或首个可见截点已是全量 [0, 全长]（单截点）→ 直接全量（完成语义）。
            if (charsPerSecond <= 0f || _cutPoints.Length <= 2)
            {
                Finish();                                       // 无步进意义：直接全量（完成语义）
                return;
            }

            if (!isActiveAndEnabled)
            {
                _pending = true;                                 // 隐藏态登记，OnEnable 起播
                _playing = false;
                return;
            }

            StartPlaying();
        }

        /// <summary>跳到全量（立即完成；按完成语义触发 <see cref="OnCompleted"/>）。</summary>
        public void Complete()
        {
            if (!_playing) return;
            Finish();
        }

        /// <summary>
        /// 外部步进（单驱动者：AutoTick 开启时调用 = 双驱动违例，Debug 三宏下显性报错并忽略）。
        /// </summary>
        public void Tick(float delta)
        {
            if (!_playing) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            if (AutoTick)
            {
                Log.Error("LTextLabel: AutoTick 与外部 Tick 双驱动（§8.1 唯一驱动者）——请关闭 AutoTick 或停用自驱", LogTag.UI);
                return;
            }
#endif
            Advance(delta);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;   // 编辑器态不步进（ExecuteAlways 仅为开关态语义；打字机是运行时表现）
            if (!AutoTick || !_playing) return;
            Advance(UiAnimationClock.Delta);
        }

        private void OnEnable()
        {
            if (!_pending) return;
            _pending = false;
            StartPlaying();
        }

        /// <summary>取消（页面关闭/隐藏）：任务停、文本落全量（数据不变——重开不残留半截）、不触发完成事件。
        /// **有任务才落**：无任务（编辑器装载/卸载 prefab 的装载期开关态）不写——不碰资产文本。</summary>
        private void OnDisable()
        {
            bool hadTask = _playing || _pending;
            _playing = false;
            _pending = false;
            if (hadTask) WriteTarget(_full);
        }

        private void StartPlaying()
        {
            _playing = true;
            _cutIndex = 1;                                       // 下标 1 = 首个可见截点（表首 0 为"未显示"哨兵）
            _progress = 0f;
            ShowAt(_cutIndex);
        }

        private void Advance(float delta)
        {
            if (delta <= 0f) return;
            _progress += delta * charsPerSecond;

            while (_progress >= 1f && _playing)
            {
                _progress -= 1f;
                _cutIndex++;
                // 推进到末截点下标（Length-1 = 全量可见）即完成——不必等再一次步进去"越过"它
                if (_cutIndex >= _cutPoints.Length - 1) { Finish(); return; }
                ShowAt(_cutIndex);
            }
        }

        private void ShowAt(int cutIndex)
        {
            if (!ResolveTarget())
            {
                Log.Error($"LTextLabel: 节点无 TMP_Text/Text 组件:{gameObject.name}", LogTag.UI);
                _playing = false;
                _pending = false;
                return;
            }
            WriteTarget(SubstringAt(_full, _cutPoints[cutIndex]));
        }

        private void Finish()
        {
            _playing = false;
            _pending = false;
            _cutIndex = _cutPoints != null && _cutPoints.Length > 0 ? _cutPoints.Length - 1 : 0;
            _progress = 0f;

            if (ResolveTarget()) WriteTarget(_full);             // 数据不变：完成态 = 全量原文
            OnCompleted?.Invoke();
        }

        /// <summary>解析文本目标（一次；TMP 优先回退 UGUI Text——与 UIBindIndex.SetText 同序）。</summary>
        private bool ResolveTarget()
        {
            if (_targetResolved) return _tmp != null || _legacy != null;
            _targetResolved = true;
            _tmp = gameObject.GetComponent<TMPro.TMP_Text>();
            if (_tmp == null) _legacy = gameObject.GetComponent<Text>();
            return _tmp != null || _legacy != null;
        }

        private void WriteTarget(string value)
        {
            if (!ResolveTarget()) return;
            if (_tmp != null) _tmp.text = value;
            else _legacy.text = value;
        }

        /// <summary>可见子串（前 visibleChars 个字符——富文本标签始终整段含入）。</summary>
        private static string SubstringAt(string full, int visibleChars)
            => full.Substring(0, visibleChars);

        /// <summary>
        /// 安全截点表（升序；每项 = 可见到的字符数）。**首项恒 0＝"未显示"哨兵**——
        /// 下标即推进进度（0 = 未显示，1 = 首个可见截点），起播/推进按下标寻值不偏一。
        ///
        /// 合法截点两条（缺一不可）：
        /// ①不在 `<…>` 标签内部——半截标签会当字面显示；
        /// ②可见段富文本**配平**——开标签未闭合期间不截（`&lt;color=red&gt;红` 这类留下未闭合
        /// 开标签的中间态不合法，须截到对应闭合标签之后）。自闭合标签（`&lt;sprite… /&gt;`）净计数为零。
        /// 空文本 = [0]；无配平截点的文本（作者未闭合标签）= [0]——皆无步进意义（Reveal 直接全量）。
        /// </summary>
        private static int[] BuildCutPoints(string text)
        {
            var cuts = new List<int>(text.Length + 1) { 0 };   // 0 位哨兵：未显示
            bool inTag = false;
            bool tagClosing = false;                             // "</" 开头 = 闭合标签
            bool tagFirst = false;                               // 处于 '<' 后首字符位
            int openTags = 0;                                    // 已开未闭标签数
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!inTag)
                {
                    if (c == '<') { inTag = true; tagClosing = false; tagFirst = true; }
                    else if (openTags == 0) cuts.Add(i + 1);    // 标签外普通字符：开标签全闭合才可截
                }
                else if (c == '>')
                {
                    inTag = false;
                    openTags += tagClosing ? -1 : 1;
                    if (openTags == 0) cuts.Add(i + 1);          // 开标签全部闭合（含闭合标签收口）才可截
                }
                else if (tagFirst && c == '/') tagClosing = true;
                if (inTag || c == '<') tagFirst = c == '<';
            }
            return cuts.ToArray();
        }
    }
}
