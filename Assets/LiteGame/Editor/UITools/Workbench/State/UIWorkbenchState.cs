namespace LiteGame.Editor.UITools.Workbench
{
    /// <summary>
    /// 工作台共享上下文（《UI 编辑器工具链专项设计》§2.1）：页面选择、目标 prefab、Lane、透视与未保存标记。
    /// </summary>
    public sealed class UIWorkbenchState
    {
        public UIWorkbenchState(UIWorkbenchLane lane = UIWorkbenchLane.Engineering)
        {
            Lane = lane;
        }

        public UIWorkbenchLane Lane { get; private set; }
        public UIWorkbenchPerspectiveId Perspective { get; private set; } = UIWorkbenchPerspectiveId.Page;
        public string TargetPath { get; private set; }
        public string SelectedPage { get; private set; }
        public bool HasUnsavedEdits { get; private set; }

        /// <summary>切换角色；返回是否变化（不改变资产、不清上下文）。</summary>
        public bool SetLane(UIWorkbenchLane lane)
        {
            if (Lane == lane) return false;
            Lane = lane;
            return true;
        }

        /// <summary>切换透视；返回是否变化（不重置目标/选中页）。</summary>
        public bool SetPerspective(UIWorkbenchPerspectiveId perspective)
        {
            if (Perspective == perspective) return false;
            Perspective = perspective;
            return true;
        }

        /// <summary>设置目标 prefab（资源路径；空值表示清空）。</summary>
        public bool SetTarget(string prefabPath)
        {
            if (string.Equals(TargetPath, prefabPath, System.StringComparison.Ordinal)) return false;
            TargetPath = prefabPath;
            return true;
        }

        /// <summary>选中页面清单条目（按页面名）。</summary>
        public bool SelectPage(string pageName)
        {
            if (string.Equals(SelectedPage, pageName, System.StringComparison.Ordinal)) return false;
            SelectedPage = pageName;
            return true;
        }

        public void MarkDirty() => HasUnsavedEdits = true;

        public void ClearDirty() => HasUnsavedEdits = false;
    }
}
