using System;

namespace LiteFramework
{
    /// <summary>候选程序集条目：程序集名（文件名去 .dll）与候选根相对路径。</summary>
    public sealed class CodeEntry
    {
        public CodeEntry(string name, string path)
        {
            Name = string.IsNullOrEmpty(name) ? throw new ArgumentException("程序集名不能为空", nameof(name)) : name;
            Path = string.IsNullOrEmpty(path) ? throw new ArgumentException("路径不能为空", nameof(path)) : path;
        }

        public string Name { get; }
        public string Path { get; }
    }
}
