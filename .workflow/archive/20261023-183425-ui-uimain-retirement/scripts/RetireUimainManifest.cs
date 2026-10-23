using System.Linq;
using LiteGame.Editor.UiTools.Page;

public static class RetireUimainManifest
{
    public static string Run()
    {
        UiPageManifest manifest = UiPageManifestStore.Load();
        string removeError = null;
        bool removed = UiPageManifestEditor.Remove(manifest, "UIMain", out removeError);
        if (removed) UiPageManifestStore.Save(manifest);
        else if (!string.IsNullOrEmpty(removeError) && !removeError.Contains("不存在")) return "remove-fail:" + removeError;

        UiPageManifestSyncResult sync = UiPageManifestSync.SyncToDisk();
        if (!sync.Ok) return "sync-fail:" + sync.ErrorText;
        int pages = sync.Manifest.Pages?.Count ?? 0;
        int forms = sync.Manifest.Pages?.Sum(page => page.Forms?.Count ?? 0) ?? 0;
        return "ok removed=" + removed + " pages=" + pages + " forms=" + forms
            + " warnings=" + (sync.Warnings?.Count ?? 0);
    }
}
