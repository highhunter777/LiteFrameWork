using System;
using LiteFramework;

namespace LiteFramework
{
    /// <summary>
    /// 签名候选文件解析（§6"发布描述至少含 schemaVersion、…keyId/signature"）。
    /// </summary>
    public static class SignedManifestEnvelope
    {
        /// <summary>从信封字节解析（<paramref name="envelopeJson"/> 为文件原文）。失败返回空提案。</summary>
        public static CandidateOffer Parse(string envelopeJson)
        {
            if (string.IsNullOrEmpty(envelopeJson)) return default;

            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(envelopeJson);
                var manifestToken = root["manifest"] as Newtonsoft.Json.Linq.JObject;
                string signatureB64 = (string)root["signature"];
                if (manifestToken == null || string.IsNullOrEmpty(signatureB64)) return default;

                // 关键：签名覆盖的是**字节原文**，故取子串而非重新序列化（§5 不做换行归一化）。
                byte[] signedBytes = System.Text.Encoding.UTF8.GetBytes(manifestToken.ToString(
                    Newtonsoft.Json.Formatting.None));

                var manifest = manifestToken.ToObject<ReleaseManifest>();
                if (manifest == null) return default;

                byte[] signature = Convert.FromBase64String(signatureB64);
                return new CandidateOffer(manifest, signedBytes, signature);
            }
            catch (Exception)
            {
                return default;              // 畸形信封 = 无候选（不抛、不代判）
            }
        }
    }
}
