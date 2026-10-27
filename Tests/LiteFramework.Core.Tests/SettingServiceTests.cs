using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // _files.Init 静态标志 + 磁盘落档
    public sealed class SettingServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly FileSystem _files;

        public SettingServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "liteframework-tests", Guid.NewGuid().ToString("N"));
            _files = new FileSystem(new FakePathProvider(_root), new FakeJsonSerializer());
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public void SettingService_首启_文件不存在_Load静默不抛()
        {
            var svc = new SettingService(_files);
            svc.Load();                                  // 首启是正常状态：空白 + 静默
            Assert.False(svc.Has("volume"));
        }

        [Fact]
        public void SettingService_SetSave后_新实例Load_值还在()
        {
            var first = new SettingService(_files);
            first.Load();
            first.SetFloat("volume", 0.35f);
            first.SetString("lang", "en-US");
            first.Save();

            var second = new SettingService(_files);
            second.Load();
            Assert.Equal(0.35f, second.GetFloat("volume"));
            Assert.Equal("en-US", second.GetString("lang"));
        }

        [Fact]
        public void SettingService_SaveIfDirty_无变更不落盘()
        {
            var svc = new SettingService(_files);
            svc.Load();
            svc.SaveIfDirty();                           // 从未变更 → 不写盘
            Assert.False(_files.Exists("settings.json"));
        }

        [Fact]
        public void SettingService_SaveIfDirty_有变更才落盘()
        {
            var svc = new SettingService(_files);
            svc.Load();
            svc.SetInt("quality", 3);
            svc.SaveIfDirty();
            Assert.True(_files.Exists("settings.json"));
        }

        [Fact]
        public void SettingService_Remove与Has_SetString传null等于移除()
        {
            var svc = new SettingService(_files);
            svc.Load();
            svc.SetString("k", "v");
            Assert.True(svc.Has("k"));

            svc.SetString("k", null);                    // null = 移除（回默认值语义）
            Assert.False(svc.Has("k"));
        }

        [Fact]
        public void SettingService_坏值解析_按默认值并Warning可见()
        {
            var svc = new SettingService(_files);
            svc.Load();
            svc.SetString("k_int", "not-a-number");
            Assert.Equal(9, svc.GetInt("k_int", 9));

            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
        }

        [Fact]
        public void SettingService_空键_抛()
        {
            var svc = new SettingService(_files);
            Assert.Throws<ArgumentNullException>(() => svc.GetString(""));
            Assert.Throws<ArgumentNullException>(() => svc.SetString(null, "v"));
        }

        private sealed class FakePathProvider : IPathProvider
        {
            private readonly string _root;
            public FakePathProvider(string root) => _root = root;
            public string RootPath => _root;
        }

        private sealed class FakeJsonSerializer : IJsonSerializer
        {
            public string Serialize<T>(T value) => JsonSerializer.Serialize(value);
            public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
        }
    }
}
