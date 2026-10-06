using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LiteSim
{
    /// <summary>
    /// 两个 <see cref="SimWorldState"/> 的**字段级差异报告**——把"checksum 不符"翻译成
    /// "哪个槽位、哪个字段、从什么变成什么"。
    ///
    /// **为什么需要它**：checksum 是 FNV-1a 折叠值，**不可逆**——它只能回答"是否相同"，
    /// 回答不了"哪里不同"。跨运行时 1-ulp 分歧下，两个世界可能只差一个 float 的最低有效位，
    /// 而 checksum 差异看起来同等巨大。本类逐字段比对（float 经位型比较，**不做容差**——
    /// 确定性分歧恰恰就是位级的分歧，用容差会把要找的答案过滤掉）。
    ///
    /// **覆盖范围与 <see cref="SimChecksum.ComputeChecksum"/> 对齐**：实体槽 + 全部分型表行
    /// + 运行态数组 + Globals/CustomData + Match + 头部（Frame/RngState）。
    /// 新增进 checksum 的字段必须同步扩展本类——否则报告会漏掉它（漏检 = 回到"不知道为什么"）。
    /// </summary>
    public static class SimWorldDiff
    {
        /// <summary>一条字段差异（报告的最小单元）。</summary>
        public readonly struct Difference
        {
            /// <summary>字段路径（如 <c>Entities[3].Pos.Y</c>、<c>Weapons[6].MagAmmo</c>）。</summary>
            public string Path { get; }

            /// <summary>左值（格式化后的字面量；float 附位型十六进制）。</summary>
            public string Left { get; }

            /// <summary>右值（格式化后的字面量；float 附位型十六进制）。</summary>
            public string Right { get; }

            public Difference(string path, string left, string right)
            {
                Path = path;
                Left = left;
                Right = right;
            }

            public override string ToString() => $"{Path}: {Left} != {Right}";
        }

        /// <summary>
        /// 全量逐字段比对。<paramref name="maxDifferences"/> 上限（防一个爆炸性分歧刷爆报告——
        /// 超出时截断并置 <see cref="Report.Truncated"/>）。
        /// </summary>
        public static Report Compare(SimWorldState a, SimWorldState b, int maxDifferences = 64)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var diffs = new List<Difference>();
            bool truncated = false;

            void Add(string path, string left, string right)
            {
                if (diffs.Count >= maxDifferences) { truncated = true; return; }
                diffs.Add(new Difference(path, left, right));
            }

            // ---- 头部 ----
            if (a.Frame != b.Frame) Add("Frame", a.Frame.ToString(CultureInfo.InvariantCulture), b.Frame.ToString(CultureInfo.InvariantCulture));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.RngState != b.RngState) Add("RngState", Hex(a.RngState), Hex(b.RngState));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）

            // ---- 活体位图（槽位存在性差异——先于逐字段，否则"一方有实体一方没有"会刷满字段噪声）----
            for (int w = 0; w < a.AliveBitmap.Length; w++)
                if (a.AliveBitmap[w] != b.AliveBitmap[w])
                    Add($"AliveBitmap[{w}]", Hex(a.AliveBitmap[w]), Hex(b.AliveBitmap[w]));

            // ---- 实体槽（逐字段；仅比对双方都活的槽位——一侧死亡已由位图差异表达）----
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                bool aliveA = a.IsAlive(i), aliveB = b.IsAlive(i);
                if (!aliveA || !aliveB) continue;

                ref EntitySlot ea = ref a.Entities[i];
                ref EntitySlot eb = ref b.Entities[i];
                string p = $"Entities[{i}]";

                if (ea.Id != eb.Id) Add($"{p}.Id", Hex(ea.Id), Hex(eb.Id));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                F(p, "Pos.X", ea.Pos.X, eb.Pos.X, Add);
                F(p, "Pos.Y", ea.Pos.Y, eb.Pos.Y, Add);
                F(p, "Pos.Z", ea.Pos.Z, eb.Pos.Z, Add);
                F(p, "Vel.X", ea.Vel.X, eb.Vel.X, Add);
                F(p, "Vel.Y", ea.Vel.Y, eb.Vel.Y, Add);
                F(p, "Vel.Z", ea.Vel.Z, eb.Vel.Z, Add);
                F(p, "Yaw", ea.Yaw, eb.Yaw, Add);
                if (ea.Hp != eb.Hp) Add($"{p}.Hp", I(ea.Hp), I(eb.Hp));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.Flags != eb.Flags) Add($"{p}.Flags", Hex(ea.Flags), Hex(eb.Flags));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.Shield != eb.Shield) Add($"{p}.Shield", I(ea.Shield), I(eb.Shield));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.Kills != eb.Kills) Add($"{p}.Kills", I(ea.Kills), I(eb.Kills));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.Deaths != eb.Deaths) Add($"{p}.Deaths", I(ea.Deaths), I(eb.Deaths));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.SelectedWeapon != eb.SelectedWeapon) Add($"{p}.SelectedWeapon", I(ea.SelectedWeapon), I(eb.SelectedWeapon));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.FireStanceFrames != eb.FireStanceFrames) Add($"{p}.FireStanceFrames", I(ea.FireStanceFrames), I(eb.FireStanceFrames));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.FaceExitTurning != eb.FaceExitTurning) Add($"{p}.FaceExitTurning", I(ea.FaceExitTurning), I(eb.FaceExitTurning));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                if (ea.CorpseFrames != eb.CorpseFrames) Add($"{p}.CorpseFrames", I(ea.CorpseFrames), I(eb.CorpseFrames));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            }

            // ---- 平行行表（分型表；行有效性与槽位活体一致，同槽位比对）----
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!a.IsAlive(i) || !b.IsAlive(i)) continue;

                if (a.Resources[i] != b.Resources[i]) Add($"Resources[{i}]", I(a.Resources[i]), I(b.Resources[i]));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）

                for (int w = 0; w < SimConfig.WeaponSlotsPerEntity; w++)
                {
                    int k = i * SimConfig.WeaponSlotsPerEntity + w;
                    ref WeaponRuntime wa = ref a.Weapons[k];
                    ref WeaponRuntime wb = ref b.Weapons[k];
                    string p = $"Weapons[{k}]";
                    if (wa.WeaponDefId != wb.WeaponDefId) Add($"{p}.WeaponDefId", I(wa.WeaponDefId), I(wb.WeaponDefId));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.MagAmmo != wb.MagAmmo) Add($"{p}.MagAmmo", I(wa.MagAmmo), I(wb.MagAmmo));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.ReserveAmmo != wb.ReserveAmmo) Add($"{p}.ReserveAmmo", I(wa.ReserveAmmo), I(wb.ReserveAmmo));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.State != wb.State) Add($"{p}.State", wa.State.ToString(), wb.State.ToString());   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.NextFireFrame != wb.NextFireFrame) Add($"{p}.NextFireFrame", I(wa.NextFireFrame), I(wb.NextFireFrame));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.ReloadEndFrame != wb.ReloadEndFrame) Add($"{p}.ReloadEndFrame", I(wa.ReloadEndFrame), I(wb.ReloadEndFrame));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.EquipEndFrame != wb.EquipEndFrame) Add($"{p}.EquipEndFrame", I(wa.EquipEndFrame), I(wb.EquipEndFrame));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (wa.ShotSeq != wb.ShotSeq) Add($"{p}.ShotSeq", I(wa.ShotSeq), I(wb.ShotSeq));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                }

                for (int s = 0; s < SimConfig.ActionSlotsPerEntity; s++)
                {
                    int k = i * SimConfig.ActionSlotsPerEntity + s;
                    ref ActionRuntime aa = ref a.Actions[k];
                    ref ActionRuntime ab = ref b.Actions[k];
                    string p = $"Actions[{k}]";
                    if (aa.ActionId != ab.ActionId) Add($"{p}.ActionId", I(aa.ActionId), I(ab.ActionId));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (aa.StartFrame != ab.StartFrame) Add($"{p}.StartFrame", I(aa.StartFrame), I(ab.StartFrame));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (aa.Phase != ab.Phase) Add($"{p}.Phase", aa.Phase.ToString(), ab.Phase.ToString());   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (aa.Charges != ab.Charges) Add($"{p}.Charges", I(aa.Charges), I(ab.Charges));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (aa.CooldownEnd != ab.CooldownEnd) Add($"{p}.CooldownEnd", I(aa.CooldownEnd), I(ab.CooldownEnd));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (aa.CastToken != ab.CastToken) Add($"{p}.CastToken", I(aa.CastToken), I(ab.CastToken));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                }

                for (int s = 0; s < SimConfig.StatusSlotsPerEntity; s++)
                {
                    int k = i * SimConfig.StatusSlotsPerEntity + s;
                    ref StatusSlotData sa = ref a.Status[k];
                    ref StatusSlotData sb = ref b.Status[k];
                    string p = $"Status[{k}]";
                    if (sa.EffectId != sb.EffectId) Add($"{p}.EffectId", I(sa.EffectId), I(sb.EffectId));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (sa.EndFrame != sb.EndFrame) Add($"{p}.EndFrame", I(sa.EndFrame), I(sb.EndFrame));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (sa.Param != sb.Param) Add($"{p}.Param", I(sa.Param), I(sb.Param));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                }

                for (int g = 0; g < SimConfig.MatchBagSlotsPerEntity; g++)
                {
                    int k = i * SimConfig.MatchBagSlotsPerEntity + g;
                    ref MatchBagSlot ba = ref a.MatchBag[k];
                    ref MatchBagSlot bb = ref b.MatchBag[k];
                    string p = $"MatchBag[{k}]";
                    if (ba.ItemDefId != bb.ItemDefId) Add($"{p}.ItemDefId", I(ba.ItemDefId), I(bb.ItemDefId));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (ba.Count != bb.Count) Add($"{p}.Count", I(ba.Count), I(bb.Count));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                    if (ba.QuickSlot != bb.QuickSlot) Add($"{p}.QuickSlot", I(ba.QuickSlot), I(bb.QuickSlot));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
                }
            }

            // ---- 平面 blob ----
            Blob("Globals", a.Globals, b.Globals, Add, ref truncated, maxDifferences);
            Blob("CustomData", a.CustomData, b.CustomData, Add, ref truncated, maxDifferences);

            // ---- 比赛状态 ----
            if (a.Match.Phase != b.Match.Phase) Add("Match.Phase", I(a.Match.Phase), I(b.Match.Phase));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.Match.Team != b.Match.Team) Add("Match.Team", I(a.Match.Team), I(b.Match.Team));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.Match.Score != b.Match.Score) Add("Match.Score", I(a.Match.Score), I(b.Match.Score));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.Match.Timer != b.Match.Timer) Add("Match.Timer", I(a.Match.Timer), I(b.Match.Timer));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.Match.Round != b.Match.Round) Add("Match.Round", I(a.Match.Round), I(b.Match.Round));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
            if (a.Match.Winner != b.Match.Winner) Add("Match.Winner", Hex(a.Match.Winner), Hex(b.Match.Winner));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）

            return new Report(diffs, truncated, a, b);
        }

        /// <summary>差异报告（差异列表 + 两侧口径摘要 + 格式化输出）。</summary>
        public sealed class Report
        {
            private readonly List<Difference> _differences;

            internal Report(List<Difference> differences, bool truncated, SimWorldState left, SimWorldState right)
            {
                _differences = differences;
                Truncated = truncated;
                LeftChecksum = SimChecksum.ComputeChecksum(left);
                RightChecksum = SimChecksum.ComputeChecksum(right);
                LeftPublicChecksum = SimChecksum.ComputePublicChecksum(left);
                RightPublicChecksum = SimChecksum.ComputePublicChecksum(right);
            }

            /// <summary>字段差异（上限截断后）。</summary>
            public IReadOnlyList<Difference> Differences => _differences;

            /// <summary>是否因达上限而截断（真 = 实际差异更多，报告不完整）。</summary>
            public bool Truncated { get; }

            public uint LeftChecksum { get; }
            public uint RightChecksum { get; }

            /// <summary>公共口径两侧值——**是否进公共口径**决定这次分歧会不会触发线上和解。</summary>
            public uint LeftPublicChecksum { get; }
            public uint RightPublicChecksum { get; }

            /// <summary>两侧是否**全量口径**相同（含私有面）。</summary>
            public bool FullEqual => LeftChecksum == RightChecksum;   // lint-allow R3（uint 位级判等，非浮点精度比较）

            /// <summary>两侧是否**公共口径**相同（线上和解锚点）。</summary>
            public bool PublicEqual => LeftPublicChecksum == RightPublicChecksum;   // lint-allow R3（uint 位级判等，非浮点精度比较）

            /// <summary>
            /// 人读报告。**首行给出定性**：是纯私有面分歧（可容忍，客户端重建不了）还是公共面分歧
            /// （线上会触发逐帧和解——必须修）。这一行就是排查的起点。
            /// </summary>
            public string ToText()
            {
                var sb = new StringBuilder();
                if (FullEqual)
                {
                    sb.Append("无差异（全量口径相同）");
                    return sb.ToString();
                }

                sb.Append(PublicEqual
                    ? "私有面分歧（公共口径相同——不触发线上和解，但重放/回放对账会暴露）"
                    : "**公共面分歧**（公共口径不同——线上会逐帧和解，必须修）");
                sb.Append('\n');
                sb.Append("  full   : ").Append(Hex(LeftChecksum)).Append(" != ").Append(Hex(RightChecksum)).Append('\n');
                sb.Append("  public : ").Append(Hex(LeftPublicChecksum)).Append(" != ").Append(Hex(RightPublicChecksum)).Append('\n');
                sb.Append("  字段差异 ").Append(_differences.Count).Append(" 处");
                if (Truncated) sb.Append("（已达上限，实际更多——调大 maxDifferences）");
                sb.Append("：\n");
                for (int i = 0; i < _differences.Count; i++)
                    sb.Append("    ").Append(_differences[i]).Append('\n');
                return sb.ToString();
            }

            public override string ToString() => ToText();
        }

        // ---- 内部 ----

        private delegate void AddFn(string path, string left, string right);

        /// <summary>float 按**位型**比对（确定性分歧就是位级分歧——不做容差，容差会把答案过滤掉）。
        /// 报告同时给字面量与位型十六进制：字面量看不出差别时（1-ulp）位型就是证据。</summary>
        private static void F(string prefix, string field, float left, float right, AddFn add)
        {
            int bl = BitConverter.SingleToInt32Bits(left);
            int br = BitConverter.SingleToInt32Bits(right);
            if (bl != br) add($"{prefix}.{field}", FloatText(left, bl), FloatText(right, br));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
        }

        private static string FloatText(float v, int bits)
            => v.ToString("R", CultureInfo.InvariantCulture) + "(0x" + bits.ToString("X8", CultureInfo.InvariantCulture) + ")";

        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static string Hex(long v) => "0x" + v.ToString("X16", CultureInfo.InvariantCulture);

        private static string Hex(ulong v) => "0x" + v.ToString("X16", CultureInfo.InvariantCulture);

        private static string Hex(uint v) => "0x" + v.ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>平面 blob 逐字节比对（差异只报**前几处**——blob 一致性崩溃时逐字节刷报告没有价值）。</summary>
        private static void Blob(string name, byte[] a, byte[] b, AddFn add, ref bool truncated, int max)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] == b[i]) continue;
                // blob 一致性崩溃时逐字节刷报告没有价值：只报前若干处即收敛（整体结论已由头部给出）
                add($"{name}[{i}]", "0x" + a[i].ToString("X2", CultureInfo.InvariantCulture),
                    "0x" + b[i].ToString("X2", CultureInfo.InvariantCulture));
                if (i > max * 4) { truncated = true; return; }
            }
            if (a.Length != b.Length) add($"{name}.Length", I(a.Length), I(b.Length));   // lint-allow R3（整型/枚举判等，非浮点精度比较——诊断工具不做容差）
        }
    }
}
