using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 架构单元模型（arch/units.json）的只读投影。
    /// 程序集允许边、层号、tier、收集组、测试桶、owner 的唯一来源；守卫只读模型，不再各自维护表。
    /// </summary>
    internal sealed class ArchModel
    {
        public int Version { get; set; }
        public LayerEntry[] Layers { get; set; }
        public string[] Tiers { get; set; }
        public Dictionary<string, TestBucket> TestBuckets { get; set; }
        public ExternalEntry[] ExternalAssemblies { get; set; }
        public UnitEntry[] Units { get; set; }
        public Dictionary<string, CollectException> CollectPathExceptions { get; set; }
        public ChangeEntry[] ChangeMatrix { get; set; }
        public DomainEntry[] Domains { get; set; }
        public DomainEdge[] DomainEdges { get; set; }
        public ScanTargetEntry[] ScanTargets { get; set; }
        public TopLevelEntry TopLevel { get; set; }

        public static ArchModel Load(string root)
        {
            string path = Path.Combine(root, "arch", "units.json");
            if (!File.Exists(path)) throw new FileNotFoundException("缺架构单元模型 arch/units.json", path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ArchModel model = JsonSerializer.Deserialize<ArchModel>(File.ReadAllText(path), options);
            if (model == null) throw new InvalidDataException("arch/units.json 解析为空");
            return model;
        }

        public UnitEntry Unit(string id)
        {
            foreach (UnitEntry unit in Units)
                if (string.Equals(unit.Id, id, StringComparison.Ordinal)) return unit;
            return null;
        }
    }

    internal sealed class LayerEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Rank { get; set; }
    }

    internal sealed class TestBucket
    {
        public string Kind { get; set; }
        public string Path { get; set; }
        public string Lane { get; set; }
    }

    internal sealed class ExternalEntry
    {
        public string Name { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class UnitEntry
    {
        public string Id { get; set; }
        public string Assembly { get; set; }
        public string Path { get; set; }
        public string Asmdef { get; set; }
        public string Layer { get; set; }
        public int Rank { get; set; }
        public string Owner { get; set; }
        public string[] Tier { get; set; }
        public string[] CollectGroups { get; set; }
        public string[] Tests { get; set; }
        public bool NoEngine { get; set; }
        public string[] DefineConstraints { get; set; }
        public string[] References { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class CollectException
    {
        public string Owner { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class ChangeEntry
    {
        public string Change { get; set; }
        public string[] Paths { get; set; }
        public string[] Ship { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class DomainEntry
    {
        public string Id { get; set; }
        public string Role { get; set; }
    }

    internal sealed class DomainEdge
    {
        public string From { get; set; }
        public string To { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class ScanTargetEntry
    {
        public string Root { get; set; }
        public string Rules { get; set; }
        public string Exclude { get; set; }
        public bool IncludeEditor { get; set; }
    }

    internal sealed class TopLevelEntry
    {
        public AssetsTopLevel Assets { get; set; }
        public string[] LiteGame { get; set; }
    }

    internal sealed class AssetsTopLevel
    {
        public string[] Dirs { get; set; }
        public string[] Ignored { get; set; }
    }
}
