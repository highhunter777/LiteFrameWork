//------------------------------------------------------------
// LiteCodeGen - 独立版绑定代码生成
//------------------------------------------------------------
using System.Text;

namespace LiteCodeGen.EditorTools
{
    /// <summary>极简缩进代码写器,负责生成 C# 文本。</summary>
    public sealed class CodeBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private int _indent;

        /// <summary>写入一行(带当前缩进);text 为空则写空行。</summary>
        public void Line(string text = "")
        {
            if (!string.IsNullOrEmpty(text))
            {
                for (var i = 0; i < _indent; i++)
                {
                    _sb.Append("    ");
                }

                _sb.Append(text);
            }

            _sb.Append('\n');
        }

        /// <summary>写入一行代码块开始符并增加缩进。</summary>
        public void Open()
        {
            Line("{");
            _indent++;
        }

        /// <summary>减少缩进并写入代码块结束符。</summary>
        public void Close()
        {
            _indent--;
            Line("}");
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}
