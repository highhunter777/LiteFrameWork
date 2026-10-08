using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LiteSim
{
    /// <summary>
    /// 战斗配置规范化摘要（《商业级通用服务端框架总设计》§5 P0-5：协议字段 configHash 的**唯一合法来源**——
    /// SHA-256；禁止运行时哈希（GetHashCode 有进程随机种子，跨进程必不一致）进入协议）。
    ///
    /// 规范化规则（固定，两端同源——本类同时是服务端 StartGame.ConfigHash 与客户端校验的单源）：
    /// - 入参 = <see cref="CombatValues"/> 实例（技术债 #1 根治后不再读全局——摘要按实例计算，
    ///   **字段序/格式与旧口径逐字节一致**：同值同摘要，联机身份判据不变）；
    /// - 字段顺序固定：实例装载面 6 字段 + **派生 1 字段** `AimMoveSpeed`
    ///   + **窗长常量 1 字段** `FireStanceFrames`（Sim 消费它限速，进联机身份）
    ///   + **逻辑枪口 3 字段** `MuzzleOffsetForward/Right/Height`（子弹出射点＝本体+朝向系
    ///   **烘焙常量偏移**——`MuzzleBake.g.cs`：prefab Muzzle 锚点 @ AimIdle t=0；表化计划随 tb_weapon，
    ///   故进联机身份）
    ///   + **判定几何 3 字段** `HitscanRadius/HitscanHeight/HeadshotRadius`（烘焙/导出常量——
    ///   两端同值由生成文件保证，仍进摘要兜底"改了生成文件只改一端"）
    ///   + **腰射散布倍率 1 字段** `HipSpreadFactor`（判定参数——per-weapon 腰射列表化计划随数值
    ///   调参批，与 FireStanceFrames 同判据进联机身份）；
    /// - float 用 InvariantCulture "R"（往返）格式——跨文化稳定（de-DE 的小数逗号不会改变摘要）；
    /// - 字段以 '\n' 分隔、无空白填充；数值后不带单位。
    /// 取 SHA-256 低 32 位作 proto uint32（StartGame.ConfigHash 字段位宽）。
    /// 语义边界：buildHash 已覆盖"代码+协议+表数据"的构建一致性；本摘要覆盖**装载后运行态数值**——
    /// 表装载失败/漏装载在两端只表现为默认值一致与否，本摘要可当场发现。
    /// </summary>
    public static class CombatConfigDigest
    {
        /// <summary>规范化文本（诊断/测试用：同一 <paramref name="values"/> 两端应逐字节一致）。</summary>
        public static string CanonicalText(in CombatValues values)
        {
            var sb = new StringBuilder(128);
            sb.Append(values.MoveSpeed.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.Gravity.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.HitscanRange.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.HitscanRadius.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.HitscanHeight.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.HeadshotRadius.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.BaseDamage.ToString(CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.DamageSpread.ToString(CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.EntityHp.ToString(CultureInfo.InvariantCulture)).Append('\n')
              .Append(values.AimMoveSpeed.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.FireStanceFrames.ToString(CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.MuzzleOffsetForward.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.MuzzleOffsetRight.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.MuzzleOffsetHeight.ToString("R", CultureInfo.InvariantCulture)).Append('\n')
              .Append(CombatConfig.HipSpreadFactor.ToString("R", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>SHA-256 低 32 位（proto uint32 口径；跨进程一致——同一份实例值必得同一摘要）。</summary>
        public static uint Compute(in CombatValues values)
        {
            return Compute(CanonicalText(values));
        }

        /// <summary>给定规范化文本的摘要（<see cref="CanonicalText(in CombatValues)"/> 同规文本的摘要入口——
        /// 测试/工具用：两份不同配置文本必得不同摘要，无需构造实例）。</summary>
        public static uint Compute(string canonicalText)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalText);
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToUInt32(hash, 0);
        }
    }
}
