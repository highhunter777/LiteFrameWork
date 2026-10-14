using System;
using System.Collections.Generic;
using System.IO;
using LiteClient;
using LiteSim;
using LiteSim.Tests;
using Luban;
using RoomServer;
using RoomServer.Runtime;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 地图投影双路径对账（《地图数据专项设计》§9/批4）：**服务端直读盘**（<see cref="ServerMapLoader"/>）与
    /// **客户端装载面**（`cfg.Tables` → <see cref="MapTableProjection"/>）从同一份 `tbmap.bytes` 投影出
    /// 同一张判定图——digest 相等且 == 首图快照夹具 <see cref="TestMaps.Arena01"/> 的摘要锚
    /// （批3 实烘值 `9c3d504907d8210e`；改场景重烘后须同步夹具/本锚）。
    /// 另钉 fail-fast：缺表拒载、行内 digest 不符拒载（篡改字节）、缺图房间拒构（D22 必填注入）。
    /// </summary>
    public sealed class MapProjectionTests
    {
        [Fact]
        public void 双路径投影_同digest_且等于首图快照锚()
        {
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");

            SimMapData serverPath = ServerMapLoader.LoadTableBytes(dir);              // 服务端：直读盘
            SimMapData clientPath = MapTableProjection.Project(LoadTables(dir)); // 客户端：装载面投影

            string serverDigest = SimMapDigest.Compute(serverPath);
            Assert.Equal(serverDigest, SimMapDigest.Compute(clientPath));
            Assert.Equal("9c3d504907d8210e", serverDigest);                       // 行内实烘锚（批3）

            SimMapData snapshot = TestMaps.Arena01();                             // 投影 == 夹具快照（批3 遗留对账的持久形态）
            Assert.Equal(SimMapDigest.Compute(snapshot), serverDigest);
            Assert.Equal(snapshot.ObstacleCount, serverPath.ObstacleCount);
            Assert.Equal(snapshot.SpawnPointCount, serverPath.SpawnPointCount);
        }

        [Fact]
        public void 表文件_在表清单内()
        {
            // 服务端取盘路径（Map/{表}.bytes）从共享投影派生——清单漂移即此红（单源守卫）
            Assert.Contains(MapTableProjection.TableFile, TableLayout.Files);
        }

        [Fact]
        public void 缺表目录_拒载()
        {
            string missing = Path.Combine(RepoRoot(), "RoomServer", "Data", "__not_exist__");
            Assert.Throws<FileNotFoundException>(() => ServerMapLoader.LoadTableBytes(missing));
        }

        [Fact]
        public void 篡改表字节_行内digest不符_拒载()
        {
            // 尾部翻转一位（出生点 z 的浮点字节）：Luban 解析仍成功、判定载荷已变——
            // 行内 content_digest 与复算不等，投影必须拒（"map.json→bytes→投影"链路走样检测）
            string path = Path.Combine(RepoRoot(), "Assets", "GameData", "Config", "Map", "tbmap.bytes");
            byte[] bytes = File.ReadAllBytes(path);
            bytes[bytes.Length - 2] ^= 0x01;
            var table = new cfg.Tbmap(new ByteBuf(bytes));
            cfg.map row = table.GetOrDefault(MapTableProjection.DefaultMapId);
            Assert.NotNull(row);

            var ex = Assert.Throws<InvalidDataException>(() => MapTableProjection.Project(row));
            Assert.Contains("content_digest", ex.Message);
        }

        [Fact]
        public void 缺行_拒载()
        {
            var tables = LoadTables(Path.Combine(RepoRoot(), "Assets", "GameData", "Config"));
            Assert.Throws<InvalidDataException>(() => MapTableProjection.Project(tables, mapId: 999));
        }

        [Fact]
        public void 无图房间_拒构()
        {
            // D22：回退位已删——RoomConfig.Map 必填，缺图 fail-fast（不静默兜底）
            Assert.Throws<ArgumentException>(() => new RoomRuntime(new RoomConfig { RoomId = "NoMap" }));
        }

        /// <summary>与 `ConfigService.LoadAsync` 同形的装载面（裸名 → 字节缓存 → cfg.Tables）。</summary>
        private static cfg.Tables LoadTables(string configDir)
        {
            var cache = new Dictionary<string, byte[]>(TableLayout.Files.Length);
            foreach (string entry in TableLayout.Files)
            {
                string path = Path.Combine(configDir, entry.Replace('/', Path.DirectorySeparatorChar) + ".bytes");
                cache[TableLayout.TableName(entry)] = File.ReadAllBytes(path);
            }
            return new cfg.Tables(file => new ByteBuf(cache[file]));
        }

        private static string RepoRoot()
        {
            for (var c = new DirectoryInfo(AppContext.BaseDirectory); c != null; c = c.Parent)
                if (Directory.Exists(Path.Combine(c.FullName, "Tests"))
                    && File.Exists(Path.Combine(c.FullName, "Tests", "Tests.slnx")))
                    return c.FullName;
            throw new DirectoryNotFoundException("找不到仓库根（需要 Tests/Tests.slnx）");
        }
    }
}
