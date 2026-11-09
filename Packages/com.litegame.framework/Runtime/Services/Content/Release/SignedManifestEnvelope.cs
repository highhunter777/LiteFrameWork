using System;
using System.Text;

namespace LiteFramework
{
    /// <summary>候选信封解析：签名覆盖 JSON 节点的紧凑字节，发布字段交给内容校验器。</summary>
    public static class SignedManifestEnvelope
    {
        /// <summary>畸形信封返回空提案；JSON 实现由装配点注入。</summary>
        public static CandidateOffer Parse(string envelopeJson, IJsonCodec json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (string.IsNullOrEmpty(envelopeJson)) return default;
            try
            {
                if (!json.TryParse(envelopeJson, out IJsonNode root)
                    || !root.TryGetMember("manifest", out IJsonNode manifestNode)
                    || manifestNode.Kind != JsonValueKind.Object
                    || !root.TryGetMember("signature", out IJsonNode signatureNode)
                    || !signatureNode.TryGetString(out string signatureB64)
                    || string.IsNullOrEmpty(signatureB64)) return default;

                string compact = manifestNode.ToCompactJson();
                if (!manifestNode.TryConvertTo(out ReleaseManifest manifest)) return default;
                return new CandidateOffer(manifest, Encoding.UTF8.GetBytes(compact), Convert.FromBase64String(signatureB64));
            }
            catch (Exception)
            {
                return default;
            }
        }
    }
}
