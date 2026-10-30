using System;
using LiteGame.View.DamageNumbers;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 伤害数字逐档材质调色板（《命中反馈与伤害数字专项设计》§4.4 批C）：<see cref="DamageNumberTier"/>
    /// 四档 → 材质（黑描边 + 软影 + 按档光晕；**面部颜色不进材质**——_FaceColor 保持白，颜色归样式
    /// 顶点渐变四角，驱动逐帧写）。
    ///
    /// **构造即完整**——任一档为 null 属装配错误（fail-fast）。"四档不齐"的降级裁决在装配点
    /// （<c>ProcedureBattle</c>：任缺则**整组不注入**，飘字退回底件默认材质）——不存在部分档位的
    /// 混合观感（那会让"为什么这发没光晕"变成不可解释的随机）。
    /// </summary>
    public sealed class DamageNumberMaterialSet
    {
        private readonly Material _caused;
        private readonly Material _causedCrit;
        private readonly Material _received;
        private readonly Material _receivedCrit;

        /// <param name="caused">造成·普通档材质（底件默认材质即此档）。</param>
        /// <param name="causedCrit">造成·爆头档材质（红光晕）。</param>
        /// <param name="received">承受·普通档材质。</param>
        /// <param name="receivedCrit">承受·爆头档材质（深红光晕）。</param>
        public DamageNumberMaterialSet(Material caused, Material causedCrit, Material received, Material receivedCrit)
        {
            _caused = caused ?? throw new ArgumentNullException(nameof(caused));
            _causedCrit = causedCrit ?? throw new ArgumentNullException(nameof(causedCrit));
            _received = received ?? throw new ArgumentNullException(nameof(received));
            _receivedCrit = receivedCrit ?? throw new ArgumentNullException(nameof(receivedCrit));
        }

        /// <summary>取档位材质（构造即完整——返回值恒非 null；未知档落造成档兜底）。</summary>
        public Material For(DamageNumberTier tier)
        {
            switch (tier)
            {
                case DamageNumberTier.CausedCrit: return _causedCrit;
                case DamageNumberTier.Received: return _received;
                case DamageNumberTier.ReceivedCrit: return _receivedCrit;
                default: return _caused;
            }
        }
    }
}
