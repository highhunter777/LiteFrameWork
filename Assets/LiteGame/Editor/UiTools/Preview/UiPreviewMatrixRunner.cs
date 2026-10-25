using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteGame.Editor.UiTools.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Editor.UiTools.Preview
{
    /// <summary>
    /// 截图矩阵执行器（P2-3 B3 / AC3）：编辑器 Play 内逐组合应用设备档/语言/字体缩放→稳定帧→
    /// ScreenCapture→回写 matrix.json。Job 状态存 SessionState，跨域重载可恢复；中断安全；
    /// 单组合失败只记失败继续。产物固定落 `Artifacts/`（.gitignore 已排除）。
    /// 输入解析归 <see cref="UiPreviewMatrixPlanner"/>，状态持久化归 <see cref="UiPreviewMatrixJobStore"/>。
    /// </summary>
    [InitializeOnLoad]
    public static class UiPreviewMatrixRunner
    {
        private const string StageWaitingResolution = "WaitingResolution";
        private const string StageWaitingForPlay = "WaitingForPlay";
        private const string StageWaitingApply = "WaitingApply";
        private const string StageWaitingSettle = "WaitingSettle";
        private const string StageWaitingCapture = "WaitingCapture";
        private const string StageFinishing = "Finishing";

        private static UiPreviewMatrixJobState _job;

        static UiPreviewMatrixRunner()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _job = UiPreviewMatrixJobStore.Load();
        }

        /// <summary>当前任务状态（只读；窗口/eval 观测用）。</summary>
        public static string CurrentStatus
        {
            get
            {
                UiPreviewMatrixJobState job = _job ?? UiPreviewMatrixJobStore.Load();
                if (job == null) return "idle";
                return $"{job.Status} {job.Cursor}/{job.Combos.Count} {job.PageName}";
            }
        }

        public static bool IsRunning
        {
            get
            {
                UiPreviewMatrixJobState job = _job ?? UiPreviewMatrixJobStore.Load();
                return job != null && string.Equals(job.Status, "Running", StringComparison.Ordinal);
            }
        }

        /// <summary>启动矩阵；返回可读结果（启动失败时说明原因）。</summary>
        public static string Start(UiPreviewMatrixOptions options)
        {
            if (IsRunning) return "已有矩阵任务运行中";
            if (EditorApplication.isPlaying) return "请先退出 Play 再运行矩阵";
            if (!UiPreviewMatrixPlanner.TryCreatePlan(options, out UiPreviewMatrixPlan plan, out string error))
                return error;

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                    return "当前场景有未保存修改（" + scene.name + "）——先保存再运行矩阵（矩阵不弹对话框）";
            }
            string originalScene = SceneManager.GetActiveScene().path;
            if (!File.Exists(Path.Combine(UiValidator.ProjectRoot, UiPagePreview.ScenePath)))
                return "预览场景不存在：" + UiPagePreview.ScenePath;

            UiPreviewGameView.TryGetSize(out int originalWidth, out int originalHeight);
            var job = new UiPreviewMatrixJobState
            {
                OriginalGameViewWidth = originalWidth,
                OriginalGameViewHeight = originalHeight,
                PagePath = plan.PagePath,
                PageName = plan.PageName,
                OutputDir = plan.OutputDir,
                OriginalScene = originalScene,
                Status = "Running",
                Stage = StageWaitingForPlay,
                Cursor = 0,
                Revision = 0,
                Deadline = EditorApplication.timeSinceStartup + 15d,
                StartedAt = DateTime.UtcNow.ToString("o"),
                Combos = plan.Combos.Select(combo => ToState(plan, combo)).ToList(),
            };

            _job = job;
            UiPreviewMatrixJobStore.Persist(job);
            SessionState.SetInt(UiPreviewDriver.OptionsRevisionKey, 0);
            SessionState.SetInt(UiPreviewDriver.OptionsAppliedKey, 0);
            SessionState.SetBool(UiPreviewDriver.HideOverlayKey, true);
            SessionState.SetString(UiPreviewDriver.LocaleStatusKey, "pending");
            SessionState.SetString(UiPreviewDriver.LocaleDataDirKey, options?.LocaleDataDir ?? string.Empty);
            try
            {
                EditorPrefs.SetString(UiPreviewDriver.TargetPrefabKey, plan.PagePath);
                EditorSceneManager.OpenScene(UiPagePreview.ScenePath, OpenSceneMode.Single);
                SetResolution(job, 0);
                WriteOptions(job, 0);
                EditorApplication.isPlaying = true;
                Debug.Log($"[UiPreviewMatrix] 启动：{plan.PageName}，{job.Combos.Count} 组合 → {plan.OutputDir}");
                return $"矩阵已启动：{plan.PageName}，{job.Combos.Count} 组合 → {plan.OutputDir}";
            }
            catch (Exception exception)
            {
                job.Status = "Failed";
                job.Stage = StageFinishing;
                job.Error = exception.GetType().Name + ": " + exception.Message;
                UiPreviewMatrixJobStore.Persist(job);
                Debug.LogError("[UiPreviewMatrix] 启动失败：" + exception);
                return "矩阵启动失败：" + exception.Message;
            }
        }

        /// <summary>中断当前任务（保留已完成组合并写索引）。</summary>
        public static void Cancel()
        {
            UiPreviewMatrixJobState job = _job ?? UiPreviewMatrixJobStore.Load();
            if (job == null || !string.Equals(job.Status, "Running", StringComparison.Ordinal)) return;
            job.Status = "Interrupted";
            job.Stage = StageFinishing;
            UiPreviewMatrixJobStore.Persist(job);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void Tick()
        {
            UiPreviewMatrixJobState job = _job ?? UiPreviewMatrixJobStore.Load();
            if (job == null) return;
            _job = job;

            if (string.Equals(job.Stage, StageFinishing, StringComparison.Ordinal))
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                else Finalize(job);
                return;
            }
            if (!string.Equals(job.Status, "Running", StringComparison.Ordinal)) return;

            if (!EditorApplication.isPlaying)
            {
                if (string.Equals(job.Stage, StageWaitingForPlay, StringComparison.Ordinal)
                    && EditorApplication.timeSinceStartup > job.Deadline)
                {
                    job.Status = "Failed";
                    job.Error = "Play 模式未在 15s 内启动";
                    job.Stage = StageFinishing;
                    UiPreviewMatrixJobStore.Persist(job);
                }
                return;
            }

            if (string.Equals(job.Stage, StageWaitingForPlay, StringComparison.Ordinal))
            {
                job.Stage = StageWaitingApply;
                job.Deadline = EditorApplication.timeSinceStartup + 10d;
                UiPreviewMatrixJobStore.Persist(job);
                return;
            }

            if (string.Equals(job.Stage, StageWaitingApply, StringComparison.Ordinal))
            {
                if (SessionState.GetInt(UiPreviewDriver.OptionsAppliedKey, -1) >= job.Revision)
                {
                    UiPreviewMatrixComboState combo = job.Combos[job.Cursor];
                    if (UiPreviewMatrix.IsLocaleUnavailable(combo.Locale,
                            SessionState.GetString(UiPreviewDriver.LocaleStatusKey, string.Empty)))
                    {
                        const string reason = "语言表直载失败——语言维度降级（作者文本）";
                        if (!job.Degraded.Contains(reason)) job.Degraded.Add(reason);
                        FailCurrent(job, reason);
                        Advance(job);
                        return;
                    }
                    job.Stage = StageWaitingSettle;
                    job.Deadline = EditorApplication.timeSinceStartup + 0.4d;
                    UiPreviewMatrixJobStore.Persist(job);
                }
                else if (EditorApplication.timeSinceStartup > job.Deadline)
                {
                    FailCurrent(job, "驱动未在 10s 内应用预览参数");
                    Advance(job);
                }
                return;
            }

            if (string.Equals(job.Stage, StageWaitingSettle, StringComparison.Ordinal))
            {
                if (EditorApplication.timeSinceStartup >= job.Deadline) Capture(job);
                return;
            }

            if (string.Equals(job.Stage, StageWaitingCapture, StringComparison.Ordinal))
            {
                string path = UiPreviewMatrixJobStore.ScreenshotPath(job, job.Combos[job.Cursor].FileName);
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    Advance(job);
                }
                else if (EditorApplication.timeSinceStartup > job.Deadline)
                {
                    FailCurrent(job, "截图未生成或为空文件");
                    Advance(job);
                }
                return;
            }

            if (string.Equals(job.Stage, StageWaitingResolution, StringComparison.Ordinal)
                && EditorApplication.timeSinceStartup >= job.Deadline)
            {
                WriteOptions(job, job.Cursor);
                job.Stage = StageWaitingApply;
                job.Deadline = EditorApplication.timeSinceStartup + 10d;
                UiPreviewMatrixJobStore.Persist(job);
            }
        }

        private static void Capture(UiPreviewMatrixJobState job)
        {
            UiPreviewMatrixComboState combo = job.Combos[job.Cursor];
            string directory = UiPreviewMatrixJobStore.FullPath(job.OutputDir);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, combo.FileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                job.Stage = StageWaitingCapture;
                job.Deadline = EditorApplication.timeSinceStartup + 3d;
                UiPreviewMatrixJobStore.Persist(job);
            }
            catch (Exception exception)
            {
                FailCurrent(job, "截图异常：" + exception.Message);
                Advance(job);
            }
        }

        private static void Advance(UiPreviewMatrixJobState job)
        {
            UiPreviewMatrixComboState completed = job.Combos[job.Cursor];
            completed.Captured = string.IsNullOrEmpty(completed.Failure);
            job.Cursor++;
            if (job.Cursor >= job.Combos.Count)
            {
                job.Status = job.Combos.Any(combo => !string.IsNullOrEmpty(combo.Failure))
                    ? "CompletedWithFailures"
                    : "Completed";
                job.Stage = StageFinishing;
                UiPreviewMatrixJobStore.Persist(job);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }

            UiPreviewMatrixComboState next = job.Combos[job.Cursor];
            bool profileChanged = !string.Equals(completed.ProfileId, next.ProfileId, StringComparison.Ordinal);
            if (profileChanged)
            {
                SetResolution(job, job.Cursor);
                job.Stage = StageWaitingResolution;
                job.Deadline = EditorApplication.timeSinceStartup + 0.35d;
                UiPreviewMatrixJobStore.Persist(job);
                return;
            }

            next.ResolutionApplied = completed.ResolutionApplied;
            WriteOptions(job, job.Cursor);
            job.Stage = StageWaitingApply;
            job.Deadline = EditorApplication.timeSinceStartup + 10d;
            UiPreviewMatrixJobStore.Persist(job);
        }

        private static void FailCurrent(UiPreviewMatrixJobState job, string reason)
        {
            job.Combos[job.Cursor].Failure = reason;
            job.Combos[job.Cursor].Captured = false;
        }

        private static void SetResolution(UiPreviewMatrixJobState job, int index)
        {
            UiPreviewMatrixComboState combo = job.Combos[index];
            combo.ResolutionApplied = UiPreviewGameView.TrySetSize(combo.Width, combo.Height);
            if (!combo.ResolutionApplied)
            {
                const string reason = "GameView 分辨率切换失败——分辨率维度降级（安全区/语言照跑）";
                if (!job.Degraded.Contains(reason)) job.Degraded.Add(reason);
            }
        }

        private static void WriteOptions(UiPreviewMatrixJobState job, int index)
        {
            UiPreviewMatrixComboState combo = job.Combos[index];
            SessionState.SetString(UiPreviewDriver.DeviceProfileIdKey, combo.ProfileId);
            SessionState.SetInt(UiPreviewDriver.DeviceWidthKey, combo.Width);
            SessionState.SetInt(UiPreviewDriver.DeviceHeightKey, combo.Height);
            SessionState.SetFloat(UiPreviewDriver.InsetLeftKey, combo.InsetLeft);
            SessionState.SetFloat(UiPreviewDriver.InsetTopKey, combo.InsetTop);
            SessionState.SetFloat(UiPreviewDriver.InsetRightKey, combo.InsetRight);
            SessionState.SetFloat(UiPreviewDriver.InsetBottomKey, combo.InsetBottom);
            SessionState.SetString(UiPreviewDriver.LocaleKey, combo.Locale);
            SessionState.SetString(UiPreviewDriver.LocaleStatusKey,
                string.IsNullOrEmpty(combo.Locale) ? "off" : "pending");
            SessionState.SetFloat(UiPreviewDriver.FontScaleKey, combo.FontScale);
            job.Revision++;
            SessionState.SetInt(UiPreviewDriver.OptionsRevisionKey, job.Revision);
            UiPreviewMatrixJobStore.Persist(job);
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            UiPreviewMatrixJobState job = _job ?? UiPreviewMatrixJobStore.Load();
            if (job == null) return;
            if (string.Equals(job.Status, "Running", StringComparison.Ordinal))
                job.Status = "Interrupted";
            job.Stage = StageFinishing;
            UiPreviewMatrixJobStore.Persist(job);
        }

        private static void Finalize(UiPreviewMatrixJobState job)
        {
            try
            {
                var run = new UiPreviewMatrixRun(job.PageName, job.Combos.Select(UiPreviewMatrixJobStore.ToCombo).ToList());
                foreach (UiPreviewMatrixComboState comboState in job.Combos)
                {
                    UiPreviewCombo combo = UiPreviewMatrixJobStore.ToCombo(comboState);
                    if (comboState.Captured) run.RecordSuccess(combo, comboState.ResolutionApplied);
                    else if (!string.IsNullOrEmpty(comboState.Failure))
                        run.RecordFailure(combo, comboState.Failure, comboState.ResolutionApplied);
                }
                foreach (string degraded in job.Degraded) run.MarkDegraded(degraded);
                if (string.Equals(job.Status, "Interrupted", StringComparison.Ordinal)) run.MarkInterrupted();
                if (string.Equals(job.Status, "Failed", StringComparison.Ordinal)) run.MarkFailed();

                string directory = UiPreviewMatrixJobStore.FullPath(job.OutputDir);
                Directory.CreateDirectory(directory);
                string index = run.BuildIndex();
                File.WriteAllText(Path.Combine(directory, "matrix.json"), index, new UTF8Encoding(false));
                Debug.Log($"[UiPreviewMatrix] {job.Status}：{job.PageName} {run.Succeeded.Count} 成功 / " +
                          $"{run.Failed.Count} 失败 → {Path.Combine(directory, "matrix.json")}");
            }
            catch (Exception exception)
            {
                Debug.LogError("[UiPreviewMatrix] 收尾失败：" + exception);
            }
            finally
            {
                SessionState.SetBool(UiPreviewDriver.HideOverlayKey, false);
                UiPreviewMatrixJobStore.Clear();
                _job = null;
                if (job.OriginalGameViewWidth > 0 && job.OriginalGameViewHeight > 0)
                    UiPreviewGameView.TrySetSize(job.OriginalGameViewWidth, job.OriginalGameViewHeight);
                if (!string.IsNullOrEmpty(job.OriginalScene)
                    && File.Exists(Path.Combine(UiValidator.ProjectRoot, job.OriginalScene))
                    && !string.Equals(SceneManager.GetActiveScene().path, job.OriginalScene, StringComparison.Ordinal))
                {
                    EditorSceneManager.OpenScene(job.OriginalScene, OpenSceneMode.Single);
                }
            }
        }

        private static UiPreviewMatrixComboState ToState(UiPreviewMatrixPlan plan, UiPreviewCombo combo)
        {
            UiDeviceProfile profile = plan.Profiles.First(item =>
                string.Equals(item.Id, combo.ProfileId, StringComparison.Ordinal));
            return new UiPreviewMatrixComboState
            {
                ProfileId = combo.ProfileId,
                Width = profile.Width,
                Height = profile.Height,
                InsetLeft = profile.SafeInsets?.Left ?? 0f,
                InsetTop = profile.SafeInsets?.Top ?? 0f,
                InsetRight = profile.SafeInsets?.Right ?? 0f,
                InsetBottom = profile.SafeInsets?.Bottom ?? 0f,
                Locale = combo.Locale,
                FontScale = combo.FontScale,
                LongText = combo.LongText,
                FileName = UiPreviewMatrix.FileName(combo),
            };
        }
    }
}
