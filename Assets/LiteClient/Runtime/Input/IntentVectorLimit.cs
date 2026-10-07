namespace LiteClient
{
    /// <summary>
    /// 意图向量的闸门边界守卫（《角色状态与动作专项设计》§3"长度 ≤1 契约"的浮点边界收口）。
    ///
    /// **为什么需要它**：服务器 <c>RoomServer.Runtime.InputGate.Store</c> 按"Move/Aim 分量与长度平方
    /// 均 ≤ 1"严格判非法（限值 <c>LiteNet.Protocol.ProtocolConstants.VectorLengthSquaredLimit = 1f</c>，
    /// 超限即**整帧丢弃**——R0-P0-3 数值边界，超限帧不得进入权威状态）。而设备源的归一化本身是
    /// 浮点运算：<c>inv = 1/√m</c> 后两个分量各自舍入，长度平方落在 <c>1±1ulp</c>（实测
    /// <c>(0.9973691, -0.0724914148)</c> 归一化结果长度平方 = <c>1.00000012</c>）。落在 +1ulp 一侧时
    /// **每一帧**都被闸门当非法向量拒收——权威端按空输入执行，本地继续预测，表现为
    /// "移不动/橡皮筋"；鼠标方向与相机 yaw 决定舍入落哪边，故症状随准星/镜头位置间歇出现。
    ///
    /// **为什么是收缩而不是再归一化**：再除一次 √m 的舍入仍可能落回 +1ulp（不可证明 ≤1）；
    /// 乘真实余量（1-1e-6）后长度平方 ≈ 0.999998，与限值间的裕量比舍入误差大两个数量级，
    /// 可证明过闸。move/aim 是方向量，幅度只是"≤1"的上限约定，1e-6 级收缩不影响语义。
    ///
    /// **边界**：只收"贴着限值的舍入噪声"，**不做完整归一化**——大幅越界是设备源的实现缺陷，
    /// 应由闸门拒收暴露，不在此掩盖（长度 ≤1 的归一化职责在设备源，见 InputService 采样路径注释）。
    /// 纯 C# 无引擎依赖，与 InputService 同口径源链接进 L1 测试。
    /// </summary>
    public static class IntentVectorLimit
    {
        /// <summary>越界收缩因子（1-1e-6：收缩后长度平方 ≈ 1-2e-6，裕量远大于舍入误差 ~1e-7）。</summary>
        private const float ShrinkFactor = 0.999999f;

        /// <summary>
        /// 确保二维向量过闸（长度平方 ≤ 1）。仅当越界时按 <see cref="ShrinkFactor"/> 收缩；
        /// 界内值逐位不动（模拟量的部分偏移是真实输入，不得改写）。判据与
        /// <c>InputGate.Store</c> 同式同序（<c>x*x + z*z</c> 与 <c>1f</c> 比较，无超越函数）。
        ///
        /// 现状只有移动向量走本口——瞄准已收敛为 AimPoint 世界点（非长度受限向量，
        /// 服务器侧改由距离闸约束；《固定斜视角射击方案专项设计》§4），原三维重载已退役。
        /// </summary>
        public static void EnsureWithinLengthLimit(ref float x, ref float z)
        {
            if (x * x + z * z <= 1f) return;
            x *= ShrinkFactor;
            z *= ShrinkFactor;
        }
    }
}
