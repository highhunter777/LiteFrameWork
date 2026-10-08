using System;
using System.Threading;
using System.Threading.Tasks;

namespace MetaServer.Contracts.Persistence
{
    /// <summary>账号记录（Auth 模块数据所有权；deviceId 唯一键 = get-or-create 的最终幂等裁判，§5.3）。</summary>
    public sealed class AccountRecord
    {
        /// <summary>账号 Id（签发端 CSPRNG 生成，"a" + 20 hex；全局唯一性由随机空间保证，非自增）。</summary>
        public string AccountId;

        /// <summary>设备标识（唯一索引 ux_account_device 承载；同设备 → 同账号）。</summary>
        public string DeviceId;

        /// <summary>创建时刻（UTC；存储端权威赋值）。</summary>
        public DateTime CreatedUtc;
    }

    /// <summary>
    /// 账号存储端口（M0-c 同款端口纪律：契约层零 Mongo 依赖；L1 用测试双打，L3 用真适配器）。
    /// </summary>
    public interface IAccountStore
    {
        /// <summary>按设备标识查账号；无则 null。</summary>
        ValueTask<AccountRecord> FindByDeviceIdAsync(string deviceId, CancellationToken ct);

        /// <summary>插入新账号。**deviceId 已存在 → 返回 null**（由调用方重读——唯一索引是
        /// get-or-create 竞态的最终裁判，§5.3"数据库唯一键是最终幂等裁判"）；基础设施故障抛异常。</summary>
        ValueTask<AccountRecord> CreateAsync(AccountRecord record, CancellationToken ct);
    }
}
