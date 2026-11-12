using System.Text;
using LiteGameNext.Platform.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGameNext.Features.Battle.Hud
{
    /// <summary>战斗 HUD 视图：10 类读数绘制；只读模型，不参与输入与判定。缺件不阻断对局。</summary>
    public sealed class BattleHudView : PageBase
    {
        public static readonly PageSpec Spec = new PageSpec(
            "Assets/LiteGame/Next/Runtime/Features/Battle/Content/BattleHud.prefab",
            PageLayer.Overlay, fullScreen: false, PageCachePolicy.DestroyOnClose);

        private BattleHudModel _model;

        public void Bind(BattleHudModel model)
        {
            _model = model;
            if (IsOpen) Refresh();
        }

        protected override void OnShow() => Refresh();

        /// <summary>按模型全量刷新 10 类读数。</summary>
        public void Refresh()
        {
            if (_model == null) return;
            RefreshVitals();
            RefreshAmmo();
            RefreshCrosshair();
            RefreshScore();
            RefreshLifeState();
            RefreshRankboard();
            RefreshKillFeed();
            RefreshNetwork();
        }

        private void RefreshVitals()
        {
            SetTextSafe("Txt_Health", _model.Health + " / " + _model.MaxHealth);
            Slider bar = BindSafe<Slider>("Sld_Health");
            if (bar == null) return;
            bar.minValue = 0f;
            bar.maxValue = 1f;
            bar.SetValueWithoutNotify(_model.MaxHealth <= 0
                ? 0f
                : (float)_model.Health / _model.MaxHealth);
        }

        private void RefreshAmmo()
        {
            string reserve = _model.ReserveAmmo > 0 ? "/" + _model.ReserveAmmo : string.Empty;
            SetTextSafe("Txt_Ammo", _model.IsReloading ? "换弹中…" : "AMMO " + _model.Ammo + reserve);
        }

        private void RefreshCrosshair()
        {
            SetVisibleSafe("Cot_Hip", !_model.IsAiming);
            SetVisibleSafe("Cot_Ads", _model.IsAiming);
            RectTransform crosshair = BindSafe<RectTransform>("Cot_Crosshair");
            if (crosshair == null) return;
            float size = Mathf.Min(120f, 36f + _model.Spread * 8f);
            crosshair.sizeDelta = new Vector2(size, size);
        }

        private void RefreshScore()
        {
            SetTextSafe("Txt_Score", "K " + _model.Kills + "  D " + _model.Deaths);
            SetTextSafe("Txt_TeamScore", "队伍 " + _model.TeamScore);
        }

        private void RefreshLifeState()
        {
            SetVisibleSafe("Cot_Respawn", _model.IsDead);
            if (_model.IsDead)
                SetTextSafe("Txt_Respawn", "复活 " + Mathf.CeilToInt(_model.RespawnSeconds) + "s");
            SetVisibleSafe("Cot_Protection", _model.IsProtected);
        }

        private void RefreshRankboard()
        {
            SetVisibleSafe("Cot_Rankboard", _model.Rankboard.Count > 0);
            for (int i = 0; i < 8; i++)
            {
                string name = "Txt_Rank" + (i + 1);
                if (_model.Rankboard.Count <= i)
                {
                    SetTextSafe(name, string.Empty);
                    continue;
                }
                RankRow row = _model.Rankboard[i];
                SetTextSafe(name, (row.IsLocal ? "▶ " : string.Empty) + row.Name + "  " + row.Kills);
            }
        }

        private void RefreshKillFeed()
        {
            SetVisibleSafe("Cot_KillFeed", _model.KillFeed.Count > 0);
            for (int i = 0; i < 5; i++)
            {
                string name = "Txt_Feed" + (i + 1);
                if (_model.KillFeed.Count <= i)
                {
                    SetTextSafe(name, string.Empty);
                    continue;
                }
                KillFeedRow row = _model.KillFeed[i];
                var sb = new StringBuilder();
                sb.Append(row.Killer).Append(" → ").Append(row.Victim);
                SetTextSafe(name, sb.ToString());
            }
        }

        private void RefreshNetwork()
        {
            SetTextSafe("Txt_Net", _model.RttMs + "ms  " + (_model.LossPermille / 10f).ToString("F1") + "%");
        }

        // ---- 容错封装：HUD 缺件不阻断对局（PageBase 原生 SetText/SetVisible 缺控件会抛）----

        private void SetTextSafe(string name, string value)
        {
            if (HasBinding(name)) SetText(name, value);
        }

        private void SetVisibleSafe(string name, bool visible)
        {
            if (HasBinding(name)) SetVisible(name, visible);
        }

        private T BindSafe<T>(string name) where T : Component
            => HasBinding(name) ? Bind<T>(name) : null;

        private bool IsOpen => Handle != null && !Handle.IsClosed;
    }
}
