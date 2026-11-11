using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteGameNext.Platform.Meta
{
    /// <summary>一次 Meta HTTP 请求（传输无关）：路径以 / 开头，正文已是 JSON。</summary>
    public sealed class MetaRequest
    {
        public MetaRequest(string method, string path, string bearer, string jsonBody)
        {
            Method = method;
            Path = path;
            Bearer = bearer;
            JsonBody = jsonBody;
        }

        public string Method { get; }

        public string Path { get; }

        public string Bearer { get; }

        public string JsonBody { get; }
    }

    /// <summary>一次 Meta HTTP 响应：0 = 网络不可达；非 2xx 保留状态码与错误体。</summary>
    public sealed class MetaResponse
    {
        public MetaResponse(long statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body ?? string.Empty;
        }

        public long StatusCode { get; }

        public string Body { get; }
    }

    /// <summary>Meta 传输端口：实现可换 UnityWebRequest/测试假件，MetaClient 不依赖 Unity 网络类型。</summary>
    public interface IMetaTransport
    {
        UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken);
    }
}
