using System;
using System.Collections.Generic;
using System.Globalization;
using LiteGame.UI;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// Lua 行表 → <see cref="IUIListRows"/>（G6）：行字段**手工展开**（Bridge.Data §1d 同判据——
    /// Lua 不触碰 C# 类型，行数据由 Lua 侧持有）。行形：
    /// <c>rows[i] = { key="Room-A", Txt_RoomName="Room-A", Txt_RoomState={ key="UI.Lobby.RoomState", arg0=2, arg1=8 } }</c>
    /// ——字段名 = **条目子节点名**；值为字符串/数字 = 数据直写（不随语言刷新），值为表（带 key）= 本地化文案，
    /// 参数走具名 <c>arg0..arg3</c>（与 self.ui 的 LText 参数口径一致；**定长不用变长表**——
    /// xLua 对嵌套数组的映射语义不由本项目控制，见 <see cref="LuaBehaviourAdapter.MaxLuaFormatArgs"/>）。
    /// 字段名为 Lua 侧任意插入序（GetKeys 无序）——投影按子节点名寻址，与顺序无关。
    /// </summary>
    internal sealed class LuaListRows : IUIListRows
    {
        private readonly LuaTable _rows;

        public LuaListRows(LuaTable rows)
        {
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public int Count => _rows.Length;

        public string KeyAt(int index) => RowAt(index).Get<string, string>("key") ?? string.Empty;

        public IReadOnlyList<UIListField> FieldsAt(int index)
        {
            LuaTable row = RowAt(index);
            var fields = new List<UIListField>(8);
            // ForEach 是 xLua 的线程安全遍历口（GetKeys<T> 已标 obsolete: not thread safe）
            row.ForEach<string, object>((node, value) =>
            {
                if (string.Equals(node, "key", StringComparison.Ordinal)) return;   // 行 key 不投影为字段
                if (value is LuaTable spec)
                {
                    string key = spec.Get<string, string>("key");
                    if (string.IsNullOrEmpty(key)) return;      // 空表/缺 key = 该字段不显（行协议允许留空）
                    fields.Add(new UIListField(node, key, FormatArgs(spec)));
                }
                else if (value != null)
                {
                    fields.Add(new UIListField(node, ToDisplayText(value)));
                }
            });
            return fields.Count == 0 ? Array.Empty<UIListField>() : fields;
        }

        private LuaTable RowAt(int index) => _rows.Get<int, LuaTable>(index + 1);      // Lua 数组 1 基

        /// <summary>取得行下标的 LuaTable（越界/非表 → 空表；不抛——滚动路径不炸壳）。</summary>
        private static object[] FormatArgs(LuaTable spec)
        {
            var args = new List<object>(LuaBehaviourAdapter.MaxLuaFormatArgs);
            for (int i = 0; i < LuaBehaviourAdapter.MaxLuaFormatArgs; i++)
            {
                object v = spec.Get<string, object>("arg" + i);
                if (v == null) break;
                args.Add(v);
            }
            return args.Count == 0 ? null : args.ToArray();
        }

        /// <summary>数值显示恒 InvariantCulture（区域设置的逗号小数点是跨设备显示漂移源）。</summary>
        private static string ToDisplayText(object value)
        {
            if (value is string s) return s;
            if (value is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
