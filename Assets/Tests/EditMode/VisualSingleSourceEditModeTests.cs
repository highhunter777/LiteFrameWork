using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteGame.Editor;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 视觉单一来源（《UI框架总设计》§7）：运行时代码不得构建用户可见视觉，
    /// 一律取 `Assets/UI/Screens|Widgets` 模板。
    ///
    /// 本用例是该规则的**可执行载体**（《UI制作规范》§8"资产静态检查"；
    /// 《UI测试开发专项设计》§4.2"每个规则至少一个合法正例和一个违规负例"）：
    /// 合成负例证明规则真会失败，再对真实代码面扫描。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class VisualSingleSourceEditModeTests : LiteTesting.Unity.UnityTestBase
    {
        // ---- 违规负例：规则必须真的会红 ----

        [Test]
        public void 负例_代码建Canvas_判违规()
        {
            var v = VisualConstructionScanner.ScanSource("Assets/X/Bad.cs",
                "var go = new GameObject(\"Root\", typeof(Canvas), typeof(CanvasScaler));");
            Assert.AreEqual(1, v.Count, "构造 Canvas 必须判违规");
            StringAssert.Contains("Canvas", v[0].Reason);
        }

        [Test]
        public void 负例_AddComponent挂视觉组件_判违规()
        {
            var v = VisualConstructionScanner.ScanSource("Assets/X/Bad.cs",
                "var img = panel.AddComponent<Image>();");
            Assert.AreEqual(1, v.Count, "AddComponent<Image> 必须判违规");
        }

        [Test]
        public void 负例_EventSystem与InputModule_判违规()
        {
            var v = VisualConstructionScanner.ScanSource("Assets/X/Bad.cs",
                "var es = new GameObject(\"ES\", typeof(EventSystem), typeof(StandaloneInputModule));");
            Assert.AreEqual(1, v.Count, "交互结构同属视觉面（§7）——一次报出即可");
        }

        // ---- 正例：规则不误伤 ----

        [Test]
        public void 正例_层容器骨架不判违规()
        {
            // UIService 建 [UIRoot] 与各层节点是**层级骨架不是视觉**（§7 原文）——不带视觉组件参数
            var v = VisualConstructionScanner.ScanSource("Assets/X/Ok.cs",
                "var root = new GameObject(\"[UIRoot]\");\n" +
                "var node = new GameObject(GroupNames[i]);\n" +
                "node.transform.SetParent(root.transform, false);");
            Assert.AreEqual(0, v.Count, "层容器节点是骨架，不判违规");
        }

        [Test]
        public void 正例_登记例外不判违规()
        {
            // 引导期错误界面：例外（§7 判据）——显式登记在 AllowedFileNames。
            // 按**文件名**登记，故此处路径随目录迁移变化不影响放行。
            var v = VisualConstructionScanner.ScanSource(
                "Assets/LiteGame/App/Procedure/ProcedureError.cs",
                "var root = new GameObject(\"BootstrapErrorUI\", typeof(Canvas), typeof(Image));");
            Assert.AreEqual(0, v.Count, "登记例外不判违规（判据见 §7：仅引导期 + 绑冒烟标记）");
        }

        [Test]
        public void 反例_同名之外的视觉构建仍判违规()
        {
            // 按文件名登记的边界：**只有登记的那个文件名**放行，别的文件即便在例外目录里也不行
            var v = VisualConstructionScanner.ScanSource(
                "Assets/LiteGame/App/Procedure/ProcedureMain.cs",
                "var root = new GameObject(\"X\", typeof(Canvas), typeof(Image));");
            Assert.AreEqual(1, v.Count, "未登记文件不得借例外目录逃逸");
        }

        [Test]
        public void 正例_注释里的违例不判违规()
        {
            var v = VisualConstructionScanner.ScanSource("Assets/X/Ok.cs",
                "// 反例：new GameObject(\"x\", typeof(Canvas)) 是被禁止的\n" +
                "var note = \"AddComponent<Image>\";");
            Assert.AreEqual(0, v.Count, "注释与字符串字面量不判违规");
        }

        // ---- 真实代码面扫描 ----

        [Test]
        public void 真实代码_运行时无未登记的视觉构建()
        {
            string root = ProjectRoot();
            // 覆盖全部含视觉构建的目录。流程（唯一例外 ProcedureError 的所在地）在 `App/Procedure`，
            // 此处**必须同步**——例外只对"被扫到的文件"生效，漏扫该目录等于让唯一例外与整个目录一起失去把守。
            List<VisualConstructionScanner.Violation> found =
                VisualConstructionScanner.ScanDirectory(root,
                    "Assets/LiteClient/Runtime", "Assets/LiteGame/App",
                    "Assets/LiteFramework/Scripts", "Assets/LiteView");

            if (found.Count > 0)
                Assert.Fail("运行时视觉构建违规（§7 视觉单一来源）:\n" +
                            string.Join("\n", found.Select(v => v.ToString())));
        }

        /// <summary>工程根 = 本文件的 Assets 之上（EditMode 下 Application.dataPath 为 <root>/Assets）。</summary>
        private static string ProjectRoot()
            => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
    }
}
