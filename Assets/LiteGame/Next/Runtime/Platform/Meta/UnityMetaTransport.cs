using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace LiteGameNext.Platform.Meta
{
    /// <summary>UnityWebRequest 传输：基址/超时构造期校验；HTTP 错误降级为响应，取消除外。</summary>
    public sealed class UnityMetaTransport : IMetaTransport
    {
        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;

        public UnityMetaTransport(string baseUrl, int timeoutSeconds = 10)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Meta 基址必填", nameof(baseUrl));
            if (timeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "超时必须 ≥ 1 秒");
            _baseUrl = baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<MetaResponse> SendAsync(MetaRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using (var unityRequest = new UnityWebRequest(_baseUrl + request.Path, request.Method))
            {
                if (!string.IsNullOrEmpty(request.JsonBody))
                    unityRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.JsonBody));
                unityRequest.downloadHandler = new DownloadHandlerBuffer();
                unityRequest.timeout = _timeoutSeconds;
                unityRequest.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(request.Bearer))
                    unityRequest.SetRequestHeader("Authorization", "Bearer " + request.Bearer);

                try
                {
                    await unityRequest.SendWebRequest().WithCancellation(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnityWebRequestException ex)
                {
                    long status = ex.ResponseCode > 0 ? ex.ResponseCode : 0;
                    return new MetaResponse(status, ex.Text);
                }

                string body = unityRequest.downloadHandler == null ? null : unityRequest.downloadHandler.text;
                if (unityRequest.result == UnityWebRequest.Result.Success)
                    return new MetaResponse(unityRequest.responseCode, body);
                long responseCode = unityRequest.responseCode > 0 ? unityRequest.responseCode : 0;
                return new MetaResponse(responseCode, body);
            }
        }
    }
}
