using UnityEngine;

namespace LiteGameNext.Platform.Ui
{
    /// <summary>页面控件绑定名：挂在控件对象上，PageBase 创建期扫描一次建立 名字→控件 表（不生成代码）。</summary>
    [DisallowMultipleComponent]
    public sealed class BindName : MonoBehaviour
    {
        [SerializeField] private string _name = string.Empty;

        /// <summary>控件名；同一页面内必须唯一，空名在扫描时跳过。</summary>
        public string Name
        {
            get => _name;
            set => _name = value;
        }
    }
}
