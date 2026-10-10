using UnityEngine;

namespace LiteView.Animation
{
    /// <summary>
    /// 持枪约束偏移（纯表现参数资产，由烘焙工具生成——同 MuzzleBake 套路但落资产而非常量）：
    /// 换弹上半身叠加期间，武器骨位姿按本偏移跟随躯干骨。**为什么要它**：换弹片段里躯干有
    /// 34–45° 的前倾后仰而枪骨仅 22°，观感是"身体在动、枪几乎不动"；跟手不可行——换弹段手部
    /// 位移 21 cm / 70°，会把枪甩出去。故跟随目标取躯干（胸椎）。
    ///
    /// **偏移语义**：枪骨相对跟随骨的局部变换，在参考姿态（AimIdle t=0 = 持枪位）下烘焙。
    /// **幅度缩放** <see cref="TorsoWeight"/>：0 = 完全用片段自己的枪骨曲线（等于不约束），
    /// 1 = 完全跟随躯干；中间值按权重混合（每帧在动画 Evaluate 之后写，故观感可实时调）。
    ///
    /// **不改动画契约**：本资产只是"换弹期这根骨的姿态归谁"，不进 Profile 与通道仲裁。
    /// </summary>
    [CreateAssetMenu(menuName = "LiteView/武器持枪偏移", fileName = "WeaponGripOffset")]
    public sealed class WeaponGripOffset : ScriptableObject
    {
        /// <summary>跟随骨路径（相对 Animator 根，斜杠分层——与片段曲线路径同规）。
        /// 当前角色族为胸椎 root/pelvis/spine_01/spine_02。</summary>
        [SerializeField] private string followBonePath = "";

        /// <summary>武器骨路径（相对 Animator 根；当前角色族为 root/add_weapon_r——
        /// 枪模型 Weapon_Rifle 是它的子节点，枪口火光/激光挂点随之跟随）。</summary>
        [SerializeField] private string weaponBonePath = "";

        /// <summary>枪骨相对跟随骨的局部位置（持枪位偏移，参考姿态烘焙）。</summary>
        [SerializeField] private Vector3 localPosition;

        /// <summary>枪骨相对跟随骨的局部旋转（欧拉角，参考姿态烘焙）。</summary>
        [SerializeField] private Vector3 localEulerAngles;

        /// <summary>幅度缩放：0 = 不约束（用片段曲线）、1 = 完全跟随躯干。</summary>
        [SerializeField, Range(0f, 1f)] private float torsoWeight = 1f;

        public string FollowBonePath => followBonePath;
        public string WeaponBonePath => weaponBonePath;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => Quaternion.Euler(localEulerAngles);
        public float TorsoWeight => torsoWeight;

        /// <summary>路径齐备（缺任一路径即不可用——构造期显性失败，不静默无效）。</summary>
        public bool IsComplete => !string.IsNullOrEmpty(followBonePath) && !string.IsNullOrEmpty(weaponBonePath);

        /// <summary>装配期写入（仅烘焙工具调用；运行期只读）。</summary>
        public void Configure(string followPath, string weaponPath, Vector3 localPos, Vector3 localEuler, float weight)
        {
            followBonePath = followPath;
            weaponBonePath = weaponPath;
            localPosition = localPos;
            localEulerAngles = localEuler;
            torsoWeight = Mathf.Clamp01(weight);
        }
    }
}
