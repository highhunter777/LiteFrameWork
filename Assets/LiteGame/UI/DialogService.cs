using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>弹窗结果（§4.3"返回确认/取消/关闭原因"）。</summary>
    public enum DialogResult
    {
        /// <summary>用户确认。</summary>
        Ok = 0,
        /// <summary>用户取消。</summary>
        Cancel,
        /// <summary>被外部关闭（导航 Back / Shutdown / 其他收口——非按钮路径）。</summary>
        Closed,
    }

    /// <summary>
    /// 弹窗服务（《UI框架总设计》§4.3"ShowDialogAsync 返回确认/取消/关闭原因，支持有界队列、
    /// 优先级、互斥组和 Scope 取消。重复断线/错误弹窗按键合并，不无限堆叠"）。
    ///
    /// 语义：
    /// - **互斥组**：同组（<see cref="Request.Group"/>，null = default）同时只展示一个；组空闲才出队
    ///   （展示中与打开在途都占组）。不同组可并行展示（各自是独立模态）。
    /// - **优先级**：同组出队按 <see cref="Request.Priority"/> 降序，同优先级 FIFO。
    /// - **合并**（"重复断线/错误弹窗不无限堆叠"）：同 <see cref="Request.MergeKey"/> 的在途请求
    ///   （排队或展示中）**共享同一结果任务**——后来的调用方并入等待，不产生新队列条目。
    /// - **有界队列**：排队数达 <see cref="QueueCapacity"/> 拒绝（类型化 Rejected——与导航同语义）。
    /// - **收口**：对话框被非按钮路径关闭（Back/Shutdown/其他 Close）→ 结果落 <see cref="DialogResult.Closed"/>
    ///   （经 <see cref="UIService.FormClosed"/> 单一漏斗）；等待者自身的 Scope 取消只解除本人等待
    ///   （<c>AttachExternalCancellation</c>），不影响对话框与其他等待者。
    ///
    /// 驱动：实现 <see cref="ITickable"/>（容器注册即自动驱动；测试直接泵 <see cref="Tick"/>）——
    /// 每 Tick 至多启动一个展示（多组并行时逐 Tick 启动，无实际差异）。
    /// </summary>
    public sealed class DialogService : ITickable, IDisposable
    {
        /// <summary>弹窗请求（不可变；Title/Message 必填，按钮文案可空 = 保持 prefab 原文案）。</summary>
        public sealed class Request
        {
            public readonly int FormId;
            public readonly string Title;
            public readonly string Message;
            public readonly string OkText;
            public readonly string CancelText;
            public readonly int Priority;
            public readonly string Group;
            public readonly string MergeKey;

            public Request(int formId, string title, string message,
                string okText = null, string cancelText = null,
                int priority = 0, string group = null, string mergeKey = null)
            {
                FormId = formId;
                Title = title;
                Message = message;
                OkText = okText;
                CancelText = cancelText;
                Priority = priority;
                Group = group ?? "(default)";
                MergeKey = mergeKey;
            }
        }

        private sealed class Entry
        {
            public readonly Request Request;
            public readonly UniTaskCompletionSource<DialogResult> Result = new UniTaskCompletionSource<DialogResult>();
            public readonly long Seq;                          // FIFO 平局判据（入队序）
            public UIDialog Dialog;                            // 展示成功后挂上（外部收口入口）
            public int FormId = -1;                            // 展示成功后登记（FormClosed 按它匹配）
            public bool ExternallyClosed;                      // 收口路径标记（区分 Closed 与 Cancel）

            public Entry(Request request, long seq) { Request = request; Seq = seq; }
        }

        private readonly UIService _ui;
        private readonly List<Entry> _queue = new List<Entry>(8);                        // 排队（跨组共用，出队按组+优先级筛选）
        private readonly Dictionary<string, Entry> _activeByGroup = new Dictionary<string, Entry>(2);  // 组 → 展示中条目
        private readonly HashSet<string> _openingGroups = new HashSet<string>(2);        // 打开在途占组（formId 未定时先占）
        private readonly Dictionary<string, Entry> _byMergeKey = new Dictionary<string, Entry>(4);      // 合并键 → 在途条目（排队或展示中）
        private readonly Dictionary<int, Entry> _activeByFormId = new Dictionary<int, Entry>(2);        // formId → 展示中条目（FormClosed 收口）
        private long _seq;

        /// <summary>排队容量（超出拒绝；§4.3"不无限堆叠"的硬边界）。</summary>
        public int QueueCapacity { get; }

        /// <summary>当前排队数（诊断/测试）。</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>当前展示中组数（诊断/测试）。</summary>
        public int ActiveCount => _activeByGroup.Count;

        /// <param name="queueCapacity">排队容量（默认 8——候选配置，目标设备验证后调整，同导航口径）。</param>
        public DialogService(UIService ui, int queueCapacity = 8)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            QueueCapacity = queueCapacity > 0 ? queueCapacity : 1;
            _ui.FormClosed += OnFormClosed;               // 与 UIService 同生命周期（同一容器装配）；Dispose 退订
        }

        /// <summary>
        /// 展示一个弹窗并等待结果（§4.3 ShowDialogAsync 口径）。合并、排队、拒绝规则见类注释；
        /// 等待者自身取消只解除本人（<see cref="DialogResult"/> 不会因单人取消而落定）。
        /// </summary>
        public UniTask<DialogResult> ShowAsync(Request request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ct.ThrowIfCancellationRequested();

            // 重复弹窗合并：同 MergeKey 在途（排队或展示中）→ 并入既有等待（不堆叠）
            if (request.MergeKey != null && _byMergeKey.TryGetValue(request.MergeKey, out Entry pending))
                return pending.Result.Task.AttachExternalCancellation(ct);

            if (_queue.Count >= QueueCapacity)
                throw new UIOpenException(UIOpenFailure.Rejected, request.FormId,
                    $"弹窗队列已满（{QueueCapacity}）——拒绝入队（§4.3 不无限堆叠）");

            var entry = new Entry(request, ++_seq);
            _queue.Add(entry);
            if (request.MergeKey != null) _byMergeKey[request.MergeKey] = entry;
            Pump();                                       // 入队即尝试出队（组空闲则立即展示）
            return entry.Result.Task.AttachExternalCancellation(ct);
        }

        /// <summary>队列泵：为每个空闲互斥组挑最高优先级条目启动展示（每 Tick 至多一个）。</summary>
        public void Tick(float deltaTime) => Pump();

        private void Pump()
        {
            Entry next = PickNext();
            if (next == null) return;
            _queue.Remove(next);
            _openingGroups.Add(next.Request.Group);       // 打开在途先占组（防同组重复展示）
            ShowCoreAsync(next).Forget();
        }

        /// <summary>出队选择：组空闲（无展示、无打开在途）条目中取优先级最高、同级 FIFO。</summary>
        private Entry PickNext()
        {
            Entry best = null;
            for (int i = 0; i < _queue.Count; i++)
            {
                Entry e = _queue[i];
                if (_activeByGroup.ContainsKey(e.Request.Group) || _openingGroups.Contains(e.Request.Group))
                    continue;
                if (best == null || e.Request.Priority > best.Request.Priority
                    || (e.Request.Priority == best.Request.Priority && e.Seq < best.Seq))
                    best = e;
            }
            return best;
        }

        private async UniTaskVoid ShowCoreAsync(Entry entry)
        {
            string group = entry.Request.Group;
            try
            {
                // 打开（加载/实例化/初始化可能失败或被权威取消——全部落为该条目的失败/取消）
                UIForm form = await _ui.ShowAsync(entry.Request.FormId, null);
                _openingGroups.Remove(group);

                UIDialog dialog = form.Root.GetComponentInChildren<UIDialog>(true);
                if (dialog == null)
                    throw new UIOpenException(UIOpenFailure.InitFailed, entry.Request.FormId,
                        $"弹窗表单[{entry.Request.FormId}] 缺少 UIDialog 组件——表单装配错误");

                entry.Dialog = dialog;
                entry.FormId = form.Id;
                _activeByGroup[group] = entry;            // 展示中占组（先登记再武装等待——收口事件可达）
                _activeByFormId[form.Id] = entry;

                bool ok = await dialog.WaitAsync(entry.Request.Title, entry.Request.Message,
                    entry.Request.OkText, entry.Request.CancelText);

                DialogResult result = entry.ExternallyClosed ? DialogResult.Closed
                    : ok ? DialogResult.Ok : DialogResult.Cancel;

                await CloseOwnForm(entry);                // **先关后交付**：任务完成时弹窗已离场、组已释放
                entry.Result.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                Release(entry);                           // 打开被权威取消（Close 在途/Shutdown）——等待者收取消
                entry.Result.TrySetCanceled();
            }
            catch (Exception ex)
            {
                Release(entry);                           // 类型化失败（LoadFailed/InitFailed 等）原样交付
                entry.Result.TrySetException(ex);
            }
            finally
            {
                Release(entry);                           // 幂等兜底（正常路径已在交付前释放）
            }
        }

        /// <summary>收口关闭自己的表单：先摘收口登记（自己的 Close 不触发 SettleExternally——
        /// 否则结果会被误标 Closed），再关（离场转场经 UIService 管线）。外部已关则跳过。</summary>
        private async UniTask CloseOwnForm(Entry entry)
        {
            _activeByFormId.Remove(entry.FormId);
            if (entry.FormId >= 0 && _ui.IsOpen(entry.FormId))
                await _ui.CloseAsync(entry.FormId, UIService.CloseReason.User);
        }

        /// <summary>释放组占用与登记（幂等；FormId 清 -1 = 收口登记已摘）。</summary>
        private void Release(Entry entry)
        {
            _activeByGroup.Remove(entry.Request.Group);
            _openingGroups.Remove(entry.Request.Group);
            if (entry.FormId >= 0) _activeByFormId.Remove(entry.FormId);
            entry.FormId = -1;
            if (entry.Request.MergeKey != null && _byMergeKey.TryGetValue(entry.Request.MergeKey, out Entry mapped)
                && ReferenceEquals(mapped, entry))
                _byMergeKey.Remove(entry.Request.MergeKey);
        }

        /// <summary>外部关闭收口（<see cref="UIService.FormClosed"/> 单一漏斗）：展示中条目落 Closed。</summary>
        private void OnFormClosed(int formId)
        {
            if (!_activeByFormId.TryGetValue(formId, out Entry entry)) return;
            entry.ExternallyClosed = true;                // 先标记再收口——ShowCoreAsync 据此区分 Closed/Cancel
            entry.Dialog?.SettleExternally();
        }

        /// <summary>诊断：组内当前展示的对话框实例（测试/DevHUD；无展示返回 false）。</summary>
        public bool TryGetActiveDialog(string group, out UIDialog dialog)
        {
            dialog = _activeByGroup.TryGetValue(group ?? "(default)", out Entry e) ? e.Dialog : null;
            return dialog != null;
        }

        public void Dispose()
        {
            _ui.FormClosed -= OnFormClosed;
        }
    }
}
