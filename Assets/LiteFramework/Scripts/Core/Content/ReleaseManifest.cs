using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 发布描述（《热更与内容发布专项设计》§6"发布描述至少含 schemaVersion、releaseId、单调发布修订、
    /// 目标平台/渠道、兼容维度、依赖、文件路径/长度/摘要、创建/到期策略、keyId/signature、
    /// 激活策略与安全撤销信息"）。
    ///
    /// **这是目标契约的类型落点**：字段齐备以便校验器逐条落实；填充由发布流水线（§13）承担。
    /// **规则**（DSL 用中文分号在文档里，这里用换行加入可枚举字符串）：见 <see cref="ReleaseManifest.Validate"/>。
    /// </summary>
    public sealed class ReleaseManifest
    {
        /// <summary>当前支持的描述结构版本。不识别的版本一律拒绝（不猜测语义）。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>结构版本（<see cref="CurrentSchemaVersion"/>）。</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>一次不可变内容发布的身份（非空、不含路径分隔符）。</summary>
        public string ReleaseId;

        /// <summary>单调发布修订（**反回退**依据：候选修订低于已确认修订即拒绝——§6 防重放）。</summary>
        public long Revision;

        /// <summary>目标平台（如 "StandaloneWindows64"/"Android"）。</summary>
        public string Platform;

        /// <summary>目标渠道（可选；空 = 全渠道）。</summary>
        public string Channel;

        /// <summary>兼容维度（App/Bridge/Protocol/Sim/schema/存档等，§5 版本元组）。</summary>
        public ReleaseCompatibility Compatibility;

        /// <summary>依赖的其他 ReleaseId（本发布生效前必须已确认的版本）。</summary>
        public List<string> Dependencies = new List<string>();

        /// <summary>候选文件清单。</summary>
        public List<ReleaseFileEntry> Files = new List<ReleaseFileEntry>();

        /// <summary>创建时间（Unix 秒；0 = 未声明）。</summary>
        public long CreatedAtUnix;

        /// <summary>到期时间（Unix 秒；0 = 不过期）。过期描述拒绝（§6 元数据过期策略）。</summary>
        public long ExpiresAtUnix;

        /// <summary>签名公钥标识（对应 <see cref="TrustedKeyRing"/>）。</summary>
        public string KeyId;

        /// <summary>对**描述规范化字节**的签名（Base64）。</summary>
        public string Signature;

        /// <summary>生效窗口（§3 首版：NextLaunch / SafeWindow / NextMatch）。</summary>
        public ReleaseEffectWindow EffectWindow = ReleaseEffectWindow.NextLaunch;

        /// <summary>安全撤销标记：置位表示该发布已被运营撤销，**不得激活**（§12 回退前检查目标未被撤销）。</summary>
        public bool Revoked;

        /// <summary>文件名（诊断用，非安全字段）。</summary>
        public string Note;
    }

    /// <summary>生效窗口（§3 首版三档；LiveRefresh 需单独登记消费者，不在本类型开放）。</summary>
    public enum ReleaseEffectWindow
    {
        /// <summary>下次启动生效（首版默认）。</summary>
        NextLaunch = 0,
        /// <summary>安全窗口生效（退匹配/UI Scope 后）。</summary>
        SafeWindow = 1,
        /// <summary>下一局生效。对局中可下载候选，但不得替换 Match 使用的配置/脚本/资源代次。**不适用于本批的冷启动激活**：见下方登记。</summary>
        NextMatch = 2,
    }

    /// <summary>兼容维度（§5 版本元组——候选必须声明它要求的下限，准入据此接受/拒绝）。</summary>
    public sealed class ReleaseCompatibility
    {
        /// <summary>要求的 App 版本下限（空 = 不限）。</summary>
        public string AppVersion;

        /// <summary>要求的 Lua Bridge API 版本（0 = 不限）。</summary>
        public int BridgeApiVersion;

        /// <summary>要求的协议版本（0 = 不限）。</summary>
        public int ProtocolVersion;

        /// <summary>要求的 Sim 版本（0 = 不限）。</summary>
        public int SimVersion;

        /// <summary>要求的配置 schema 版本（0 = 不限）。</summary>
        public int ConfigSchemaVersion;

        /// <summary>要求的存档 schema 版本（0 = 不限）。</summary>
        public int SaveSchemaVersion;
    }

    /// <summary>候选文件条目（§6"文件路径/长度/摘要"）。</summary>
    public sealed class ReleaseFileEntry
    {
        /// <summary>相对候选目录的路径（**只允许受控目录内的相对路径**——见 <see cref="ReleaseManifestValidator"/>）。</summary>
        public string Path;

        /// <summary>字节长度（负 = 非法）。</summary>
        public long Length;

        /// <summary>文件完整性摘要（**原始字节** SHA-256 小写 hex，64 字符）。</summary>
        public string Sha256;
    }
}
