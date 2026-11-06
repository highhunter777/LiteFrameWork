using System;

namespace LiteFramework.Animation
{
    public enum AnimationChannelMask
    {
        None = 0,
        Base = 1 << 0,
        Overlay = 1 << 1,
        Override = 1 << 2,
    }
}
