using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 令牌源的规范 JSON 口径（单源）：字段序由模型 `JsonProperty(Order)` 决定、组内令牌按 ID `Ordinal` 排序、
    /// 缩进固定、LF 换行、末尾换行、可选字段省略（null 不写）、未知字段拒绝（`MissingMemberHandling.Error`）、
    /// 重复键拒绝（`DuplicatePropertyNameHandling.Error`）。
    /// `sourceHash` 取规范文本的 SHA-256（小写 hex），生成幂等与"SO 过期"判定共用同一指纹。
    /// </summary>
    public static class UiThemeTokenJson
    {
        /// <summary>规范序列化设置（反序列化复用同一份：未知字段报错，避免静默丢字段）。</summary>
        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        };

        /// <summary>规范文本：先就地规范化（组内按 ID `Ordinal` 排序，幂等），LF 归一 + 末尾换行。</summary>
        public static string Serialize(UiThemeTokens tokens)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            Sort(tokens);
            string json = JsonConvert.SerializeObject(tokens, Settings);
            return json.Replace("\r\n", "\n").Replace("\r", "\n") + "\n";
        }

        /// <summary>反序列化（未知字段/重复键报错）；返回后七组与组内令牌均已按规范序整理。</summary>
        public static UiThemeTokens Deserialize(string json)
        {
            string text = json ?? string.Empty;
            RejectDuplicateKeys(text);
            UiThemeTokens tokens = JsonConvert.DeserializeObject<UiThemeTokens>(text, Settings);
            Sort(tokens);
            return tokens;
        }

        /// <summary>规范文本的 SHA-256（小写 hex）。</summary>
        public static string HashCanonical(string canonicalText)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalText ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        /// <summary>重复键预扫描：Newtonsoft 默认"后者覆盖前者"，会让重复令牌静默丢一条。</summary>
        private static void RejectDuplicateKeys(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    JToken.ReadFrom(reader, LoadSettings);
                }
            }
            catch (JsonException error)
            {
                throw new JsonSerializationException("令牌源 JSON 非法（重复键）：" + error.Message, error);
            }
        }

        /// <summary>组内令牌整理为规范序（组清单来自 <see cref="UiThemeGroups.Enumerate"/>；缺组不补，由校验器报）。</summary>
        private static void Sort(UiThemeTokens tokens)
        {
            if (tokens?.Groups == null) return;
            foreach ((string _, string[] _, IUiThemeGroup group) in tokens.Groups.Enumerate())
                group?.SortTokens();
        }
    }
}
