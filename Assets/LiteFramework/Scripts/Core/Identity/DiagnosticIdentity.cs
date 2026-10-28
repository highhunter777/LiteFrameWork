using System;

namespace LiteFramework.Diagnostics
{
    /// <summary>现场包身份（BuildHash/AppVersion/Platform）；由 IdentityContext.Diagnostics 投影。</summary>
    public readonly struct DiagnosticIdentity
    {
        public string BuildHash { get; }
        public string AppVersion { get; }
        public string Platform { get; }

        public DiagnosticIdentity(string buildHash, string appVersion, string platform)
        {
            BuildHash = buildHash ?? string.Empty;
            AppVersion = appVersion ?? string.Empty;
            Platform = platform ?? string.Empty;
        }

        public static DiagnosticIdentity Unknown { get; } = new DiagnosticIdentity(string.Empty, string.Empty, string.Empty);

        public bool IsKnown => !string.IsNullOrEmpty(BuildHash);
    }
}
