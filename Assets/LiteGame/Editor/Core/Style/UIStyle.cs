using UnityEngine;

namespace LiteGame.EditorTools.UI
{
    /// <summary>UI 颜色工具：颜色距离（旧名读口已退役——颜色一律经 <see cref="UITokenCatalog"/> 按令牌 ID 读取）。</summary>
    public static class UIStyle
    {
        /// <summary>颜色与 token 的距离（逐通道差的平方和；alpha 参与比较——灰盒底色 alpha 是语义的一部分）。</summary>
        public static float Distance(Color a, Color b)
        {
            float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
            return dr * dr + dg * dg + db * db + da * da;
        }
    }
}
