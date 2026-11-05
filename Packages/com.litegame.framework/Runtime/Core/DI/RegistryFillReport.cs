using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 注册表填充报告：逐项 try/catch，单项失败记入继续，
    /// 整批跑完后统一判定——<see cref="HasFailures"/> 非空 → 阻止进 Main、逐条 Log.Error。
    /// reason 由填充方区分"未注册"（配置漏配）与"注册失败"（lua 炸了），两者排查方向完全不同。
    /// 引擎无关（Core），xUnit 双轨可测。
    /// </summary>
    public sealed class RegistryFillReport
    {
        private readonly List<(string key, string kind, string reason)> _failures = new();

        /// <summary>尝试填充的项数（空路径跳过行不计；LuaKeys 常量校验项计入）。</summary>
        public int Total;

        public int Filled;

        public int Failed;

        /// <summary>失败清单（key / kind / reason）——HUD 显示完整清单的数据源。</summary>
        public IReadOnlyList<(string key, string kind, string reason)> Failures => _failures;

        /// <summary>整批统一判定口径：任何失败都阻止进 Main（fail-fast，不做带病启动）。</summary>
        public bool HasFailures => Failed > 0;

        /// <summary>记录单项失败（同一 key+kind 只记第一条——判定在整批跑完后一次做出，同因不双报）。</summary>
        public void RecordFailure(string key, string kind, string reason)
        {
            for (int i = 0; i < _failures.Count; i++)
            {
                if (_failures[i].key == key && _failures[i].kind == kind) return;
            }
            _failures.Add((key, kind, reason));
            Failed++;
        }
    }
}
