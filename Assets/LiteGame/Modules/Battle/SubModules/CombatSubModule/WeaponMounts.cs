using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 武器挂载点解析（激光/枪口火光等"装在武器上"的表现件共用——`Weapon_Rifle/Muzzle`：
    /// 美术锚点单源，见《固定斜视角射击方案专项设计》§6 激光行）。**只读视图层级**：
    /// 找不到（灰盒/裸 prefab 形态）返回 null——调用方静默退化，不造第二视觉源。
    /// </summary>
    internal static class WeaponMounts
    {
        /// <summary>在视图层级里按名深找 Muzzle 挂点（Transform.Find 只认全路径——按名遍历）。</summary>
        public static Transform FindMuzzle(Transform viewRoot)
            => viewRoot == null ? null : FindDescendant(viewRoot, "Muzzle");

        private static Transform FindDescendant(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
