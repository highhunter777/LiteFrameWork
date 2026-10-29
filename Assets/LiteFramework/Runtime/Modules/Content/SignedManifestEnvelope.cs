using System;
using LiteFramework;

namespace LiteClient
{
    /// <summary>
    /// 签名候选文件解析（§6"发布描述至少含 schemaVersion、…keyId/signature"）。
    ///
    /// **纯解析，不做信任判定**：把发布描述字节与签名从信封中取出，交给
    /// <see cref="ReleaseManifestValidator"/>。解析失败一律返回空提案——
    /// 提供方不代判"可不可信"（§6"先验证描述的结构/预算与签名"）。
    ///
    /// 信封格式（最小实现，与 §6 要求的字段对应）：
    /// <code>
    /// {
    ///   "manifest": { …ReleaseManifest 字段… },      // 与签名覆盖的字节一致
    ///   "signature": "&lt;base64&gt;"                    // 对 manifest 原始字节的签名
    /// }
    /// </code>
    /// **`manifest` 必须与签名覆盖的字节逐字节一致**——故本类从同一份字节里切出 manifest 段，
    /// 而不是重新序列化对象（重新序列化会因字段序/空白差异导致验签失败）。
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

                // 关键：签名覆盖的是**字节原文**，故取子串而非重新序列化（§5 不做换行归一化）
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
