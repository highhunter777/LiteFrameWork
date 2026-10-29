using System;
using System.IO;
using LiteCodeGen.EditorTools;
using LiteTesting;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    [Category(TestCategory.Contract)]
    public sealed class BindFileWriterEditModeTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "litecodegen-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void 主文件_已存在时不覆盖()
        {
            string path = Path.Combine(_root, "Main.cs");
            File.WriteAllText(path, "manual");

            Assert.That(BindFileWriter.WriteMainIfMissing(path, "template"), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("manual"));
        }

        [Test]
        public void 主文件_不存在时创建_Designer按内容写()
        {
            string main = Path.Combine(_root, "Main.cs");
            Assert.That(BindFileWriter.WriteMainIfMissing(main, "template"), Is.True);
            Assert.That(File.ReadAllText(main), Is.EqualTo("template"));

            string designer = Path.Combine(_root, "Main.Designer.cs");
            Assert.That(BindFileWriter.WriteIfChanged(designer, "a"), Is.True);
            Assert.That(BindFileWriter.WriteIfChanged(designer, "a"), Is.False);
            Assert.That(BindFileWriter.WriteIfChanged(designer, "b"), Is.True);
            Assert.That(File.ReadAllText(designer), Is.EqualTo("b"));
        }

        [Test]
        public void 共享源集目录被拒绝()
        {
            string reason;
            Assert.That(BindOutputPolicy.IsForbidden("Assets/LiteSim/Scripts", out reason), Is.True);
            Assert.That(reason, Is.Not.Null);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(BindOutputPolicy.IsForbidden("Assets/LiteGame/UI/Bind", out reason), Is.False);
        }
    }
}
