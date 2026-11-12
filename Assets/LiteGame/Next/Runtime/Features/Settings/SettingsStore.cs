using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFrameworkNext;
using UnityEngine;

namespace LiteGameNext.Features.Settings
{
    /// <summary>设置持久化：settings.json 键值字典，原子写；失败的读写只记日志不炸调用方。</summary>
    public sealed class SettingsStore
    {
        private const string FileName = "settings.json";

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(32);
        private readonly string _path;
        private bool _dirty;

        /// <summary>默认落到 persistentDataPath；测试可注入任意路径。</summary>
        public SettingsStore(string directoryOverride = null)
        {
            string dir = string.IsNullOrEmpty(directoryOverride)
                ? Application.persistentDataPath
                : directoryOverride;
            _path = Path.Combine(dir, FileName);
        }

        public string FilePath => _path;

        /// <summary>文件 → 字典；缺失或损坏一律视为首启（静默空表）。</summary>
        public void Load()
        {
            _values.Clear();
            _dirty = false;
            if (!File.Exists(_path)) return;
            try
            {
                string json = File.ReadAllText(_path);
                SettingsJson parsed = JsonUtility.FromJson<SettingsJson>(json);
                if (parsed?.entries == null) return;
                for (int i = 0; i < parsed.entries.Length; i++)
                {
                    SettingsJsonEntry entry = parsed.entries[i];
                    if (!string.IsNullOrEmpty(entry.key)) _values[entry.key] = entry.value;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("设置读取失败（按首启处理）：" + ex.Message);
            }
        }

        /// <summary>有未存改动才写；写失败保留脏标记，下次再试。</summary>
        public void SaveIfDirty()
        {
            if (!_dirty) return;
            try
            {
                var container = new SettingsJson { entries = new SettingsJsonEntry[_values.Count] };
                int i = 0;
                foreach (KeyValuePair<string, string> pair in _values)
                    container.entries[i++] = new SettingsJsonEntry { key = pair.Key, value = pair.Value };

                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(container));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temp, _path);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Log.Warning("设置写入失败（保留脏标记）：" + ex.Message);
            }
        }

        /// <summary>读字符串值；缺键返回 null（调用方给默认值）。</summary>
        public string Get(string key) => _values.TryGetValue(key, out string value) ? value : null;

        /// <summary>写字符串值；与现值相同不置脏。</summary>
        public void Set(string key, string value)
        {
            if (_values.TryGetValue(key, out string existing) && existing == value) return;
            _values[key] = value;
            _dirty = true;
        }

        /// <summary>退出/关闭时的保存时机（ClientApp 关闭链调用）。</summary>
        public UniTask FlushAsync(CancellationToken ct)
        {
            SaveIfDirty();
            return UniTask.CompletedTask;
        }

        [Serializable]
        private sealed class SettingsJson
        {
            public SettingsJsonEntry[] entries;
        }

        [Serializable]
        private sealed class SettingsJsonEntry
        {
            public string key;
            public string value;
        }
    }
}
