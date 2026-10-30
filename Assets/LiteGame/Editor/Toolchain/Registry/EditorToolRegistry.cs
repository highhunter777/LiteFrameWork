using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteGame.Toolchain
{
    public sealed class EditorToolRegistry
    {
        private readonly Dictionary<string, Func<IEditorToolPage>> _pages =
            new Dictionary<string, Func<IEditorToolPage>>(StringComparer.Ordinal);

        private readonly Dictionary<string, Action> _windows =
            new Dictionary<string, Action>(StringComparer.Ordinal);

        private readonly Dictionary<string, Func<ToolRequest, ToolResult>> _actions =
            new Dictionary<string, Func<ToolRequest, ToolResult>>(StringComparer.Ordinal);

        public void RegisterPage(string handler, Func<IEditorToolPage> factory)
        {
            if (string.IsNullOrEmpty(handler) || factory == null) throw new ArgumentException("page handler/factory missing");
            if (_pages.ContainsKey(handler)) throw new InvalidOperationException("page handler duplicated: " + handler);
            _pages.Add(handler, factory);
        }

        public void RegisterWindow(string handler, Action open)
        {
            if (string.IsNullOrEmpty(handler) || open == null) throw new ArgumentException("window handler/open missing");
            if (_windows.ContainsKey(handler)) throw new InvalidOperationException("window handler duplicated: " + handler);
            _windows.Add(handler, open);
        }

        public void RegisterAction(string handler, Func<ToolRequest, ToolResult> execute)
        {
            if (string.IsNullOrEmpty(handler) || execute == null) throw new ArgumentException("action handler/execute missing");
            if (_actions.ContainsKey(handler)) throw new InvalidOperationException("action handler duplicated: " + handler);
            _actions.Add(handler, execute);
        }

        public void RegisterAction(string handler, Func<ToolResult> execute)
        {
            if (execute == null) throw new ArgumentException("action execute missing");
            RegisterAction(handler, _ => execute());
        }

        public bool TryCreatePage(string handler, out Func<IEditorToolPage> factory)
        {
            return _pages.TryGetValue(handler, out factory);
        }

        public bool TryOpenWindow(string handler, out Action open)
        {
            return _windows.TryGetValue(handler, out open);
        }

        public bool TryExecute(string handler, out Func<ToolRequest, ToolResult> execute)
        {
            return _actions.TryGetValue(handler, out execute);
        }

        public IEnumerable<string> RegisteredHandlers
        {
            get { return _pages.Keys.Concat(_windows.Keys).Concat(_actions.Keys).Distinct(StringComparer.Ordinal); }
        }
    }
}
