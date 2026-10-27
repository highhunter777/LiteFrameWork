using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // 解析失败路径读 Log（全局静态）
    public sealed class SettingTests
    {
        private static readonly FileSystem TestFiles = new FileSystem(new TempPathProvider(), new SystemTextJsonSerializer());
        private readonly SettingService _svc = new SettingService(TestFiles);   // 纯内存（不 Load/Save，不触 IO）

        [Fact]
        public void Setting_缺失_返回默认值()
        {
            var s = new Setting<int>(_svc, "k_int", 42);
            Assert.Equal(42, s.Get());
        }

        [Fact]
        public void Setting_Set后Get_四类型往返()
        {
            new Setting<int>(_svc, "k_int", 0).Set(7);
            new Setting<string>(_svc, "k_str", "").Set("中文值");
            new Setting<bool>(_svc, "k_bool", false).Set(true);
            new Setting<float>(_svc, "k_float", 0f).Set(0.25f);

            Assert.Equal(7, new Setting<int>(_svc, "k_int", 0).Get());
            Assert.Equal("中文值", new Setting<string>(_svc, "k_str", "").Get());
            Assert.True(new Setting<bool>(_svc, "k_bool", false).Get());
            Assert.Equal(0.25f, new Setting<float>(_svc, "k_float", 0f).Get());
        }

        [Fact]
        public void Setting_Set越界_被clamp钳制()
        {
            var volume = new Setting<float>(_svc, "k_vol", 0.8f, v => Math.Clamp(v, 0f, 1f));
            volume.Set(2f);
            Assert.Equal(1f, volume.Get());
            volume.Set(-3f);
            Assert.Equal(0f, volume.Get());
        }

        [Fact]
        public void Setting_存储值越界_Get侧同样被clamp()
        {
            _svc.SetString("k_qual", "99");                            // 模拟外部改档/旧档脏值
            var quality = new Setting<int>(_svc, "k_qual", 2, v => Math.Clamp(v, 0, 3));
            Assert.Equal(3, quality.Get());
        }

        [Fact]
        public void Setting_坏值_按默认值并Warning可见()
        {
            _svc.SetString("k_bad", "abc");
            var s = new Setting<int>(_svc, "k_bad", 5);
            Assert.Equal(5, s.Get());

            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
            Assert.Contains("k_bad", last.Message);
        }

        [Fact]
        public void Setting_float往返无损_零点一不缩水()
        {
            new Setting<float>(_svc, "k_r", 0f).Set(0.1f);
            Assert.Equal(0.1f, new Setting<float>(_svc, "k_r", 0f).Get());   // "R" 格式 + InvariantCulture
        }

        [Fact]
        public void Setting_不支持的类型_构造时抛()
        {
            Assert.Throws<NotSupportedException>(() => new Setting<double>(_svc, "k_d", 0d));
        }

        private sealed class TempPathProvider : IPathProvider
        {
            public string RootPath => Path.Combine(Path.GetTempPath(), "liteframework-setting-tests");
        }

        private sealed class SystemTextJsonSerializer : IJsonSerializer
        {
            public string Serialize<T>(T value) => JsonSerializer.Serialize(value);
            public T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json);
        }
    }
}
