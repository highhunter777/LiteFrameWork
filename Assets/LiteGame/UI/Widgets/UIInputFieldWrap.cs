using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>输入框封装（M4 §2.5/控件库）：UGUI InputField + 校验 hook（返回 false = 拒绝本次提交）。</summary>
    public class UIInputFieldWrap : MonoBehaviour
    {
        public TMP_InputField Field;
        [Tooltip("提交校验 hook；false = 拒绝并还原")]
        public Func<string, bool> Validator;
        public event Action<string> OnSubmitted;
        public event Action<string> OnValueChanged;

        private string _lastValid;

        private void Awake()
        {
            if (Field == null) Field = GetComponent<TMP_InputField>();
            if (Field == null) return;
            Field.onValueChanged.AddListener(v => OnValueChanged?.Invoke(v));
            Field.onEndEdit.AddListener(v =>
            {
                if (Validator != null && !Validator(v))
                {
                    Field.text = _lastValid ?? string.Empty;     // 拒绝并还原
                    return;
                }
                _lastValid = v;
                OnSubmitted?.Invoke(v);
            });
        }

        public string Value => Field != null ? Field.text : string.Empty;

        public void Set(string value, bool notify = false)
        {
            if (Field == null) return;
            _lastValid = value;
            if (notify) Field.text = value;
            else { Field.text = value; OnValueChanged?.Invoke(value); }
        }
    }

}
