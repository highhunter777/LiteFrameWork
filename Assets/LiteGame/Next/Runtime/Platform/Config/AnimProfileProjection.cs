using System;
using System.Collections.Generic;

namespace LiteGameNext.Platform.Config
{
    /// <summary>动画片段档案（来自 `tbanimationprofile`）：片段键、模型族、通道、绑定、循环、速度区间、回退。</summary>
    public readonly struct AnimProfile
    {
        public AnimProfile(string id, string modelFamily, string kind, string channel, string binding,
            string bindings, bool loop, float minSpeed, float maxSpeed, bool holdOnFinish,
            bool requiresLoad, string fallbackId)
        {
            Id = id;
            ModelFamily = modelFamily;
            Kind = kind;
            Channel = channel;
            Binding = binding;
            Bindings = bindings;
            Loop = loop;
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
            HoldOnFinish = holdOnFinish;
            RequiresLoad = requiresLoad;
            FallbackId = fallbackId;
        }

        public string Id { get; }
        public string ModelFamily { get; }
        public string Kind { get; }
        public string Channel { get; }
        public string Binding { get; }
        public string Bindings { get; }
        public bool Loop { get; }
        public float MinSpeed { get; }
        public float MaxSpeed { get; }
        public bool HoldOnFinish { get; }
        public bool RequiresLoad { get; }
        public string FallbackId { get; }

        /// <summary>速度是否落在本片段区间（含下界、不含上界；max≤min 视为无区间约束）。</summary>
        public bool MatchesSpeed(float speed)
            => MaxSpeed <= MinSpeed || (speed >= MinSpeed && speed < MaxSpeed);
    }

    /// <summary>`tbanimationprofile` → 动画档案表；按 Id 与模型族索引。</summary>
    public static class AnimProfileProjection
    {
        /// <summary>投影成 id→档案字典。</summary>
        public static Dictionary<string, AnimProfile> Project(cfg.Tables tables)
        {
            var result = new Dictionary<string, AnimProfile>(StringComparer.Ordinal);
            if (tables?.Tbanimationprofile == null) return result;
            foreach (cfg.animationprofile row in tables.Tbanimationprofile.DataList)
            {
                if (row == null || string.IsNullOrEmpty(row.Id)) continue;
                result[row.Id] = new AnimProfile(row.Id, row.ModelFamily, row.Kind, row.Channel,
                    row.Binding, row.Bindings, row.Loop, row.MinSpeed, row.MaxSpeed,
                    row.HoldOnFinish, row.RequiresLoad, row.FallbackId);
            }
            return result;
        }

        /// <summary>按模型族取该族全部档案（保持表序）。</summary>
        public static List<AnimProfile> ForFamily(IReadOnlyDictionary<string, AnimProfile> map, string family)
        {
            var result = new List<AnimProfile>();
            if (map == null || string.IsNullOrEmpty(family)) return result;
            foreach (KeyValuePair<string, AnimProfile> pair in map)
                if (string.Equals(pair.Value.ModelFamily, family, StringComparison.Ordinal))
                    result.Add(pair.Value);
            return result;
        }
    }
}
