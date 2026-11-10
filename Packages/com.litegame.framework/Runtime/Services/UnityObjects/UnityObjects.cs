using UnityEngine;

namespace LiteFramework.Services.UnityObjects
{
    /// <summary>Unity 对象销毁的唯一口径：Play 延迟销毁（不打断当帧），编辑态立即销毁（EditMode 无延迟销毁帧）。</summary>
    public static class UnityObjects
    {
        /// <summary>销毁对象；null（含 Unity fake-null）与已销毁对象为无操作。</summary>
        public static void Destroy(UnityEngine.Object target)
        {
            if (target == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(target); return; }
#endif
            UnityEngine.Object.Destroy(target);
        }
    }
}
