using System.Collections.Generic;
using System.Globalization;
using LiteFramework.UI;

namespace LiteGame.Modules.UI
{
    /// <summary>结算页：本机摘要 ＋ 玩家排行列表（名次口径在 C#，本页只做行投影）。</summary>
    public sealed class Page_ResultLogic : PageLogicBase
    {
        protected override void OnShow(IUIData data)
        {
            SetTextKey("Txt_BackLobby", "Common.BackLobby");
            SetText("Txt_HeaderRank", "名次");
            SetText("Txt_HeaderPlayer", "玩家");
            SetText("Txt_HeaderKills", "击杀");
            SetText("Txt_HeaderDeaths", "死亡");
            SetText("Txt_RankEmpty", "暂无排行");
            OnButtonClick("Btn_BackLobby", () => Replace(PageIds.Page_Lobby));

            if (data is ResultPageData result && result.HasResult)
            {
                Scoreboard(result);
                PushRankboard(result.Rows);
                return;
            }
            Empty();
            PushRankboard(null);
        }

        private void PushRankboard(IReadOnlyList<ResultPageData.Row> ranks)
        {
            var rows = UIListRowSet.Create();
            int count = ranks?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                ResultPageData.Row row = ranks[i];
                rows.Row((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Text("Txt_Rank", row.Rank.ToString(CultureInfo.InvariantCulture))
                    .Text("Txt_PlayerName", row.Name)
                    .KeyText("Txt_Kills", "UI.Result.Kills", row.Kills)
                    .KeyText("Txt_Deaths", "UI.Result.Deaths", row.Deaths);
            }
            Index.SetList("VList_Rankboard", rows);
            SetVisible("Txt_RankEmpty", count == 0);
        }

        private void Scoreboard(ResultPageData data)
        {
            SetVisible("Cot_Scoreboard", true);
            SetVisible("Txt_KillCount", true);
            SetVisible("Txt_DeathCount", true);
            string reasonKey = "UI.Result.Reason." + data.Reason;      // 值域随枚举（设计 §8）；键在运行期拼
            SetTextKey("Txt_ResultTitle", reasonKey);
            SetTextKey("Txt_KillCount", "UI.Result.Kills", data.Kills);
            SetTextKey("Txt_DeathCount", "UI.Result.Deaths", data.Deaths);
            SetVisible("Txt_MyRank", data.Rank > 0);
            if (data.Rank > 0) SetTextKey("Txt_MyRank", "UI.Result.Rank", data.Rank);
        }

        private void Empty()
        {
            SetTextKey("Txt_ResultTitle", "UI.Result.NoMatchData");
            SetVisible("Cot_Scoreboard", false);
            SetVisible("Txt_KillCount", false);
            SetVisible("Txt_DeathCount", false);
            SetVisible("Txt_MyRank", false);
        }
    }
}
