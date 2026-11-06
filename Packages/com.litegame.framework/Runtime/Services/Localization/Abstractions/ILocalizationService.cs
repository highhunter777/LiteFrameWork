using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 本地化查询端口（`Get/Format/Raw` 为目标接口——本类即其实现）。
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>当前语言（BCP-47，如 "zh-CN"/"en"）。</summary>
        string Locale { get; }

        /// <summary>语言变更事件（携带新 locale）。LTextLabel 等订阅它刷新，**不重跑 OnShow**。</summary>
        event Action<string> OnLocaleChanged;

        /// <summary>切换语言；同值 = 无操作（不触发事件）。</summary>
        void SetLocale(string locale);

        /// <summary>取文本（**未转义**原文；缺 key 走缺 key 策略）。</summary>
        string Raw(string key);

        /// <summary>取文本（**安全文本**：玩家/外部插入的参数按规范转义）。</summary>
        string Get(string key);

        /// <summary>取文本并填参（模板参数按编号/语义验证）。</summary>
        string Format(string key, params object[] args);

        /// <summary>带复数的取文本（英文 one/other 显式选取）。</summary>
        string FormatPlural(string key, long count, params object[] args);

        /// <summary>缺 key 累计次数（诊断——发布期据此判断翻译完整性）。</summary>
        int MissingKeyCount { get; }
    }
}
