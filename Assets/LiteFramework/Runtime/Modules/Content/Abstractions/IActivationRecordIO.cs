using System;

namespace LiteFramework
{
    /// <summary>激活记录的持久化端口（Unity 侧以 FileSystem/JSON 实现；L1 用内存假件）。</summary>
    public interface IActivationRecordIO
    {
        /// <summary>读记录；无记录或记录不可解析 = 返回 null（调用方按"全新安装/损坏回内置"处理）。</summary>
        ActivationRecord TryLoad();

        /// <summary>原子写记录（实现方负责先验证后提交与临时文件切换——热更 §8"原子提交由平台存储适配实现"）。</summary>
        void Save(ActivationRecord record);
    }
}
