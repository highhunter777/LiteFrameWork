namespace LiteFramework
{
    /// <summary>
    /// 一份候选提案：先验证描述的结构/预算与签名，再依据可信描述计划下载。
    /// </summary>
    public readonly struct CandidateOffer
    {
        public readonly ReleaseManifest Manifest;
        public readonly byte[] SignedBytes;
        public readonly byte[] Signature;

        public CandidateOffer(ReleaseManifest manifest, byte[] signedBytes, byte[] signature)
        {
            Manifest = manifest;
            SignedBytes = signedBytes;
            Signature = signature;
        }

        /// <summary>无候选（已是最新 / 无发布通道）。</summary>
        public bool IsEmpty => Manifest == null;
    }
}
