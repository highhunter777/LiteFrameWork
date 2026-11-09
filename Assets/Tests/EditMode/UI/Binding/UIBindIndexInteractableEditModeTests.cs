using System;
using System.Collections.Generic;
using LiteFramework.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 受控 API <c>SetInteractable</c> 的三解析面：<c>Selectable</c> → <see cref="UIWidget"/> → <see cref="IUIInteractable"/>。
    /// </summary>
    [Category(LiteTesting.TestCategory.Unit)]
    public sealed class UIBindIndexInteractableEditModeTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void 封装件控件经接口透传可交互()
        {
            var dropdown = Spawn<TMP_Dropdown>(out var dropdownGo);
            var dropdownWrap = dropdownGo.AddComponent<UIDropdownWrap>();
            dropdownWrap.Dropdown = dropdown;

            var slider = Spawn<Slider>(out var sliderGo);
            var sliderWrap = sliderGo.AddComponent<UISliderWrap>();
            sliderWrap.Slider = slider;

            var toggle = Spawn<Toggle>(out var toggleGo);
            var toggleWrap = toggleGo.AddComponent<UIToggleWrap>();
            toggleWrap.Toggle = toggle;

            var field = Spawn<TMP_InputField>(out var fieldGo);
            var fieldWrap = fieldGo.AddComponent<UIInputFieldWrap>();
            fieldWrap.Field = field;

            var index = new UIBindIndex(new Dictionary<string, Component>
            {
                { "Dropdown_Test", dropdownWrap },
                { "Slider_Test", sliderWrap },
                { "Toggle_Test", toggleWrap },
                { "InputField_Test", fieldWrap },
            });

            foreach (string name in new[] { "Dropdown_Test", "Slider_Test", "Toggle_Test", "InputField_Test" })
                index.SetInteractable(name, false);

            Assert.IsFalse(dropdown.interactable, "下拉封装禁用到 inner TMP 下拉");
            Assert.IsFalse(slider.interactable, "滑条封装禁用到 inner Slider");
            Assert.IsFalse(toggle.interactable, "开关封装禁用到 inner Toggle");
            Assert.IsFalse(field.interactable, "输入框封装禁用到 inner TMP 输入框");

            foreach (string name in new[] { "Dropdown_Test", "Slider_Test", "Toggle_Test", "InputField_Test" })
                index.SetInteractable(name, true);

            Assert.IsTrue(dropdown.interactable && slider.interactable && toggle.interactable && field.interactable,
                "恢复可交互同为透传");
        }

        [Test]
        public void 非控件目标仍fail_fast()
        {
            var go = new GameObject("plain", typeof(RectTransform));
            _spawned.Add(go);
            var index = new UIBindIndex(new Dictionary<string, Component> { { "Cot_Plain", go.transform } });

            Assert.Throws<InvalidOperationException>(() => index.SetInteractable("Cot_Plain", false),
                "既非 Selectable、也非控件、也没实现接口——必须抛（不静默吞）");
        }

        private T Spawn<T>(out GameObject go) where T : Component
        {
            go = new GameObject(typeof(T).Name, typeof(RectTransform));
            _spawned.Add(go);
            return go.AddComponent<T>();
        }
    }
}
