using LiteGame;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 联机入口部署配置解析（纯函数面——命令行 → <see cref="MetaEntryConfig"/>）。
    /// 语义锚点：**未配置 Meta 基址 = 联机入口显性拒绝**（HasMeta=false，消费端不连任何地址）；
    /// 非法值静默保持默认（部署参数不是攻击面——令牌/票据效力由服务端验签把守，
    /// 但也不让它让启动失败）；信封密钥空 = 不注入（回落环境变量口，由装配侧裁决）。
    /// </summary>
    public sealed class MetaDeployConfigEditModeTests
    {
        [Test]
        public void 无参数_全默认_即未配置()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(null);

            Assert.IsFalse(c.HasMeta);
            Assert.AreEqual("", c.MetaUrl);
            Assert.AreEqual("", c.EnvelopeKeyBase64);
            Assert.AreEqual(15, c.TimeoutSeconds);
        }

        [Test]
        public void 等号形态_基址剥尾斜杠()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[]
            {
                "-meta.url=https://meta.example.com/",
                "-envelope.key=AAECAwQFBgcICQ==",
            });

            Assert.IsTrue(c.HasMeta);
            Assert.AreEqual("https://meta.example.com", c.MetaUrl);
            Assert.AreEqual("AAECAwQFBgcICQ==", c.EnvelopeKeyBase64);
        }

        [Test]
        public void 空格形态_取下一参数为值()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[]
            {
                "-meta.url", "https://meta.example.com",
                "-meta.timeout", "20",
                "-envelope.key", " AAEC ",
            });

            Assert.AreEqual("https://meta.example.com", c.MetaUrl);
            Assert.AreEqual(20, c.TimeoutSeconds);
            Assert.AreEqual("AAEC", c.EnvelopeKeyBase64, "密钥值修剪首尾空白");
        }

        [Test]
        public void 值位是另一参数开关_不吞开关_该键保持默认()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[]
            {
                "-meta.url", "-meta.timeout", "20",
            });

            Assert.IsFalse(c.HasMeta, "缺值的键不得吞掉后续开关");
            Assert.AreEqual(20, c.TimeoutSeconds);
        }

        [Test]
        public void 非法超时_静默保持默认()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[]
            {
                "-meta.timeout", "abc",
                "-meta.timeout", "0",
            });

            Assert.AreEqual(15, c.TimeoutSeconds, "非数字与非正数超时均保持默认");
        }

        [Test]
        public void 未知参数_被忽略()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[] { "-batchmode", "-nographics", "42" });

            Assert.IsFalse(c.HasMeta);
            Assert.AreEqual(15, c.TimeoutSeconds);
        }

        [Test]
        public void 密钥显式空值_保持未注入()
        {
            MetaEntryConfig c = MetaDeployConfig.Parse(new[] { "-envelope.key=" });

            Assert.AreEqual("", c.EnvelopeKeyBase64, "显式空值 = 保持默认（回落环境变量口）");
        }
    }
}
