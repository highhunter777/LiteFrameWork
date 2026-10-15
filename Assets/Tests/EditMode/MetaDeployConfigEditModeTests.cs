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

        // ---- 三源装载（命令行 > 包内 deploy.args 烙入 > 默认）----

        [Test]
        public void 烙入合并_命令行逐字段优先_烙入只填空()
        {
            string bake = "-meta.url=http://baked.example.com\n-envelope.key=QUFrRUQ=\n-meta.timeout=30\n";
            MetaEntryConfig c = MetaDeployConfig.Load(
                new[] { "-meta.url=http://args.example.com" }, bake);

            Assert.AreEqual("http://args.example.com", c.MetaUrl, "命令行优先（烙入不覆盖显式注入）");
            Assert.AreEqual("QUFrRUQ=", c.EnvelopeKeyBase64, "命令行未给的键由烙入补");
            Assert.AreEqual(30, c.TimeoutSeconds, "烙入的数值键同样生效");
        }

        [Test]
        public void 烙入缺失_退化为纯命令行装载()
        {
            MetaEntryConfig c = MetaDeployConfig.Load(new[] { "-meta.url=http://args.example.com" }, null);
            Assert.AreEqual("http://args.example.com", c.MetaUrl, "无烙入文本 = 纯命令行（现状行为零变化）");

            MetaEntryConfig c2 = MetaDeployConfig.Load(new[] { "-meta.url=http://a.com" }, "   \r\n  ");
            Assert.AreEqual("http://a.com", c2.MetaUrl, "空白烙入 = 无烙入");
            Assert.AreEqual("", c2.EnvelopeKeyBase64);
        }

        [Test]
        public void 烙入全键覆盖_零参数分发形态()
        {
            string bake = "-meta.url=http://meta.example.com\n-envelope.key=QUFrRUQ=\n-meta.timeout=20\n";
            MetaEntryConfig c = MetaDeployConfig.Load(new string[0], bake);

            Assert.AreEqual("http://meta.example.com", c.MetaUrl, "分发包零参数：全键来自烙入");
            Assert.AreEqual("QUFrRUQ=", c.EnvelopeKeyBase64);
            Assert.AreEqual(20, c.TimeoutSeconds);
            Assert.IsTrue(c.HasMeta);
        }

        [Test]
        public void Tokenize_行流与单行多token等价_空文本空流()
        {
            string[] tokens = MetaDeployConfig.Tokenize("-a=1\r\n-b=2\n-c 3 \t");
            CollectionAssert.AreEqual(new[] { "-a=1", "-b=2", "-c", "3" }, tokens);

            Assert.AreEqual(0, MetaDeployConfig.Tokenize(null).Length);
            Assert.AreEqual(0, MetaDeployConfig.Tokenize("  \r\n").Length);
        }

        [Test]
        public void 拼接序_烙入在前命令行在后_同键后见者赢()
        {
            // token 层单流（烙入 ++ 命令行）：Parse 顺序覆盖 ⇒ 同键命令行赢、异键烙入保留
            string bake = "-meta.url=http://baked.com\n-meta.timeout=30\n";
            MetaEntryConfig c = MetaDeployConfig.Load(
                new[] { "-meta.url=http://args.com", "-envelope.key=QUFrRUQ=" }, bake);

            Assert.AreEqual("http://args.com", c.MetaUrl, "同键：命令行（后见）赢");
            Assert.AreEqual(30, c.TimeoutSeconds, "命令行未给的键：烙入保留");
            Assert.AreEqual("QUFrRUQ=", c.EnvelopeKeyBase64);
        }

        [Test]
        public void 空值语义_烙入流内显式空值_命令行未再给_保持未注入()
        {
            string bake = "-envelope.key=\n";                     // 烙入里显式空值 = 未注入
            MetaEntryConfig c = MetaDeployConfig.Load(new string[0], bake);

            Assert.AreEqual("", c.EnvelopeKeyBase64, "空值不顶默认（信封回落环境变量口）");
            Assert.IsFalse(c.HasMeta);
        }
    }
}
