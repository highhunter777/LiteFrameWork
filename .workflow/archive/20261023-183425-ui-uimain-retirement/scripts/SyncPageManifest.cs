using System.Linq;
using LiteGame.Editor.UiTools.Page;

public static class SyncPageManifestTool
{
    public static string Run()
    {
        UiPageManifestSyncResult sync = UiPageManifestSync.SyncToDisk();
        if (!sync.Ok) return "fail:" + sync.ErrorText;
        int pages = sync.Manifest.Pages?.Count ?? 0;
        int forms = sync.Manifest.Pages?.Sum(page => page.Forms?.Count ?? 0) ?? 0;
        return "ok pages=" + pages + " forms=" + forms + " warnings=" + (sync.Warnings?.Count ?? 0);
    }
}
