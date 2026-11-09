using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 装配记账：产物提供段、消费边与未解析读取；装配期单线程写入，密封后只读。
    /// </summary>
    internal sealed class AssemblyLedger
    {
        internal const string FrameworkProvider = "Framework";

        private readonly Dictionary<Type, string> _providerByType = new Dictionary<Type, string>();
        private readonly List<Type> _providerOrder = new List<Type>();
        private readonly List<(Type Type, string Consumer)> _consumerEdges = new List<(Type, string)>();
        private readonly HashSet<string> _consumerKeys = new HashSet<string>(StringComparer.Ordinal);

        internal void RecordProvider(Type type, string provider)
        {
            if (type == null || string.IsNullOrEmpty(provider) || _providerByType.ContainsKey(type)) return;
            _providerByType.Add(type, provider);
            _providerOrder.Add(type);
        }

        internal void RecordConsumer(Type type, string consumer)
        {
            if (type == null || string.IsNullOrEmpty(consumer)) return;
            if (!_consumerKeys.Add(type.FullName + "|" + consumer)) return;
            _consumerEdges.Add((type, consumer));
        }

        internal AssemblyReport Build(IReadOnlyList<string> order)
        {
            var segmentOrder = new string[order.Count];
            for (int i = 0; i < order.Count; i++) segmentOrder[i] = order[i];

            var products = new AssemblyProduct[_providerOrder.Count];
            for (int i = 0; i < products.Length; i++)
            {
                Type type = _providerOrder[i];
                products[i] = new AssemblyProduct(type, _providerByType[type], ConsumersOf(type));
            }

            var unresolved = new List<string>();
            for (int i = 0; i < _consumerEdges.Count; i++)
            {
                (Type type, string consumer) = _consumerEdges[i];
                if (!_providerByType.ContainsKey(type)) unresolved.Add(consumer + " -> " + type.Name);
            }
            return new AssemblyReport(segmentOrder, products, unresolved.ToArray());
        }

        private string[] ConsumersOf(Type type)
        {
            var consumers = new List<string>();
            for (int i = 0; i < _consumerEdges.Count; i++)
                if (_consumerEdges[i].Type == type) consumers.Add(_consumerEdges[i].Consumer);
            return consumers.ToArray();
        }
    }
}
