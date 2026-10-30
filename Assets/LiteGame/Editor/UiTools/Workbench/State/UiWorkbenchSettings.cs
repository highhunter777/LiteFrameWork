using System;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>工作台偏好注入口（EditorPrefs / 内存两实现）：夹具不读写真实 EditorPrefs。</summary>
    public interface IWorkbenchSettings
    {
        UiWorkbenchLane Lane { get; set; }
        UiWorkbenchPerspectiveId Perspective { get; set; }

        /// <summary>预览设备档 id（P2-3 B3；空串＝跟随预览缺省档）。</summary>
        string PreviewDeviceProfileId { get; set; }
    }

    /// <summary>EditorPrefs 实现：键名常量单源；读写失败退回默认值（内容降级，不阻断窗口）。</summary>
    public sealed class UiWorkbenchSettings : IWorkbenchSettings
    {
        public const string LaneKey = "LiteGame.UiWorkbench.Lane";
        public const string PerspectiveKey = "LiteGame.UiWorkbench.Perspective";
        public const string PreviewDeviceKey = "LiteGame.UiWorkbench.PreviewDevice";

        private UiWorkbenchLane _lane = UiWorkbenchLane.Engineering;
        private UiWorkbenchPerspectiveId _perspective = UiWorkbenchPerspectiveId.Page;
        private string _previewDevice = string.Empty;

        public UiWorkbenchLane Lane
        {
            get
            {
                string raw = UnityEditor.EditorPrefs.GetString(LaneKey, string.Empty);
                return Enum.TryParse(raw, out UiWorkbenchLane lane) && Enum.IsDefined(typeof(UiWorkbenchLane), lane)
                    ? lane
                    : _lane;
            }
            set
            {
                _lane = value;
                UnityEditor.EditorPrefs.SetString(LaneKey, value.ToString());
            }
        }

        public UiWorkbenchPerspectiveId Perspective
        {
            get
            {
                string raw = UnityEditor.EditorPrefs.GetString(PerspectiveKey, string.Empty);
                return Enum.TryParse(raw, out UiWorkbenchPerspectiveId perspective)
                       && Enum.IsDefined(typeof(UiWorkbenchPerspectiveId), perspective)
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
    public sealed class UiWorkbenchSettingsMemory : IWorkbenchSettings
    {
        public UiWorkbenchLane Lane { get; set; } = UiWorkbenchLane.Engineering;
        public UiWorkbenchPerspectiveId Perspective { get; set; } = UiWorkbenchPerspectiveId.Page;
        public string PreviewDeviceProfileId { get; set; } = string.Empty;
    }
}
