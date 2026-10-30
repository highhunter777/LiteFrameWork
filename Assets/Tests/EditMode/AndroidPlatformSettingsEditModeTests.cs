using System.IO;
using LiteFramework;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Android 平台工程设置四件套（《安卓平台适配》批一）：后端/架构、脚本宏、方向、
    /// 包名与网络、图形 API 与纹理压缩、默认质量档、帧率档。
    ///
    /// 这些设置只存在于 `ProjectSettings/*.asset`，改错不会编译失败、也不会在桌面表现——
    /// 只有真机首启/上架时才暴露。故把它们钉成可执行断言：设置漂移即 L2 红。
    /// 脚本宏不在本件重复断言（唯一真源是 `scripts/gate/toolchain.json` 的 platformDefines，
    /// 由 `toolchain.py check` 校验）。
    /// </summary>
    public sealed class AndroidPlatformSettingsEditModeTests
    {
        private const string PlayerSettingsAsset = "ProjectSettings/ProjectSettings.asset";
        private const string QualitySettingsAsset = "ProjectSettings/QualitySettings.asset";
        private const string BundleId = "com.litegame.dev";
        private const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";

        /// <summary>`m_BuildTargetDefaultTextureCompressionFormat` 段里的 ASTC 取值
        /// （`TextureCompressionFormat.ASTC` 的序列化整数；该枚举在编辑器程序集内不可见，故以常量钉住）。</summary>
        private const int AstcSerializedValue = 3;

        [Test]
        public void 安卓_后端为IL2CPP且架构为ARM64()
        {
            Assert.AreEqual(ScriptingImplementation.IL2CPP, PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android));
            Assert.AreEqual(AndroidArchitecture.ARM64, PlayerSettings.Android.targetArchitectures,
                "目标架构必须只含 ARM64（ARMv7 会产出 x86/arm32 混合包并拖慢构建）");
        }

        [Test]
        public void 安卓_锁横屏且关闭竖屏自动旋转()
        {
            Assert.AreEqual(UIOrientation.LandscapeLeft, PlayerSettings.defaultInterfaceOrientation);
            Assert.IsFalse(PlayerSettings.allowedAutorotateToPortrait, "竖屏自动旋转必须关闭");
            Assert.IsFalse(PlayerSettings.allowedAutorotateToPortraitUpsideDown, "倒置竖屏自动旋转必须关闭");
        }

        [Test]
        public void 安卓_包名与网络权限()
        {
            Assert.AreEqual(BundleId, PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
            Assert.IsTrue(PlayerSettings.Android.forceInternetPermission, "INTERNET 权限必须强制声明");
        }

        [Test]
        public void 安卓_自定义主清单声明明文旅程()
        {
            Assert.IsTrue(SerializedPlayerSettings().FindProperty("useCustomMainManifest").boolValue,
                "未启用自定义主清单——cleartext 策略无处落地");

            Assert.IsTrue(File.Exists(ManifestPath), "自定义主清单缺失：" + ManifestPath);
            string text = File.ReadAllText(ManifestPath);
            StringAssert.Contains("android.permission.INTERNET", text);
            StringAssert.Contains("com.unity3d.player.UnityPlayerActivity", text);
            StringAssert.Contains("unityplayer.UnityActivity", text);
            // 源码清单是安全默认：明文只在 development 通道出包时由 build-player 注入并还原
            StringAssert.DoesNotContain("android:usesCleartextTraffic", text,
                "源码清单不得携带明文放行属性——否则 release 包会一并放开 HTTP");
        }

        [Test]
        public void 安卓_图形API为GLES3优先()
        {
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            Assert.GreaterOrEqual(apis.Length, 1);
            Assert.AreEqual(GraphicsDeviceType.OpenGLES3, apis[0], "GLES3 必须排在图形 API 首位（Vulkan 兼容面更窄）");
        }

        [Test]
        public void 安卓_默认纹理压缩为ASTC()
        {
            SerializedProperty formats = SerializedPlayerSettings().FindProperty("m_BuildTargetDefaultTextureCompressionFormat");
            Assert.IsNotNull(formats, "工程设置缺 m_BuildTargetDefaultTextureCompressionFormat——纹理压缩面无法核对");
            Assert.AreEqual(AstcSerializedValue, PlatformValue(formats, "Android", "m_BuildTarget", "m_Format"),
                "Android 默认纹理压缩应为 ASTC（移动端唯一广覆盖的有损高质量格式）");
        }

        [Test]
        public void 安卓_默认质量档为Performant()
        {
            int performant = System.Array.IndexOf(QualitySettings.names, "Performant");
            Assert.GreaterOrEqual(performant, 0, "质量档缺少 Performant——质量表被改名后本断言必须同步");

            SerializedObject quality = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath(QualitySettingsAsset)[0]);
            SerializedProperty perPlatform = quality.FindProperty("m_PerPlatformDefaultQuality");
            Assert.IsNotNull(perPlatform, "质量设置缺 m_PerPlatformDefaultQuality——平台默认档无法核对");
            Assert.AreEqual(performant, PlatformValue(perPlatform, "Android"));
        }

        [Test]
        public void 帧率档_移动端定格桌面保持引擎默认()
        {
            Assert.AreEqual(60, AppLifetime.ResolveTargetFrameRate(true));
            Assert.AreEqual(-1, AppLifetime.ResolveTargetFrameRate(false));
        }

        private static SerializedObject SerializedPlayerSettings()
        {
            Object settings = AssetDatabase.LoadAllAssetsAtPath(PlayerSettingsAsset)[0];
            return new SerializedObject(settings);
        }

        /// <summary>读「平台 → 值」数组段的某个平台值。段内键名随设置项不同
        /// （`first`/`second` 是字典形态，`m_BuildTarget`/`m_Format` 是结构化条目）。</summary>
        private static int PlatformValue(SerializedProperty array, string platform,
            string nameKey = "first", string valueKey = "second")
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                SerializedProperty entry = array.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative(nameKey).stringValue == platform)
                    return entry.FindPropertyRelative(valueKey).intValue;
            }

            Assert.Fail("设置段缺少平台条目：" + platform);
            return int.MinValue;
        }
    }
}
