using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LiteGame.Editor.UITools.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiteGame.Editor.UITools.Preview
{
    /// <summary>
    /// 截图矩阵执行器（P2-3 B3 / AC3）：编辑器 Play 内逐组合应用设备档/语言/字体缩放→稳定帧→ ScreenCapture→回写 matrix.json。
    /// </summary>
    [InitializeOnLoad]
    public static class UIPreviewMatrixRunner
    {
        private const string StageWaitingResolution = "WaitingResolution";
        private const string StageWaitingForPlay = "WaitingForPlay";
        private const string StageWaitingApply = "WaitingApply";
        private const string StageWaitingSettle = "WaitingSettle";
        private const string StageWaitingCapture = "WaitingCapture";
        private const string StageFinishing = "Finishing";

        private static UIPreviewMatrixJobState _job;

        static UIPreviewMatrixRunner()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _job = UIPreviewMatrixJobStore.Load();
        }

        /// <summary>当前任务状态（只读；窗口/eval 观测用）。</summary>
        public static string CurrentStatus
        {
            get
            {
                UIPreviewMatrixJobState job = _job ?? UIPreviewMatrixJobStore.Load();
                if (job == null) return "idle";
                return $"{job.Status} {job.Cursor}/{job.Combos.Count} {job.PageName}";
            }
        }

        public static bool IsRunning
        {
            get
            {
                UIPreviewMatrixJobState job = _job ?? UIPreviewMatrixJobStore.Load();
                return job != null && string.Equals(job.Status, "Running", StringComparison.Ordinal);
            }
        }

        /// <summary>启动矩阵；返回可读结果（启动失败时说明原因）。</summary>
        public static string Start(UIPreviewMatrixOptions options)
        {
            if (IsRunning) return "已有矩阵任务运行中";
            if (EditorApplication.isPlaying) return "请先退出 Play 再运行矩阵";
            if (!UIPreviewMatrixPlanner.TryCreatePlan(options, out UIPreviewMatrixPlan plan, out string error))
                return error;

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                    return "当前场景有未保存修改（" + scene.name + "）——先保存再运行矩阵（矩阵不弹对话框）";
            }
            string originalScene = SceneManager.GetActiveScene().path;
            if (!File.Exists(Path.Combine(UIValidator.ProjectRoot, UIPagePreview.ScenePath)))
                return "预览场景不存在：" + UIPagePreview.ScenePath;

            UIPreviewGameView.TryGetSize(out int originalWidth, out int originalHeight);
            var job = new UIPreviewMatrixJobState
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
            UIPreviewMatrixJobStore.Persist(job);
            SessionState.SetInt(UIPreviewDriver.OptionsRevisionKey, 0);
            SessionState.SetInt(UIPreviewDriver.OptionsAppliedKey, 0);
            SessionState.SetBool(UIPreviewDriver.HideOverlayKey, true);
            SessionState.SetString(UIPreviewDriver.LocaleStatusKey, "pending");
            SessionState.SetString(UIPreviewDriver.LocaleDataDirKey, options?.LocaleDataDir ?? string.Empty);
            try
            {
                EditorPrefs.SetString(UIPreviewDriver.TargetPrefabKey, plan.PagePath);
                EditorSceneManager.OpenScene(UIPagePreview.ScenePath, OpenSceneMode.Single);
                SetResolution(job, 0);
                WriteOptions(job, 0);
                EditorApplication.isPlaying = true;
                Debug.Log($"[UIPreviewMatrix] 启动：{plan.PageName}，{job.Combos.Count} 组合 → {plan.OutputDir}");
                return $"矩阵已启动：{plan.PageName}，{job.Combos.Count} 组合 → {plan.OutputDir}";
            }
            catch (Exception exception)
            {
                job.Status = "Failed";
                job.Stage = StageFinishing;
                job.Error = exception.GetType().Name + ": " + exception.Message;
                UIPreviewMatrixJobStore.Persist(job);
                Debug.LogError("[UIPreviewMatrix] 启动失败：" + exception);
                return "矩阵启动失败：" + exception.Message;
            }
        }

        /// <summary>中断当前任务（保留已完成组合并写索引）。</summary>
        public static void Cancel()
        {
            UIPreviewMatrixJobState job = _job ?? UIPreviewMatrixJobStore.Load();
            if (job == null || !string.Equals(job.Status, "Running", StringComparison.Ordinal)) return;
            job.Status = "Interrupted";
            job.Stage = StageFinishing;
            UIPreviewMatrixJobStore.Persist(job);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void Tick()
        {
            UIPreviewMatrixJobState job = _job ?? UIPreviewMatrixJobStore.Load();
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
                    UIPreviewMatrixJobStore.Persist(job);
                }
                return;
            }

            if (string.Equals(job.Stage, StageWaitingForPlay, StringComparison.Ordinal))
            {
                job.Stage = StageWaitingApply;
                job.Deadline = EditorApplication.timeSinceStartup + 10d;
                UIPreviewMatrixJobStore.Persist(job);
                return;
            }

            if (string.Equals(job.Stage, StageWaitingApply, StringComparison.Ordinal))
            {
                if (SessionState.GetInt(UIPreviewDriver.OptionsAppliedKey, -1) >= job.Revision)
                {
                    UIPreviewMatrixComboState combo = job.Combos[job.Cursor];
                    if (UIPreviewMatrix.IsLocaleUnavailable(combo.Locale,
                            SessionState.GetString(UIPreviewDriver.LocaleStatusKey, string.Empty)))
                    {
                        const string reason = "语言表直载失败——语言维度降级（作者文本）";
                        if (!job.Degraded.Contains(reason)) job.Degraded.Add(reason);
                        FailCurrent(job, reason);
                        Advance(job);
                        return;
                    }
                    job.Stage = StageWaitingSettle;
                    job.Deadline = EditorApplication.timeSinceStartup + 0.4d;
                    UIPreviewMatrixJobStore.Persist(job);
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
                string path = UIPreviewMatrixJobStore.ScreenshotPath(job, job.Combos[job.Cursor].FileName);
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
                UIPreviewMatrixJobStore.Persist(job);
            }
        }

        private static void Capture(UIPreviewMatrixJobState job)
        {
            UIPreviewMatrixComboState combo = job.Combos[job.Cursor];
            string directory = UIPreviewMatrixJobStore.FullPath(job.OutputDir);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, combo.FileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                job.Stage = StageWaitingCapture;
                job.Deadline = EditorApplication.timeSinceStartup + 3d;
                UIPreviewMatrixJobStore.Persist(job);
            }
            catch (Exception exception)
            {
                FailCurrent(job, "截图异常：" + exception.Message);
                Advance(job);
            }
        }

        private static void Advance(UIPreviewMatrixJobState job)
        {
            UIPreviewMatrixComboState completed = job.Combos[job.Cursor];
            completed.Captured = string.IsNullOrEmpty(completed.Failure);
            job.Cursor++;
            if (job.Cursor >= job.Combos.Count)
            {
                job.Status = job.Combos.Any(combo => !string.IsNullOrEmpty(combo.Failure))
                    ? "CompletedWithFailures"
                    : "Completed";
                job.Stage = StageFinishing;
                UIPreviewMatrixJobStore.Persist(job);
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }

            UIPreviewMatrixComboState next = job.Combos[job.Cursor];
            bool profileChanged = !string.Equals(completed.ProfileId, next.ProfileId, StringComparison.Ordinal);
            if (profileChanged)
            {
                SetResolution(job, job.Cursor);
                job.Stage = StageWaitingResolution;
                job.Deadline = EditorApplication.timeSinceStartup + 0.35d;
                UIPreviewMatrixJobStore.Persist(job);
                return;
            }

            next.ResolutionApplied = completed.ResolutionApplied;
            WriteOptions(job, job.Cursor);
            job.Stage = StageWaitingApply;
            job.Deadline = EditorApplication.timeSinceStartup + 10d;
            UIPreviewMatrixJobStore.Persist(job);
        }

        private static void FailCurrent(UIPreviewMatrixJobState job, string reason)
        {
            job.Combos[job.Cursor].Failure = reason;
            job.Combos[job.Cursor].Captured = false;
        }

        private static void SetResolution(UIPreviewMatrixJobState job, int index)
        {
            UIPreviewMatrixComboState combo = job.Combos[index];
            combo.ResolutionApplied = UIPreviewGameView.TrySetSize(combo.Width, combo.Height);
            if (!combo.ResolutionApplied)
            {
                const string reason = "GameView 分辨率切换失败——分辨率维度降级（安全区/语言照跑）";
                if (!job.Degraded.Contains(reason)) job.Degraded.Add(reason);
            }
        }

        private static void WriteOptions(UIPreviewMatrixJobState job, int index)
        {
            UIPreviewMatrixComboState combo = job.Combos[index];
            SessionState.SetString(UIPreviewDriver.DeviceProfileIdKey, combo.ProfileId);
            SessionState.SetInt(UIPreviewDriver.DeviceWidthKey, combo.Width);
            SessionState.SetInt(UIPreviewDriver.DeviceHeightKey, combo.Height);
            SessionState.SetFloat(UIPreviewDriver.InsetLeftKey, combo.InsetLeft);
            SessionState.SetFloat(UIPreviewDriver.InsetTopKey, combo.InsetTop);
            SessionState.SetFloat(UIPreviewDriver.InsetRightKey, combo.InsetRight);
            SessionState.SetFloat(UIPreviewDriver.InsetBottomKey, combo.InsetBottom);
            SessionState.SetString(UIPreviewDriver.LocaleKey, combo.Locale);
            SessionState.SetString(UIPreviewDriver.LocaleStatusKey,
                string.IsNullOrEmpty(combo.Locale) ? "off" : "pending");
            SessionState.SetFloat(UIPreviewDriver.FontScaleKey, combo.FontScale);
            job.Revision++;
            SessionState.SetInt(UIPreviewDriver.OptionsRevisionKey, job.Revision);
            UIPreviewMatrixJobStore.Persist(job);
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            UIPreviewMatrixJobState job = _job ?? UIPreviewMatrixJobStore.Load();
            if (job == null) return;
            if (string.Equals(job.Status, "Running", StringComparison.Ordinal))
                job.Status = "Interrupted";
            job.Stage = StageFinishing;
            UIPreviewMatrixJobStore.Persist(job);
        }

        private static void Finalize(UIPreviewMatrixJobState job)
        {
            try
            {
                var run = new UIPreviewMatrixRun(job.PageName, job.Combos.Select(UIPreviewMatrixJobStore.ToCombo).ToList());
                foreach (UIPreviewMatrixComboState comboState in job.Combos)
                {
                    UIPreviewCombo combo = UIPreviewMatrixJobStore.ToCombo(comboState);
                    if (comboState.Captured) run.RecordSuccess(combo, comboState.ResolutionApplied);
                    else if (!string.IsNullOrEmpty(comboState.Failure))
                        run.RecordFailure(combo, comboState.Failure, comboState.ResolutionApplied);
                }
                foreach (string degraded in job.Degraded) run.MarkDegraded(degraded);
                if (string.Equals(job.Status, "Interrupted", StringComparison.Ordinal)) run.MarkInterrupted();
                if (string.Equals(job.Status, "Failed", StringComparison.Ordinal)) run.MarkFailed();

                string directory = UIPreviewMatrixJobStore.FullPath(job.OutputDir);
                Directory.CreateDirectory(directory);
                string index = run.BuildIndex();
                File.WriteAllText(Path.Combine(directory, "matrix.json"), index, new UTF8Encoding(false));
                Debug.Log($"[UIPreviewMatrix] {job.Status}：{job.PageName} {run.Succeeded.Count} 成功 / " +
                          $"{run.Failed.Count} 失败 → {Path.Combine(directory, "matrix.json")}");
            }
            catch (Exception exception)
            {
                Debug.LogError("[UIPreviewMatrix] 收尾失败：" + exception);
            }
            finally
            {
                SessionState.SetBool(UIPreviewDriver.HideOverlayKey, false);
                UIPreviewMatrixJobStore.Clear();
                _job = null;
                if (job.OriginalGameViewWidth > 0 && job.OriginalGameViewHeight > 0)
                    UIPreviewGameView.TrySetSize(job.OriginalGameViewWidth, job.OriginalGameViewHeight);
                if (!string.IsNullOrEmpty(job.OriginalScene)
                    && File.Exists(Path.Combine(UIValidator.ProjectRoot, job.OriginalScene))
                    && !string.Equals(SceneManager.GetActiveScene().path, job.OriginalScene, StringComparison.Ordinal))
                {
                    EditorSceneManager.OpenScene(job.OriginalScene, OpenSceneMode.Single);
                }
            }
        }

        private static UIPreviewMatrixComboState ToState(UIPreviewMatrixPlan plan, UIPreviewCombo combo)
        {
            UIDeviceProfile profile = plan.Profiles.First(item =>
                string.Equals(item.Id, combo.ProfileId, StringComparison.Ordinal));
            return new UIPreviewMatrixComboState
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
                FileName = UIPreviewMatrix.FileName(combo),
            };
        }
    }
}
