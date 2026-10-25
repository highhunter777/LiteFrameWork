using System.Linq;
using LiteGame.Editor.UiTools.Preview;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 设备档案单源（P2-3 B3 / AC1；总设计 §7.3 钦定六横屏档＋竖屏预留）：
    /// JSON 装载/校验负例/确定性排序/三方读面（驱动/工作台/矩阵）零字面量复制。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiDeviceProfilesEditModeTests
    {
        private static UiDeviceProfileDocument LoadReal() => UiDeviceProfileCatalog.Load();

        [Test]
        public void 装载_六横屏档入矩阵且竖屏预留()
        {
            UiDeviceProfileDocument doc = LoadReal();
            var landscape = doc.Profiles.Where(p => p.Orientation == "landscape").ToList();
            var portrait = doc.Profiles.Where(p => p.Orientation == "portrait").ToList();
            Assert.GreaterOrEqual(landscape.Count, 6, "总设计 §7.3 钦定至少六横屏档");
            Assert.IsTrue(landscape.All(p => p.InMatrix), "横屏档全部入矩阵");
            Assert.IsNotEmpty(portrait, "竖档预留（inMatrix=false）");
            Assert.IsTrue(portrait.All(p => !p.InMatrix), "竖屏预留不入默认矩阵");
        }

        [Test]
        public void 装载_钦定宽高比全覆盖()
        {
            UiDeviceProfileDocument doc = LoadReal();
            var aspects = doc.Profiles.Where(p => p.InMatrix)
                .Select(p => p.Width / (float)p.Height).Distinct().ToList();
            // 市场标称比为近似值（如 2560×1080=64:27 称 21:9），容差取 0.05。
            foreach (float aspect in new[] { 16f / 9f, 19.5f / 9f, 20f / 9f, 4f / 3f, 16f / 10f, 21f / 9f })
                Assert.IsTrue(aspects.Any(a => System.Math.Abs(a - aspect) < 0.05f),
                    "缺钦定宽高比：" + aspect.ToString("0.###"));
        }

        [Test]
        public void 校验_预置档零问题()
        {
            UiDeviceProfileDocument doc = LoadReal();
            Assert.IsNotEmpty(doc.Profiles, "预置档案应非空（防空集恒真）");
            Assert.IsEmpty(UiDeviceProfileCatalog.Validate(doc), "预置档案应零问题");
        }

        [Test]
        public void 校验_宽高比与分辨率不符报错()
        {
            var doc = new UiDeviceProfileDocument();
            doc.Profiles.Add(new UiDeviceProfile
            { Id = "test.bad-aspect", Width = 1920, Height = 1080, Orientation = "landscape", Aspect = 1.5f });
            Assert.IsNotEmpty(UiDeviceProfileCatalog.Validate(doc), "Aspect 声明与宽高派生不符应报错");
        }

        [Test]
        public void 校验_inset负值或越界报错()
        {
            var doc = new UiDeviceProfileDocument();
            doc.Profiles.Add(new UiDeviceProfile
            {
                Id = "test.bad-inset", Width = 1000, Height = 500, Orientation = "landscape", Aspect = 2f,
                SafeInsets = new UiSafeInsets { Left = -10f, Top = 0f, Right = 600f, Bottom = 0f },
            });
            Assert.IsNotEmpty(UiDeviceProfileCatalog.Validate(doc), "inset 负值/超出半宽应报错");
        }

        [Test]
        public void 校验_ID文法非法报错()
        {
            var doc = new UiDeviceProfileDocument();
            doc.Profiles.Add(new UiDeviceProfile
            { Id = "Bad Id!", Width = 1000, Height = 500, Orientation = "landscape", Aspect = 2f });
            Assert.IsNotEmpty(UiDeviceProfileCatalog.Validate(doc), "ID 文法（小写字母数字点横线）非法应报错");
        }

        [Test]
        public void 确定性_重读一致且按id排序无时间戳()
        {
            string canonical1 = UiDeviceProfileCatalog.Serialize(LoadReal());
            string canonical2 = UiDeviceProfileCatalog.Serialize(UiDeviceProfileCatalog.Load());
            Assert.IsNotEmpty(canonical1, "规范文本非空（防空集恒真）");
            StringAssert.Contains("\"profiles\"", canonical1, "规范文本应含档位数组");
            Assert.AreEqual(canonical1, canonical2, "重读规范文本逐字节一致");
            Assert.IsFalse(canonical1.Contains("time") || canonical1.Contains("Time"), "无时间戳字段");
            var ids = LoadReal().Profiles.Select(p => p.Id).ToList();
            Assert.IsNotEmpty(ids, "档位非空");
            CollectionAssert.AreEqual(ids.OrderBy(i => i, System.StringComparer.Ordinal).ToList(), ids, "按 id Ordinal 排序");
        }

        [Test]
        public void TryGet_已知与未知()
        {
            UiDeviceProfileDocument doc = LoadReal();
            Assert.IsTrue(UiDeviceProfileCatalog.TryGet(doc, "pc.16-9", out UiDeviceProfile profile), "钦定档应可取");
            Assert.AreEqual(1920, profile.Width);
            Assert.IsFalse(UiDeviceProfileCatalog.TryGet(doc, "no.such", out _), "未知档不猜");
        }

        [Test]
        public void MatrixProfiles_只含入矩阵档()
        {
            var matrix = UiDeviceProfileCatalog.MatrixProfiles(LoadReal());
            Assert.IsNotEmpty(matrix);
            Assert.IsTrue(matrix.All(p => p.InMatrix), "矩阵面只含 InMatrix 档");
        }
    }
}
