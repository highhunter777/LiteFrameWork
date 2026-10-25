using System;
using System.Collections.Generic;
using System.Globalization;
using LiteClient.UI;
using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>
    /// 颜色令牌的 8bit 编解码单源（schema `colorValue`：`#RRGGBB`/`#RRGGBBAA`）。
    /// 规范序列化与 SO 生成都走这里，避免"JSON 写一套、资产读一套"。
    /// </summary>
    public static class UiThemeColorHex
    {
        /// <summary>是否合法（6 或 8 位 hex，`#` 前缀）。</summary>
        public static bool IsValid(string hex)
        {
            return TryParse(hex, out _);
        }

        /// <summary>解析为 `Color32`；非法返回 false（不猜默认值）。</summary>
        public static bool TryParse(string hex, out Color32 value)
        {
            value = default;
            if (string.IsNullOrEmpty(hex)) return false;
            if (hex[0] != '#') return false;
            string body = hex.Substring(1);
            if (body.Length != 6 && body.Length != 8) return false;
            if (!int.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int raw)) return false;
            if (body.Length == 6) raw = (raw << 8) | 0xFF;
            value = new Color32(
                (byte)((raw >> 24) & 0xFF),
                (byte)((raw >> 16) & 0xFF),
                (byte)((raw >> 8) & 0xFF),
                (byte)(raw & 0xFF));
            return true;
        }

        /// <summary>规范输出（始终 8 位，大写 hex）。</summary>
        public static string Format(Color32 value)
        {
            return "#" + value.r.ToString("X2") + value.g.ToString("X2")
                 + value.b.ToString("X2") + value.a.ToString("X2");
        }

        /// <summary>浮点数组（0..1）按 `Mathf.RoundToInt(channel*255)` 量化——生成/夹具共用的取整单源。</summary>
        public static Color32 Quantize(float r, float g, float b, float a)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
        }
    }
}
