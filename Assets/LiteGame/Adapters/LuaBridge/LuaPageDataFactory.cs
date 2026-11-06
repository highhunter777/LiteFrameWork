using System;
using System.Collections.Generic;
using LiteFramework.UI;
using LiteGame.Modules.UI;
using XLua;

namespace LiteGame
{
    /// <summary>
    /// 页面数据投射的实现（`IPageDataFactory` 端口 → Lua 表）：中立数据树逐层展开为 LuaTable， 行数组展开为 1 基序数组。
    /// </summary>
    public sealed class LuaPageDataFactory : IPageDataFactory
    {
        private readonly Func<LuaEnv> _env;

        public LuaPageDataFactory(Func<LuaEnv> env)
            => _env = env ?? throw new ArgumentNullException(nameof(env));

        /// <inheritdoc />
        public IUIData Create(PageData data)
        {
            if (data == null) return null;
            if (data.IsEmpty) return null;                  // 空树 = 页面无数据（与"传 null"同口径）
            return new LuaUIData(Build(_env(), data));
        }

        private static LuaTable Build(LuaEnv env, PageData data)
        {
            LuaTable table = env.NewTable();
            IReadOnlyList<KeyValuePair<string, object>> values = data.Values;
            for (int i = 0; i < values.Count; i++)
            {
                string key = values[i].Key;
                object value = values[i].Value;
                switch (value)
                {
                    case bool flag: table.Set(key, flag); break;
                    case int number: table.Set(key, number); break;
                    case string text: table.Set(key, text); break;
                    case PageData child: table.Set(key, Build(env, child)); break;
                    case IReadOnlyList<PageData> rows:
                        table.Set(key, BuildRows(env, rows));
                        break;
                    default:
                        throw new InvalidOperationException("页面数据 '" + key + "' 的值类型不受支持："
                            + (value == null ? "null" : value.GetType().FullName)
                            + "（只允许 bool/int/string/PageData/行数组）");
                }
            }
            return table;
        }

        private static LuaTable BuildRows(LuaEnv env, IReadOnlyList<PageData> rows)
        {
            LuaTable array = env.NewTable();
            for (int i = 0; i < rows.Count; i++) array.Set(i + 1, Build(env, rows[i]));
            return array;
        }
    }
}
