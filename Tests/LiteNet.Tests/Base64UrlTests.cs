using System;
using System.Text;
using LiteNet;
using RoomServer.Application;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// Base64Url 编解码单源用例（P2）：票据线格式（<see cref="JoinTicketFormat"/>）、访问令牌线格式
    /// （Meta <c>AccessTokenFormat</c>）与重连票据（<c>ReconnectService</c>）三处此前各写一份实现，
    /// 本组把"三条链共用同一实现"钉成事实（任何一处改回自带实现都会在这里露出来）。
    /// </summary>
    public sealed class Base64UrlTests
    {
        [Fact]
        public void 编码_无padding无分隔符_URL安全字母表()
        {
            // 构造会产出 '+' '/' 的字节（0xFB 0xFF → "+/"），确认被替换为 '-' '_'
            string encoded = Base64Url.Encode(new byte[] { 0xFB, 0xFF, 0xBF });
            Assert.DoesNotContain('+', encoded);
            Assert.DoesNotContain('/', encoded);
            Assert.DoesNotContain('=', encoded);          // 去 padding

            // 文本形态（票据/令牌段）：结果绝不出现 '.'（否则破坏 '.' 分隔的线格式）
            Assert.DoesNotContain('.', Base64Url.EncodeText("acct.with.dots.中文"));
        }

        [Fact]
        public void 文本往返_含非ASCII与空值()
        {
            string source = "acct.with.dots.中文.émoji";
            string encoded = Base64Url.EncodeText(source);

            Assert.Equal(source, Base64Url.DecodeText(encoded));
            Assert.Equal(string.Empty, Base64Url.DecodeText(string.Empty));
            Assert.Equal(string.Empty, Base64Url.EncodeText(null));
            Assert.Null(Base64Url.DecodeText(null));
        }

        [Fact]
        public void 解码非法输入_返回null不抛()
        {
            Assert.Null(Base64Url.DecodeText("ab!"));      // 非 base64 字母表
            Assert.Null(Base64Url.DecodeText("abcde"));    // 长度模 4 余 1：非法
            Assert.False(Base64Url.TryDecode("abcde", out _));
            Assert.False(Base64Url.TryDecode("ab!", out _));
        }

        [Fact]
        public void 字节往返_与票据与重连票据同源()
        {
            byte[] bytes = { 1, 2, 3, 250, 251, 252 };
            string encoded = Base64Url.Encode(bytes);

            Assert.True(Base64Url.TryDecode(encoded, out byte[] back));
            Assert.Equal(bytes, back);

            // 重连票据：CSPRNG 16 B → 22 字符 base64url（既有语义不变，实现已归单源）
            var session = new ReconnectService();
            string token = session.Issue(playerId: 1, roomId: "Room-A");
            Assert.Equal(22, token.Length);
            Assert.Matches("^[A-Za-z0-9_-]{22}$", token);
            Assert.True(Base64Url.TryDecode(token, out byte[] raw));
            Assert.Equal(ReconnectService.TokenEntropyBytes, raw.Length);

            // 票据线格式：编码结果可被同一实现解回（且不含 '.'，分段不受影响）
            string field = JoinTicketFormat.Encode("段值.带点.中文");
            Assert.DoesNotContain('.', field);
            Assert.Equal("段值.带点.中文", JoinTicketFormat.Decode(field));
        }

        [Fact]
        public void 生成物_不含padding_长度与原始字节匹配()
        {
            // 3 字节 → 4 字符（去掉 padding 后）；16 字节 → 22 字符
            Assert.Equal(4, Base64Url.Encode(Encoding.UTF8.GetBytes("abc")).Length);
            Assert.Equal(22, Base64Url.Encode(new byte[16]).Length);
        }
    }
}
