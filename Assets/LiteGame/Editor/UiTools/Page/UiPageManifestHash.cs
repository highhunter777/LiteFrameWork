using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LiteGame.Editor.UiTools.Page
{
    /// <summary>
    /// 页面清单内容指纹（《UI 编辑器工具链专项设计》§8.5）：按页序覆盖身份与维护字段；
    /// **不含**告警、生成器版本、时间戳——对账/编辑后重算，用于发现外部手改。
    /// </summary>
    public static class UiPageManifestHash
    {
        public static string Compute(UiPageManifest manifest)
        {
            var sb = new StringBuilder();
            sb.Append("ui-page-manifest/").Append(UiPageManifest.CurrentSchemaVersion).Append('\n');
            if (manifest?.Pages != null)
            {
                foreach (UiPageManifestEntry page in manifest.Pages)
                {
                    if (page == null) continue;
                    string forms = page.Forms == null
                        ? string.Empty
                        : string.Join(";", page.Forms.OrderBy(form => form.FormId)
                            .Select(form => form.FormId + ":" + form.Layer + ":" + (form.FullScreen ? "1" : "0")));
                    sb.Append("page|").Append(page.Name ?? string.Empty).Append('|')
                        .Append(page.PrefabPath ?? string.Empty).Append('|')
                        .Append(forms).Append('|')
                        .Append(page.LuaPath ?? string.Empty).Append('|')
                        .Append(page.CacheStrategy ?? string.Empty).Append('|')
                        .Append(page.Owner ?? string.Empty).Append('|')
                        .Append(page.Status ?? string.Empty).Append('|')
                        .Append(page.ContractPath ?? string.Empty).Append('|')
                        .Append(page.ContractPresent ? "1" : "0").Append('|')
                        .Append(page.DesignRef ?? string.Empty).Append('|')
                        .Append(page.DeviceProfiles == null
                            ? string.Empty
                            : string.Join(",", page.DeviceProfiles.OrderBy(profile => profile, StringComparer.Ordinal)))
                        .Append('|')
                        .Append(page.Notes ?? string.Empty).Append('\n');
                }
            }

            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return "sha256:" + BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
