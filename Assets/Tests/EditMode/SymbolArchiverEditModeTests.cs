using System.IO;
using LiteGame.Toolchain;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// T0-2d：IL2CPP 符号归档——收集（仅符号扩展名）、按相对路径拷贝、manifest 对账；
    /// 无符号产物返回 null（非失败）。
    /// </summary>
    public sealed class SymbolArchiverEditModeTests
    {
        [Test]
        public void Archive_符号按相对结构归档并写manifest()
        {
            string build = Path.Combine(Path.GetTempPath(), "sym-build-" + System.Guid.NewGuid().ToString("N"));
            string archive = Path.Combine(Path.GetTempPath(), "sym-archive-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(build, "Game_Data", "il2cpp_data"));
                File.WriteAllBytes(Path.Combine(build, "GameAssembly.pdb"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(build, "Game_Data", "il2cpp_data", "nested.pdb"), new byte[] { 4, 5 });
                File.WriteAllBytes(Path.Combine(build, "Game.exe"), new byte[] { 9 });          // 非符号：不收
                File.WriteAllBytes(Path.Combine(build, "UnityPlayer.dll"), new byte[] { 9 });    // 非符号：不收

                string target = SymbolArchiver.Archive(build, "abc123def", archive);

                Assert.IsNotNull(target);
                Assert.IsTrue(File.Exists(Path.Combine(target, "GameAssembly.pdb")));
                Assert.IsTrue(File.Exists(Path.Combine(target, "Game_Data", "il2cpp_data", "nested.pdb")));
                Assert.IsFalse(File.Exists(Path.Combine(target, "Game.exe")));

                string manifest = File.ReadAllText(Path.Combine(target, "manifest.json"));
                StringAssert.Contains("abc123def", manifest);
                StringAssert.Contains("GameAssembly.pdb", manifest);
                StringAssert.Contains("nested.pdb", manifest);
                StringAssert.Contains("sha256", manifest);
                StringAssert.DoesNotContain("Game.exe", manifest);
            }
            finally
            {
                if (Directory.Exists(build)) Directory.Delete(build, true);
                if (Directory.Exists(archive)) Directory.Delete(archive, true);
            }
        }

        [Test]
        public void Archive_无符号产物返回null_目录缺失安全()
        {
            string empty = Path.Combine(Path.GetTempPath(), "sym-empty-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(empty);
                Assert.IsNull(SymbolArchiver.Archive(empty, "h", Path.Combine(empty, "out")));

                // 不存在的构建目录：空表收集（不抛），归档返回 null
                Assert.IsNull(SymbolArchiver.Archive(
                    Path.Combine(Path.GetTempPath(), "no-such-build-" + System.Guid.NewGuid().ToString("N")),
                    "h", Path.Combine(empty, "out")));
            }
            finally
            {
                if (Directory.Exists(empty)) Directory.Delete(empty, true);
            }
        }
    }
}
