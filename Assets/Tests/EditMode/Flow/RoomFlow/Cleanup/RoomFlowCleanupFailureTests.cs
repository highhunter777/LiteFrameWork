using System.Collections;
using Cysharp.Threading.Tasks;
using LiteFramework.UI;
using LiteGame.Modules.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine.TestTools;
using LiteGame.Modules.Meta.Abstractions.Session;

namespace LiteGame.Tests.EditMode
{
    /// <summary>清理失败停机态：必要卸载抛异常 → 保留占用与场景、retryable=false 反馈、不导航。</summary>
    public sealed class RoomFlowCleanupFailureTests : LiteTesting.Unity.UnityTestBase
    {
        [UnityTest]
        public IEnumerator 卸载抛异常_保留占用不报清理完成_错误反馈展示_不导航_再次入场被拒()
        {
            var harness = new RoomFlowFeedbackHarness(Scope);
            var scenes = new ThrowingUnloadSceneService();
            harness.Register(PageIds.Page_Lobby, 1, harness.NewPlainPage("lobby"));
            harness.Register(PageIds.Page_Result, 1, harness.NewPlainPage("result"));
            var rig = new RoomFlowRig(feedback: harness.Feedback, pages: harness.Pages, scenes: scenes);
            int recoveryCalls = 0;
            rig.Lifetime.RecoveryStep = _ =>
            {
                recoveryCalls++;
                return UniTask.CompletedTask;
            };
            try
            {
                rig.Login();
                Assert.IsTrue(rig.Session.QuickEnterAsync().GetAwaiter().GetResult().Ok, "取票成功（前置）");
                rig.Machine.Advance();
                ControlledMatchSession account = rig.Accounts.Last;
                Assert.IsNotNull(account, "Match 已建连（前置）");
                account.EmitJoined();
                rig.Machine.Advance();
                for (int i = 0; i < 200 && scenes.LoadCalls < 2; i++)
                {
                    harness.Pump();
                    yield return null;
                }

                Assert.AreEqual(ProcedureId.Battle, rig.Machine.Current, "已进入 Battle（前置）");
                Assert.AreEqual(1, rig.Runs.CreateCount, "Battle 已接管会话（前置）");
                Assert.GreaterOrEqual(scenes.LoadCalls, 2, "本局场景组已经 ISceneService 加载（前置）");
                Assert.IsTrue(scenes.IsLoaded(ControlledMapProvider.MapLocation), "地图场景已加载（前置）");
                RoomFlowOperation operation = rig.Lifetime.Current;
                Assert.IsNotNull(operation, "操作句柄仍在（前置）");

                rig.Session.LeaveMatch();                  // owner 退出：操作失效 + 请求回 Main
                rig.Machine.Advance();
                for (int i = 0; i < 20 && scenes.UnloadCalls == 0; i++)
                {
                    harness.Pump();
                    yield return null;
                }
                for (int i = 0; i < 20 && !harness.TryGetErrorDialog(out _); i++)
                {
                    harness.Pump();
                    yield return null;
                }

                Assert.AreEqual(ProcedureId.Main, rig.Machine.Current, "阶段按 next 安全流转回 Main");
                Assert.AreEqual(0, rig.Error.Enters, "清理失败不得把整机钉在 ProcedureError");
                Assert.IsTrue(rig.Lifetime.IsBusy, "清理失败必须保留操作占用");
                Assert.IsTrue(operation.Invalidated, "owner 退出已使操作失效（前置）");
                Assert.IsFalse(operation.CleanupDone, "卸载失败不得报'已清理'放行");
                Assert.IsFalse(rig.Lifetime.Release(operation), "清理未完成时释放占用必须被拒绝");
                Assert.AreNotEqual(UniTaskStatus.Succeeded, operation.CleanupTask.Status,
                    "清理完成信号不得放行下一局");
                Assert.AreEqual(RoomFlowPhase.Cleaning, operation.Phase, "停在清理中（未 Cleaned）");
                Assert.AreEqual(1, scenes.UnloadCalls, "卸载确实已发起并失败");
                Assert.IsTrue(scenes.IsLoaded(ControlledMapProvider.MapLocation), "卸载失败时旧场景仍被保留");

                harness.Pump();
                Assert.IsTrue(harness.TryGetErrorDialog(out UIDialog dialog),
                    "清理失败必须展示错误反馈（owner 已退出也不得跳过）");
                Assert.AreEqual("离场清理失败", dialog.Title.text, "反馈标题指向离场清理失败");
                StringAssert.Contains("已保留房间占用", dialog.Message.text, "反馈说明保留房间占用");
                Assert.AreEqual("确认", dialog.OkButton.GetComponentInChildren<TMP_Text>(true).text,
                    "retryable=false 只给确认按钮");
                Assert.AreEqual(RoomFlowFeedbackHarness.PlaceholderCancel,
                    dialog.CancelButton.GetComponentInChildren<TMP_Text>(true).text,
                    "retryable=false 不写取消文案");

                Assert.GreaterOrEqual(harness.LoadsOf(RoomFlowFeedbackHarness.ErrorFormId), 1,
                    "打开记录有效：错误面确实经同一加载口（零值断言不是记录器失效）");
                Assert.AreEqual(0, recoveryCalls, "清理未完成不得进入恢复步骤（PageFlow 不被调用）");
                Assert.AreEqual(0, harness.LoadsOf(PageIds.Page_Lobby), "不得导航到大厅页");
                Assert.AreEqual(0, harness.LoadsOf(PageIds.Page_Result), "不得导航到结算页");
                Assert.IsFalse(harness.Ui.IsOpen(PageIds.Page_Lobby), "大厅页不得被打开");
                Assert.IsFalse(harness.Ui.IsOpen(PageIds.Page_Result), "结算页不得被打开");

                int tickets = rig.Client.TicketCalls;
                int attempts = rig.Accounts.CreateAttempts;
                MetaResult<string> again = rig.Session.QuickEnterAsync().GetAwaiter().GetResult();
                Assert.IsFalse(again.Ok, "占用未释放时再次入场必须被拒");
                Assert.AreEqual("room.flow-busy", again.Reason, "拒绝原因是稳定忙结果");
                Assert.AreEqual(tickets, rig.Client.TicketCalls, "被拒的入场不得取票");
                Assert.AreEqual(attempts, rig.Accounts.CreateAttempts, "被拒的入场不得建连接");
            }
            finally
            {
                harness.Dispose();
            }
        }
    }
}
