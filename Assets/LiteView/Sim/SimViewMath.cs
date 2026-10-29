using LiteSim;
using UnityEngine;

namespace LiteView
{
    /// <summary>Sim 位姿 → 视图 Transform 的落位数学（位置 1:1，Yaw 恒差 90°）。</summary>
    internal static class SimViewMath
    {
        public static void Place(Transform t, in SimVector3 pos, float yaw)
        {
            t.position = new Vector3(pos.X, pos.Y, pos.Z);
            t.rotation = FacingRotation(yaw);
        }

        public static Quaternion FacingRotation(float yaw)
            => Quaternion.Euler(0f, 90f - yaw * Mathf.Rad2Deg, 0f);
    }
}
