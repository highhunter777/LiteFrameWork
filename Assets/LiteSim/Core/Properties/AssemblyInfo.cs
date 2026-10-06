using System.Runtime.CompilerServices;

// 测试程序集可见内部类型（与 LiteNet/Vendor/kcp2k、RoomServer 的既有惯例同款：
// 被测端口/生成物多为 internal，强制 public 只为测试会污染对外契约）。
[assembly: InternalsVisibleTo("LiteSim.Core.Tests")]
