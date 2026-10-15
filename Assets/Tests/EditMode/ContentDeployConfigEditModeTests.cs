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

        // ---- 三源装载（命令行 > 包内 deploy.args 烙入 > 默认）----

        [Test]
        public void 烙入合并_命令行逐字段优先_烙入只填空()
        {
            string bake = "-content.cdnUrl=http://cdn.baked.com\n-content.cdnTimeout=45\n";
            ContentCdnConfig c = ContentDeployConfig.Load(
                new[] { "-content.cdnUrl=http://cdn.args.com" }, bake);

            Assert.AreEqual("http://cdn.args.com", c.BaseUrl, "命令行优先");
            Assert.AreEqual("candidate.json", c.OfferPath, "两边都未给 = 默认");
            Assert.AreEqual(45, c.TimeoutSeconds, "烙入数值键生效");
            Assert.IsTrue(c.HasCdn);
        }

        [Test]
        public void 烙入缺失_退化为纯命令行装载()
        {
            ContentCdnConfig c = ContentDeployConfig.Load(new string[0], null);
            Assert.IsFalse(c.HasCdn, "无烙入且无参数 = 全默认（本地信封通道——现状行为零变化）");
            Assert.AreEqual(0, ContentDeployConfig.Tokenize(null).Length);
            Assert.AreEqual(0, ContentDeployConfig.Tokenize(" \r\n ").Length);
        }

        [Test]
        public void 烙入全键覆盖_零参数分发形态()
        {
            string bake = "-content.cdnUrl=http://cdn.example.com\n-content.cdnOfferPath=prod.json\n-content.cdnTimeout=60\n";
            ContentCdnConfig c = ContentDeployConfig.Load(new string[0], bake);

            Assert.AreEqual("http://cdn.example.com", c.BaseUrl);
            Assert.AreEqual("prod.json", c.OfferPath);
            Assert.AreEqual(60, c.TimeoutSeconds);
        }
    }
}
