#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 工作台场景入口（dev-only，随工作台场景携带）：两种进入形态的收口——
    /// ①**直接 Play 本场景**（无宿主）：启用随场景携带的**禁用态 GameEntry**，走真装配链（开发目检最顺手的入口）；
    /// ②**被 Boot 加性加载**（`DebugTuner` 工作台模式）：宿主已在场，保持携带的 GameEntry 禁用——**不二次装配**
    /// （Boot 的 GameEntry 与服务都是 DontDestroyOnLoad，二次装配会起第二套宿主/UIRoot）。
    /// 与工作台面板同为 dev 件：整件随三宏剥离。
    /// </summary>
    public sealed class WorkbenchSceneEntry : MonoBehaviour
    {
        [Tooltip("随场景携带的引导件（**禁用态**）——无宿主时由本件启用；被加性加载时保持禁用")]
        [SerializeField] private GameObject _bundledEntry;

        [Tooltip("随场景携带的音频监听器（**禁用态**）——直接 Play 形态启用以供试听；被加性加载时保持禁用（用宿主的，避免「2 个 AudioListener」告警）")]
        [SerializeField] private AudioListener _bundledListener;

        private void Start()
        {
            if (UnityEngine.Object.FindAnyObjectByType<GameEntry>() != null) return;   // 宿主已在场（加性加载形态）
            if (_bundledEntry == null)
            {
                Log.Warning("工作台场景入口：未接线携带的 GameEntry（重跑 AgentScripts/BuildWorkbenchPanel.cs）", LogTag.Workbench);
                return;
            }
            _bundledEntry.SetActive(true);                                            // 直接 Play 形态：走真装配
            if (_bundledListener != null) _bundledListener.enabled = true;             // 音频监听器同批启用（加性形态保持关）
            Log.Info("工作台场景入口：无宿主——启用携带的引导件与音频监听器（直接 Play 形态）", LogTag.Workbench);
        }
    }
}
#endif
