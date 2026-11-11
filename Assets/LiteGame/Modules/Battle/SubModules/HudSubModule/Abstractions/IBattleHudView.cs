using LiteFramework.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Modules.Battle.SubModules.HudSubModule.Abstractions
{
    /// <summary>
    /// 战斗 HUD 视图引用契约（序列化引用即契约）：装配器只依赖本接口，`BattleHudView` 实现。
    /// 件缺失约定：可空件返回 null 由驱动内静默降级；整族判据用 `Has*`。
    /// </summary>
    public interface IBattleHudView : IHudView
    {
        RectTransform CrosshairRoot { get; }
        GameObject HipForm { get; }
        GameObject AdsForm { get; }

        CanvasGroup PanelGroup { get; }
        HpBar HpBar { get; }
        TextMeshProUGUI AmmoText { get; }
        TextMeshProUGUI ReserveAmmoText { get; }
        GameObject SlashSeparator { get; }
        GameObject ReloadLabel { get; }
        Button SettingsButton { get; }

        bool HasRankboard { get; }
        GameObject[] RankCells { get; }
        TextMeshProUGUI[] RankNames { get; }
        TextMeshProUGUI[] RankKills { get; }
        GameObject[] RankHighlights { get; }
        TextMeshProUGUI TeamScoreText { get; }

        bool HasKillFeed { get; }
        GameObject[] KillFeedRows { get; }
        TextMeshProUGUI[] KillFeedKillers { get; }
        Image[] KillFeedWeapons { get; }
        TextMeshProUGUI[] KillFeedVictims { get; }
        Sprite[] WeaponIcons { get; }

        RectTransform NetStatsRoot { get; }
        TextMeshProUGUI NetStatsText { get; }

        bool HasRespawnHud { get; }
        RectTransform RespawnRoot { get; }
        TextMeshProUGUI RespawnTitle { get; }
        TextMeshProUGUI RespawnCountdown { get; }
        RectTransform ProtectionRoot { get; }
    }
}
