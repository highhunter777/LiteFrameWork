using System;
using System.Collections.Generic;
using LiteFramework.Animation;
using LiteSim;
using cfg;
using LiteGame.Modules.Config;

namespace LiteGame
{
    /// <summary>
    /// 配置表校验（发布闸门）：与 ConfigService 的装载/探针共用同一套语义规则。
    ///
    /// 返回 null = 通过，非 null = 拒绝原因。**不得触碰已发布状态**——调用方在
    /// ConfigSnapshotService.Publish 前调用（失败保留旧版）；候选健康探针也直接调用本类。
    /// </summary>
    public static class ConfigTableValidator
    {
        public static string Validate(Tables candidate)
        {
            if (candidate == null) return "候选表为 null";
            string valuesError = SimConfigMapper.Validate(candidate.Tbcombatnum,
                candidate.Tbmovementconfig, candidate.Tbentityconfig, candidate.Tbcombatgeom);
            if (valuesError != null) return valuesError;
            if (candidate.Tbitemconfig == null || candidate.Tbitemconfig.DataList.Count == 0)
                return "tbitemconfig 空表——表源被改坏或生成物过期";
            if (candidate.Tbweapon == null || candidate.Tbweapon.GetOrDefault(WeaponConfig.DefaultRifleId) == null)
                return "tbweapon 缺默认步枪行（id=0）——武器系统懒装备依赖它";
            foreach (cfg.weapon row in candidate.Tbweapon.DataList)
            {
                if (row.FireMode != WeaponConfig.FireModeAuto && row.FireMode != WeaponConfig.FireModeSemi)
                    return $"tbweapon id={row.Id} 非法 fire_mode：'{row.FireMode}'（合法集 auto/semi——两端同链映射 bool）";
                if (row.Slot < 0 || row.Slot >= SimConfig.WeaponSlotsPerEntity)
                    return $"tbweapon id={row.Id} slot 越界：{row.Slot}（槽数 {SimConfig.WeaponSlotsPerEntity}——切枪装备依赖槽位映射）";
                if (row.Pellets < 1)
                    return $"tbweapon id={row.Id} pellets 非法：{row.Pellets}（≥1——零弹丸＝哑枪，错表必两端分叉）";
            }

            // 动画 Profile 行：翻译边界（类别/通道字符串）+ 全族装载演练——语义校验
            // （速度区间/两栖/回退防环/空绑定）全部前置到启动，错表炸在 Preload 而非对局装配
            if (candidate.Tbanimationprofile == null || candidate.Tbanimationprofile.DataList.Count == 0)
                return "tbanimationprofile 空表——表源被改坏或生成物过期";
            try
            {
                var animRows = AnimationProfileTableMapper.ToRows(candidate.Tbanimationprofile);
                var families = new HashSet<string>();
                foreach (var row in animRows) families.Add(row.ModelFamily);
                foreach (string family in families)
                    AnimationProfileLoader.FromRows(animRows, family);   // 装载演练：结果丢弃，只为把语义错误炸在此处
            }
            catch (Exception ex)
            {
                return $"tbanimationprofile: {ex.Message}";
            }

            // 准心调参表（表现域——不进判定面摘要，行完整性仍是配置错误）：每把武器一行 + 值域
            // （缺行/错值炸 Preload，不在对局内静默丢手感）。static_max_scale 以 1 为基线（<1 会把准心
            // 缩得比本体小）；burst_cap 须 ≥ 静态上限（连发只张开不收紧）
            if (candidate.Tbcrosshair == null || candidate.Tbcrosshair.DataList.Count == 0)
                return "tbcrosshair 空表——表源被改坏或生成物过期";
            foreach (cfg.weapon weapon in candidate.Tbweapon.DataList)
            {
                if (candidate.Tbcrosshair.GetOrDefault(weapon.Id) == null)
                    return $"tbcrosshair 缺武器 id={weapon.Id} 行——每把武器一行（准心手感完整性闸）";
            }
            foreach (cfg.crosshair row in candidate.Tbcrosshair.DataList)
            {
                if (row.StaticMaxScale < 1f)
                    return $"tbcrosshair id={row.Id} static_max_scale 非法：{row.StaticMaxScale}（≥1——1=基线倍率）";
                if (row.BurstCap < row.StaticMaxScale)
                    return $"tbcrosshair id={row.Id} burst_cap={row.BurstCap} 低于 static_max_scale={row.StaticMaxScale}（连发上限须不低于静态上限）";
                if (row.BurstGrowth < 0f || row.Kick < 0f)
                    return $"tbcrosshair id={row.Id} 负值非法：burst_growth={row.BurstGrowth} kick={row.Kick}";
            }

            // 战斗策略表（B3）：规则越界/颜色格式/分带次序/轨道参数——错表炸 Preload，不静默带进对局。
            try
            {
                BattleStrategyProfileFactory.Create(candidate.Tbbattlestrategy);
            }
            catch (Exception ex)
            {
                return $"tbbattlestrategy: {ex.Message}";
            }
            return null;
        }
    }
}
