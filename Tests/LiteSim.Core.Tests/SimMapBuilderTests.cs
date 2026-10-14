using System;
using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>地图数据构建单源（<see cref="SimMapBuilder"/>）：定长填充正确、越界与非法值 fail-fast。</summary>
    public sealed class SimMapBuilderTests
    {
        [Fact]
        public void 构建_定长填充_计数与值到位()
        {
            var obstacles = new SimObstacle[]
            {
                new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(1f, 0f, 2f), HalfX = 3f, HalfZ = 4f, Height = 5f },
            };
            var spawns = new SimVector3[] { new SimVector3(7f, 0f, 8f) };

            SimMapData map = SimMapBuilder.Build(0.5f, 100f, 90f, obstacles, 1, spawns, 1);

            Assert.Equal(1, map.ObstacleCount);
            Assert.Equal(0.5f, map.GroundY);
            Assert.Equal(100f, map.HalfWidth);
            Assert.Equal(90f, map.HalfDepth);
            Assert.Equal(3f, map.Obstacles[0].HalfX);
            Assert.Equal(7f, map.SpawnPoints[0].X);
            Assert.Equal(8f, map.SpawnPoints[0].Z);
        }

        [Fact]
        public void 越界_障碍数超上限_抛()
        {
            var obstacles = new SimObstacle[SimMapData.MaxObstacles + 1];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, obstacles, obstacles.Length, new SimVector3[1], 0));
        }

        [Fact]
        public void 越界_出生数超上限_抛()
        {
            var spawns = new SimVector3[SimMapData.MaxSpawnPoints + 1];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, new SimObstacle[1], 0, spawns, spawns.Length));
        }

        [Fact]
        public void 非法_障碍坐标NaN_抛()
        {
            var obstacles = new SimObstacle[]
            {
                new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(float.NaN, 0f, 0f), HalfX = 1f, HalfZ = 1f, Height = 1f },
            };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, obstacles, 1, new SimVector3[1], 0));
        }

        [Fact]
        public void 非法_盒半宽为零_抛()
        {
            var obstacles = new SimObstacle[]
            {
                new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 0f, HalfZ = 1f, Height = 1f },
            };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, obstacles, 1, new SimVector3[1], 0));
        }

        [Fact]
        public void 非法_surface越界_抛()
        {
            var obstacles = new SimObstacle[]
            {
                new SimObstacle
                {
                    Kind = SimObstacleKind.Box,
                    Center = new SimVector3(0f, 0f, 0f),
                    HalfX = 1f,
                    HalfZ = 1f,
                    Height = 1f,
                    Surface = (SimObstacleSurface)11,
                },
            };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, obstacles, 1, new SimVector3[1], 0));
        }

        [Fact]
        public void 非法_高度非正_抛()
        {
            var obstacles = new SimObstacle[]
            {
                new SimObstacle { Kind = SimObstacleKind.Box, Center = new SimVector3(0f, 0f, 0f), HalfX = 1f, HalfZ = 1f, Height = 0f },
            };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimMapBuilder.Build(0f, 100f, 100f, obstacles, 1, new SimVector3[1], 0));
        }
    }
}
