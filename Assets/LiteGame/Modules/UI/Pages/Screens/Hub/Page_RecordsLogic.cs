using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteGame.Modules.UI.Bind;
using LiteGame.Modules.Meta.Abstractions.Session;

namespace LiteGame.Modules.UI.Pages.Screens.Hub
{
    /// <summary>最近战绩页：进入即查最近 N 局（空表/未配置 = 空态），行点击无动作，返回大厅。</summary>
    public sealed class Page_RecordsLogic : PageLogicBase
    {
        protected override void OnShow(IUIData data)
        {
            if (Services == null) return;
            SetTextKey("Txt_Title", "UI.Lobby.Records");
            SetTextKey("Txt_Back", "Common.BackLobby");
            SetText("Txt_HeaderTime", "时间");
            SetText("Txt_HeaderKills", "击杀");
            SetText("Txt_HeaderReason", "结束原因");
            OnButtonClick("Btn_Back", () => Replace(PageIds.Page_Lobby));
            SetVisible("Txt_Empty", false);                    // 查询返回前不显空态
            LoadAsync().ForgetLogged("Page_Records");
        }

        private async UniTask LoadAsync()
        {
            IMetaSession meta = Meta;
            if (!meta.IsConfigured)
            {
                Empty();
                return;
            }

            int generation = Form.DisplayGeneration;
            MetaResult<IReadOnlyList<MetaRecordRow>> result;
            try { result = await meta.ListRecordsAsync(Form.DisplayToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Error("战绩查询失败:" + ex.Message, LogTag.Meta);
                result = MetaResult<IReadOnlyList<MetaRecordRow>>.Fail("Internal");
            }
            if (!Form.IsDisplayCurrent(generation)) return;

            int count = result.Ok && result.Value != null ? result.Value.Count : 0;
            if (count == 0)
            {
                Empty();
                return;
            }
            SetVisible("Txt_Empty", false);
            Push(result.Value);
        }

        private void Push(IReadOnlyList<MetaRecordRow> rows)
        {
            var set = UIListRowSet.Create();
            for (int i = 0; i < rows.Count; i++)
            {
                MetaRecordRow row = rows[i];
                string reasonKey = "UI.Result.Reason." + row.Reason;   // 结束原因按枚举名拼键（设计 §8）
                set.Row(row.MatchId)
                    .Text("Txt_MatchTime", row.TimeText)
                    .KeyText("Txt_Kills", "UI.Result.Kills", row.Kills)
                    .KeyText("Txt_EndReason", reasonKey);
            }
            Index.SetList("VList_Records", set);
        }

        private void Empty()
        {
            SetVisible("Txt_Empty", true);
            SetTextKey("Txt_Empty", "UI.Records.Empty");
            Index.SetList("VList_Records", UIListRowSet.Create());
        }
    }
}
