using System.Net;
using LiteNet.Transport;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 规范化远端地址用例（R2 安全批，§P0-6"Transport 必须提供规范化远端地址…后才可宣称实现
    /// per-IP 限流"）：限流键的稳定性依赖归一——同一主机的不同端点表达必须落到同一个键，
    /// 不同主机不得合并。纯 L1，无 Socket/墙钟。
    /// </summary>
    public sealed class RemoteAddressTests
    {
        [Fact]
        public void IPv4_去端口()
        {
            Assert.Equal("1.2.3.4", RemoteAddress.Normalize(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 1234)));
            Assert.Equal("1.2.3.4", RemoteAddress.Normalize(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 65535)));
        }

        [Fact]
        public void IPv4mapped_归一为IPv4_双栈socket同一主机同键()
        {
            string dual = RemoteAddress.Normalize(new IPEndPoint(IPAddress.Parse("::ffff:1.2.3.4"), 9));
            string plain = RemoteAddress.Normalize(new IPEndPoint(IPAddress.Parse("1.2.3.4"), 100));
            Assert.Equal("1.2.3.4", dual);
            Assert.Equal(plain, dual);
        }

        [Fact]
        public void IPv6_压缩小写()
        {
            Assert.Equal("2001:db8::1",
                RemoteAddress.Normalize(new IPEndPoint(
                    IPAddress.Parse("2001:0db8:0000:0000:0000:0000:0000:0001"), 1)));
        }

        [Fact]
        public void 未知地址_返回空()
        {
            Assert.Null(RemoteAddress.Normalize((IPEndPoint)null));
            Assert.Null(RemoteAddress.Normalize((IPAddress)null));
        }
    }
}
