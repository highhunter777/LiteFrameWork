using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine.Networking;

namespace LiteGame
{
    /// <summary>
    /// CDN 信封提供者（《热更与内容发布专项设计》§7"各源必须提供同一摘要文件"）：
    /// 从部署基址取候选信封 <c>{baseUrl}/{offerPath}</c>，交 <see cref="SignedManifestEnvelope.Parse"/>
    /// 解析——**提供方不代判可信**，签名/预算/兼容判定仍在 <c>ReleaseManifestValidator</c>（§6 信任门）。
    ///
    /// **结果语义（与 <see cref="FileSystemCandidateProvider"/> 逐条对齐）**：
    /// - HTTP 404 = 发布点无候选（= 本地信封文件不存在）→ 空提案（IsEmpty），调用方按
    ///   "以已确认版本继续"处理；
    /// - 2xx 但信封畸形 = 解析返回空提案（Parse 的既有契约："畸形信封 = 无候选（不抛、不代判）"）；
    /// - **网络错/5xx/超时 = 抛出**——"查不到"与"没有"是两回事，网络故障伪装成无候选会让
    ///   更新被静默跳过（诚实边界：暂态失败交 <c>ProcedurePatch</c> 的既有 catch 进确定错误态留档，
    ///   由用户/流程重试）。信封多源轮转待发布侧多源就绪（当前单基址）。
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
                    // UniTask 的 WithCancellation 在非 2xx 时自己抛——await 之后的 result 检查对错误路径不可达
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
