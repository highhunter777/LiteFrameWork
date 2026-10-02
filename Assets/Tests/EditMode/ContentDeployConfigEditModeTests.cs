using LiteGame;
using NUnit.Framework;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 部署配置解析（纯函数面——命令行 → <see cref="ContentCdnConfig"/>）。
    /// 语义锚点：**未配置 = 本地信封通道**（现行为不变）；非法值静默保持默认
    /// （部署参数不是攻击面——验签兜底，但也不让它让启动失败）。
    /// </summary>
    public sealed class ContentDeployConfigEditModeTests
    {
        [Test]
        public void 无参数_全默认_即本地通道()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(null);

            Assert.IsFalse(c.HasCdn);
            Assert.AreEqual("", c.BaseUrl);
            Assert.AreEqual("candidate.json", c.OfferPath);
            Assert.AreEqual(30, c.TimeoutSeconds);
        }

        [Test]
        public void 等号形态_基址剥尾斜杠()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(new[] { "-content.cdnUrl=http://cdn.example.com/" });

            Assert.IsTrue(c.HasCdn);
            Assert.AreEqual("http://cdn.example.com", c.BaseUrl);
        }

        [Test]
        public void 空格形态_取下一参数为值()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(new[]
            {
                "-content.cdnUrl", "http://cdn.example.com",
                "-content.cdnOfferPath", "/rel/candidate.json",
                "-content.cdnTimeout", "15",
            });

            Assert.AreEqual("http://cdn.example.com", c.BaseUrl);
            Assert.AreEqual("rel/candidate.json", c.OfferPath);
            Assert.AreEqual(15, c.TimeoutSeconds);
        }

        [Test]
        public void 值位是另一参数开关_不吞开关_该键保持默认()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(new[]
            {
                "-content.cdnUrl", "-content.cdnTimeout", "15",
            });

            Assert.IsFalse(c.HasCdn, "缺值的键不得吞掉后续开关");
            Assert.AreEqual(15, c.TimeoutSeconds);
        }

        [Test]
        public void 非法值_静默保持默认()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(new[]
            {
                "-content.cdnTimeout", "abc",
                "-content.cdnOfferPath", "",
            });

            Assert.AreEqual(30, c.TimeoutSeconds, "非数字超时保持默认");
            Assert.AreEqual("candidate.json", c.OfferPath, "空路径保持默认");
        }

        [Test]
        public void 未知参数_被忽略()
        {
            ContentCdnConfig c = ContentDeployConfig.Parse(new[] { "-batchmode", "-nographics", "42" });

            Assert.IsFalse(c.HasCdn);
            Assert.AreEqual(30, c.TimeoutSeconds);
        }
    }
}
