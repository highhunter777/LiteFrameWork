using System.Runtime.CompilerServices;

// 测试程序集可见内部类型（与 LiteNet/Vendor/kcp2k 的既有惯例同款：被测端口/纯逻辑多为 internal，
// 强制 public 只为测试会污染对外契约）。仅暴露给测试程序集，不影响产品可见性。
[assembly: InternalsVisibleTo("LiteNet.Tests")]
