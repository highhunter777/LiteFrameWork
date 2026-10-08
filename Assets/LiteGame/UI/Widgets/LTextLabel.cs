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
    /// - **富文本安全**：可见截点不在 `<…>` 标签内部截断（作者文本允许富文本标签——§9），
    ///   中间态不会把半截标签当字面显示。
    ///
    /// 路由：节点挂本组件时，<c>UIBindIndex.SetText</c>（命令式/本地化路径）经 <see cref="Reveal"/>
    /// 进打字机；**数据绑定路径（BindText）保持瞬时**——数值类文本不走打字机（表现行为只属于叙事文本）。
    /// </summary>
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

            if (charsPerSecond <= 0f || _cutPoints.Length <= 1)
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
                Log.Error("LTextLabel: AutoTick 与外部 Tick 双驱动（§8.1 唯一驱动者）——请关闭 AutoTick 或停用自驱", "UI");
                return;
            }
#endif
            Advance(delta);
        }

        private void Update()
        {
            if (!AutoTick || !_playing) return;
            Advance(UiAnimationClock.Delta);
        }

        private void OnEnable()
        {
            if (!_pending) return;
            _pending = false;
            StartPlaying();
        }

        /// <summary>取消（页面关闭/隐藏）：任务停、文本落全量（数据不变——重开不残留半截）、不触发完成事件。</summary>
        private void OnDisable()
        {
            _playing = false;
            _pending = false;
            WriteTarget(_full);                                  // 无目标则解析一次再落（终态一致性）
        }

        private void StartPlaying()
        {
            _playing = true;
            _cutIndex = 1;                                       // 起播即显示首个安全截点（≥1 字符）
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
                int next = _cutIndex + 1;
                if (next >= _cutPoints.Length) { Finish(); return; }
                _cutIndex = next;
                ShowAt(_cutIndex);
            }
        }

        private void ShowAt(int cutIndex)
        {
            if (!ResolveTarget())
            {
                Log.Error($"LTextLabel: 节点无 TMP_Text/Text 组件:{gameObject.name}", "UI");
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
        /// 安全截点表（升序；每项 = 可见到的字符数）：可见字符每 +1 一项；处于 `<…>` 标签内部的
        /// 索引**不是**截点（截断会把半截标签当字面显示）。空文本 = [0]（无步进意义）。
        /// </summary>
        private static int[] BuildCutPoints(string text)
        {
            var cuts = new List<int>(text.Length + 1);
            bool inTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                if (!inTag) cuts.Add(i + 1);                    // 标签闭合后的位置才可截
            }
            return cuts.Count > 0 ? cuts.ToArray() : new[] { 0 };
        }
    }
}
