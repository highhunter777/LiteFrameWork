using System;
using System.Text.RegularExpressions;

namespace RoomServer.Application
{
    /// <summary>
    /// 验签密钥（kid 允许密钥轮换——§16 安全测试清单"密钥轮换"；算法固定 HMAC-SHA256，**不自创密码算法**）。
    /// 密钥**不入库**，由部署注入（Meta 专项 §13.1：密钥与票据原文都不得进日志）。
    /// </summary>
    public sealed class JoinTicketKey
    {
        /// <summary>密钥标识（票据明文携带；未知 kid 直接拒绝）。</summary>
        public readonly string Kid;

        private readonly byte[] _secret;

        public JoinTicketKey(string kid, byte[] secret)
        {
            if (string.IsNullOrEmpty(kid)) throw new ArgumentException("kid 不能为空", nameof(kid));
            if (secret == null || secret.Length == 0) throw new ArgumentException("secret 不能为空", nameof(secret));
            if (!KidPattern.IsMatch(kid)) throw new ArgumentException("kid 只允许 [A-Za-z0-9_-]{1,32}", nameof(kid));
            Kid = kid;
            _secret = (byte[])secret.Clone();   // 防御性拷贝：外部改不到内部密钥
        }

        /// <summary>从 base64 装配（宿主配置形态）。</summary>
        public static JoinTicketKey FromBase64(string kid, string base64Secret)
        {
            if (string.IsNullOrEmpty(base64Secret)) throw new ArgumentException("密钥不能为空", nameof(base64Secret));
            return new JoinTicketKey(kid, Convert.FromBase64String(base64Secret));
        }

        internal byte[] Secret
        {
            get { return _secret; }
        }

        /// <summary>kid 形状约束：**保证不含分隔符 <c>.</c>**（含则票据被拆成错误段数）。</summary>
        private static readonly Regex KidPattern = new Regex(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
    }
}
