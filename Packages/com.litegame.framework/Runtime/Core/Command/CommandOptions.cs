namespace LiteFramework
{
    /// <summary>命令注册配置（《命令中心专项设计》§2）：
    /// GmOnly = 权限门（三宏之外 release 一律 GmBlocked——GM 能力物理上进不了发布版）；
    /// Description = GM 面板发现面用（§6——面板据注册清单列出可用命令，不硬编码）。</summary>
    public sealed class CommandOptions
    {
        public bool GmOnly;
        public string Description;

        public CommandOptions(bool gmOnly = false, string description = null)
        {
            GmOnly = gmOnly;
            Description = description;
        }
    }
}
