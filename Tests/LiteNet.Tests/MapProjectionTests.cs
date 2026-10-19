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
    /// （Arena01 56×56 再缩批实烘值 `83d63d5c44f77893`；改场景重烘后须同步夹具/本锚）。
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
            Assert.Equal("83d63d5c44f77893", serverDigest);                       // 行内实烘锚（56×56 再缩批）

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
        public void 多图_训练场行_双路径投影_且等于训练场快照锚()
        {
            // 多图批：行 2（训练场）同口径对账——服务端按行取盘 == 客户端按行投影 == 快照夹具摘要
            string dir = Path.Combine(RepoRoot(), "Assets", "GameData", "Config");
            int mapId = MapTableProjection.TrainingMapId;

            SimMapData serverPath = ServerMapLoader.LoadTableBytes(dir, mapId);
            SimMapData clientPath = MapTableProjection.Project(LoadTables(dir), mapId);

            Assert.Equal("c78441a74f6bc5f9", SimMapDigest.Compute(serverPath));
            Assert.Equal(SimMapDigest.Compute(serverPath), SimMapDigest.Compute(clientPath));
            SimMapData snapshot = TestMaps.TrainingGround();
            Assert.Equal(SimMapDigest.Compute(snapshot), SimMapDigest.Compute(serverPath));
            Assert.Equal(snapshot.ObstacleCount, serverPath.ObstacleCount);
            Assert.Equal(snapshot.SpawnPointCount, serverPath.SpawnPointCount);
        }

        [Fact]
        public void 行内场景列_每行非空且为场景资产路径_行键唯一()
        {
            // 多图批：客户端装载与离场卸载都按表行 scene 取（"加图＝加文件 + 加场景"）——
            // 缺场景路径/空串 = 投影面没有可用场景（装载期 fail-fast，本用例把它钉在表生成物上）。
            var tables = LoadTables(Path.Combine(RepoRoot(), "Assets", "GameData", "Config"));
            Assert.NotNull(tables.Tbmap);
            var seenIds = new HashSet<int>();
            var seenScenes = new HashSet<string>();
            foreach (cfg.map row in tables.Tbmap.DataList)
            {
                Assert.True(seenIds.Add(row.MapId), $"map_id 重复：{row.MapId}");
                Assert.False(string.IsNullOrEmpty(row.Scene), $"map_id={row.MapId} 缺 scene 列（重烘 + gen.bat 后才有值）");
                Assert.StartsWith("Assets/Scenes/", row.Scene);
                Assert.EndsWith(".unity", row.Scene);
                Assert.True(seenScenes.Add(row.Scene), $"场景重复登记：{row.Scene}（多行共用场景会在卸载面去重，属可疑配置）");
                Assert.True(File.Exists(Path.Combine(RepoRoot(), row.Scene.Replace('/', Path.DirectorySeparatorChar))),
                    $"map_id={row.MapId} 的 scene 不存在：{row.Scene}");
            }
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
            // 篡改**首图行内 content_digest 串**（ASCII 可见于字节流）：Luban 解析仍成功、内容摘要已不符——
            // 行内 content_digest 与复算不等，投影必须拒（"map.json→bytes→投影"链路走样检测）。
            // 注记：不再用"尾部翻一位"——tbmap 已是多图（每图一文件，行序＝文件序），尾部字节属最后一张图。
            string path = Path.Combine(RepoRoot(), "Assets", "GameData", "Config", "Map", "tbmap.bytes");
            byte[] bytes = File.ReadAllBytes(path);
            byte[] digestAscii = System.Text.Encoding.ASCII.GetBytes("83d63d5c44f77893");
            int at = IndexOf(bytes, digestAscii);
            Assert.True(at >= 0, "字节流应含首图行内 digest 串");
            bytes[at] = (byte)(bytes[at] == (byte)'9' ? '8' : '9');
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

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { hit = false; break; }
                if (hit) return i;
            }
            return -1;
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
