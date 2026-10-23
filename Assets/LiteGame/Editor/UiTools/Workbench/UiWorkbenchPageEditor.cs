using LiteGame.Editor.UiTools.Page;

namespace LiteGame.Editor.UiTools.Workbench
{
    /// <summary>
    /// 页面透视的编辑门面：只改内存清单并经 <see cref="UiPageManifestEditor" /> 校验，**不自动落盘**；
    /// 保存由窗口显式调用 <see cref="UiPageManifestStore.Save"/>（失败不写盘、保留内存编辑值）。
    /// 维护字段与状态迁移的校验口径不在此复制——单一真相仍是 <see cref="UiPageManifestEditor"/>。
    /// </summary>
    public static class UiWorkbenchPageEditor
    {
        /// <summary>写维护字段（Owner/设计稿/备注）；null 表示保留原值。</summary>
        public static bool ApplyEditorial(UiPageManifest manifest, string name, string owner,
            string designRef, string notes, out string error)
            => UiPageManifestEditor.SetEditorial(manifest, name, owner, designRef, null, notes, out error);

        /// <summary>状态迁移（五态；迁移表由 <see cref="UiPageManifestEditor.SetStatus"/> 单源校验）。</summary>
        public static bool ApplyStatus(UiPageManifest manifest, string name, UiPageStatus status, out string error)
            => UiPageManifestEditor.SetStatus(manifest, name, status, out error);
    }
}
