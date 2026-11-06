using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.Networking;

namespace LiteFramework
{
    /// <summary>
    /// CDN 信封提供者（《热更与内容发布专项设计》§7"各源必须提供同一摘要文件"）： 从部署基址取候选信封 <c>{baseUrl}/{offerPath}</c>。
    /// </summary>
    public sealed class HttpCandidateProvider : ICandidateProvider
    {
        private readonly string _baseUrl;         // 末尾不带 /
        private readonly string _offerPath;       // 信封相对路径（不带前导 /）
        private readonly int _timeoutSeconds;

        /// <param name="baseUrl">部署基址（如 <c>http://cdn.example.com/content</c>；文件与信封同基址）。</param>
        /// <param name="offerPath">信封相对路径（发布流水线约定，默认 <c>candidate.json</c>）。</param>
        public HttpCandidateProvider(string baseUrl, string offerPath, int timeoutSeconds = 30)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl)
                ? throw new ArgumentException("来源基址不能为空", nameof(baseUrl))
                : baseUrl.TrimEnd('/');
            _offerPath = string.IsNullOrEmpty(offerPath)
                ? throw new ArgumentException("信封路径不能为空", nameof(offerPath))
                : offerPath.TrimStart('/');
            _timeoutSeconds = timeoutSeconds < 1 ? 30 : timeoutSeconds;
        }

        public async UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default)
        {
            string url = _baseUrl + "/" + _offerPath;

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = _timeoutSeconds;

                try
                {
                    await request.SendWebRequest().WithCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    throw;                       // 取消按取消传播（不降级成"无候选"）
                }
                catch (UnityWebRequestException ex) when (ex.ResponseCode == 404)
                {
                    return default;              // 无发布 = 无候选（同本地文件不存在语义）
                }
                catch (UnityWebRequestException ex)
                {
                    // UniTask 的 WithCancellation 在非 2xx 时自己抛——await 之后的 result 检查对错误路径不可达。
                    throw new InvalidOperationException(
                        $"候选信封获取失败：HTTP {ex.ResponseCode}: {url}", ex);
                }

                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException(
                        $"候选信封获取失败：{request.result} {request.responseCode}: {url}");

                string json = request.downloadHandler?.text;
                return SignedManifestEnvelope.Parse(json);   // 畸形 → 空提案（Parse 契约，不在此重复判定）
            }
        }
    }
}
