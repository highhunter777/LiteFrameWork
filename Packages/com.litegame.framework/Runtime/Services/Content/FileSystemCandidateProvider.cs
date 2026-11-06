using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LiteFramework
{
    /// <summary>
    /// 从 FileSystem 读候选信封（§6）。只读**一个**约定路径：存在即返回提案，不存在即无候选。
    /// 真实发布通道（CDN/发布服务）属 H3-b，本实现**不发起任何网络请求**。
    /// </summary>
    public sealed class FileSystemCandidateProvider : ICandidateProvider
    {
        /// <summary>约定候选信封路径（FileSystem 相对路径）。</summary>
        public const string DefaultOfferPath = "content/candidate.json";

        private readonly string _offerPath;
        private readonly IFileStore _files;

        public FileSystemCandidateProvider(string offerPath = DefaultOfferPath, IFileStore files = null)
        {
            _offerPath = string.IsNullOrEmpty(offerPath) ? DefaultOfferPath : offerPath;
            _files = files ?? throw new System.ArgumentNullException(nameof(files), "IFileStore 未注入");
        }

        public UniTask<CandidateOffer> TryGetCandidateAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromResult(default(CandidateOffer));
            if (!_files.Exists(_offerPath)) return UniTask.FromResult(default(CandidateOffer));

            string json = _files.ReadAllText(_offerPath);
            return UniTask.FromResult(SignedManifestEnvelope.Parse(json));
        }
    }
}
