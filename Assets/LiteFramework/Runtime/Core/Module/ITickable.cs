using System.Collections;
using System.Collections.Generic;

namespace LiteFramework
{
    public interface ITickable 
    {
        void Tick(float realDelta);   // 真实帧间隔(不缩放)；受变速的时间一律找时钟要(ScaledDelta)
    }
}
