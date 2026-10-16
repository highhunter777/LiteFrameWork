// 一类一文件：非首个 MonoBehaviour 无法序列化进 prefab
using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>下拉选择封装：SetOptions 重建选项；onIndexChanged 出索引。</summary>
    public class UIDropdownWrap : MonoBehaviour
    {
        public TMP_Dropdown Dropdown;
        public event Action<int> OnIndexChanged;

        private void Awake()
        {
            if (Dropdown == null) Dropdown = GetComponent<TMP_Dropdown>();
            if (Dropdown != null) Dropdown.onValueChanged.AddListener(i => OnIndexChanged?.Invoke(i));
        }

        public void SetOptions(IReadOnlyList<string> options, int selected = 0)
        {
            if (Dropdown == null) return;
            Dropdown.ClearOptions();
            Dropdown.AddOptions(new List<string>(options));
            Dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, options.Count - 1));
        }

        /// <summary>设当前值（程序 Set——不触发 onValueChanged；越界自行收敛）。</summary>
        public void SetValue(int index)
        {
            if (Dropdown == null || Dropdown.options.Count == 0) return;
            Dropdown.SetValueWithoutNotify(Mathf.Clamp(index, 0, Dropdown.options.Count - 1));
            Dropdown.RefreshShownValue();
        }

        public int Selected => Dropdown != null ? Dropdown.value : 0;
    }

}
