#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System;
using System.Collections.Generic;
using LiteNet.Diagnostics;

namespace LiteGame
{
    /// <summary>只读弱网预设表（关闭 / 局域网 / 移动 / 高丢包）。</summary>
    public static class WeakNetPresets
    {
        public readonly struct Info
        {
            public readonly string Name;
            public readonly WeakNetProfile Profile;

            public Info(string name, WeakNetProfile profile)
            {
                Name = name;
                Profile = profile;
            }
        }

        public static readonly IReadOnlyList<Info> All = Array.AsReadOnly(new[]
        {
            new Info("none", WeakNetProfile.None),
            new Info("lan", new WeakNetProfile(true, 5, 0, 0, 0, 0, 1)),
            new Info("mobile", new WeakNetProfile(true, 80, 20, 50, 10, 20, 7)),
            new Info("lossy", new WeakNetProfile(true, 150, 40, 100, 20, 50, 11)),
        });

        public static bool TryGet(string name, out WeakNetProfile profile)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Name == name)
                {
                    profile = All[i].Profile;
                    return true;
                }
            }
            profile = WeakNetProfile.None;
            return false;
        }

        public static string NameOf(WeakNetProfile profile)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Profile.Equals(profile)) return All[i].Name;
            }
            return "custom";
        }
    }
}
#endif
