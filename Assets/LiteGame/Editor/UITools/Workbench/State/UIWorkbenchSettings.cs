using System;

namespace LiteGame.Editor.UITools.Workbench
{
    /// <summary>工作台偏好注入口（EditorPrefs / 内存两实现）：夹具不读写真实 EditorPrefs。</summary>
    public interface IWorkbenchSettings
    {
        UIWorkbenchLane Lane { get; set; }
        UIWorkbenchPerspectiveId Perspective { get; set; }

        /// <summary>预览设备档 id（P2-3 B3；空串＝跟随预览缺省档）。</summary>
        string PreviewDeviceProfileId { get; set; }
    }

    /// <summary>EditorPrefs 实现：键名常量单源；读写失败退回默认值（内容降级，不阻断窗口）。</summary>
    public sealed class UIWorkbenchSettings : IWorkbenchSettings
    {
        public const string LaneKey = "LiteGame.UIWorkbench.Lane";
        public const string PerspectiveKey = "LiteGame.UIWorkbench.Perspective";
        public const string PreviewDeviceKey = "LiteGame.UIWorkbench.PreviewDevice";

        private UIWorkbenchLane _lane = UIWorkbenchLane.Engineering;
        private UIWorkbenchPerspectiveId _perspective = UIWorkbenchPerspectiveId.Page;
        private string _previewDevice = string.Empty;

        public UIWorkbenchLane Lane
        {
            get
            {
                string raw = UnityEditor.EditorPrefs.GetString(LaneKey, string.Empty);
                return Enum.TryParse(raw, out UIWorkbenchLane lane) && Enum.IsDefined(typeof(UIWorkbenchLane), lane)
                    ? lane
                    : _lane;
            }
            set
            {
                _lane = value;
                UnityEditor.EditorPrefs.SetString(LaneKey, value.ToString());
            }
        }

        public UIWorkbenchPerspectiveId Perspective
        {
            get
            {
                string raw = UnityEditor.EditorPrefs.GetString(PerspectiveKey, string.Empty);
                return Enum.TryParse(raw, out UIWorkbenchPerspectiveId perspective)
                       && Enum.IsDefined(typeof(UIWorkbenchPerspectiveId), perspective)
                    ? perspective
                    : _perspective;
            }
            set
            {
                _perspective = value;
                UnityEditor.EditorPrefs.SetString(PerspectiveKey, value.ToString());
            }
        }

        /// <summary>预览设备档 id（空串＝跟随预览缺省档；读失败退回内存值）。</summary>
        public string PreviewDeviceProfileId
        {
            get => UnityEditor.EditorPrefs.GetString(PreviewDeviceKey, null) ?? _previewDevice;
            set
            {
                _previewDevice = value ?? string.Empty;
                UnityEditor.EditorPrefs.SetString(PreviewDeviceKey, _previewDevice);
            }
        }
    }

    /// <summary>内存实现：测试与无偏好环境用；不落任何持久化。</summary>
    public sealed class UIWorkbenchSettingsMemory : IWorkbenchSettings
    {
        public UIWorkbenchLane Lane { get; set; } = UIWorkbenchLane.Engineering;
        public UIWorkbenchPerspectiveId Perspective { get; set; } = UIWorkbenchPerspectiveId.Page;
        public string PreviewDeviceProfileId { get; set; } = string.Empty;
    }
}
