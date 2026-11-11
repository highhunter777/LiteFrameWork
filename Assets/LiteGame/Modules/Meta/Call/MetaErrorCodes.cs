namespace LiteGame.Modules.Meta.Call
{
    /// <summary>稳定错误码的单源（与服务端 <c>AuthErrorCodes</c> 同值；客户端只比对，不发明）。</summary>
    public static class MetaErrorCodes
    {
        /// <summary>访问令牌缺失/失效（服务端 401；恢复链判"账号重入"）。</summary>
        public const string Unauthorized = "auth.unauthorized";

        /// <summary>凭证失效的 HTTP 状态码（与 <see cref="Unauthorized"/> 同判据，两端都收）。</summary>
        public const long HttpUnauthorized = 401;
    }
}
