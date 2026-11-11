using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteFramework.UI;
using LiteTesting.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LiteGame.Modules.Battle.SubModules.SettlementSubModule;

namespace LiteGame.Tests.EditMode
{
    /// <summary>反馈面夹具：真实 FeedbackService/页面流 + 代码建页面，记录每次页面打开尝试。</summary>
    internal sealed class RoomFlowFeedbackHarness : IDisposable
    {
        public const int ErrorFormId = 201;
        public const int LoadingFormId = 202;
        /// <summary>假弹窗按钮预置文案：未写入即证明本次未给该按钮文案（retryable 判据）。</summary>
        public const string PlaceholderOk = "预置OK";
        public const string PlaceholderCancel = "预置取消";

        private readonly UnityTestScope _scope;
        private readonly FakePageCatalog _catalog = new();
        private readonly Dictionary<string, GameObject> _prefabs = new();
        private readonly Dictionary<string, int> _formIds = new();
        private readonly List<int> _loads = new();
        private bool _disposed;

        public readonly UIService Ui;
        public readonly DialogService Dialogs;
        public readonly UINavigationController Nav;
        public readonly FeedbackService Feedback;
        public readonly PageFlow Pages;

        public RoomFlowFeedbackHarness(UnityTestScope scope)
        {
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Ui = new UIService(_catalog, transitionStrategy: new InstantPageTransition(),
                loadPrefab: LoadPrefab);
            Nav = new UINavigationController(Ui);
            Dialogs = new DialogService(Ui, nav: Nav);
            Feedback = new FeedbackService(Ui, Dialogs, Nav);
            Pages = new PageFlow(Nav, Ui, new SettlementSummary());

            Register(ErrorFormId, layer: 3, prefab: DialogPrefab(_scope, "roomflow_error"));
            Register(LoadingFormId, layer: 3, prefab: LoadingPrefab(_scope, "roomflow_loading"));
        }

        /// <summary>登记一张测试页面（未登记 = 打开即抛，避免"导航失败"被当成"没导航"）。</summary>
        public void Register(int formId, int layer, GameObject prefab, bool fullScreen = false)
        {
            string location = "roomflow/" + formId;
            _catalog.Add(formId, new PageDefinition
            {
                Id = formId,
                Name = "Fake" + formId,
                Location = location,
                Layer = layer,
                FullScreen = fullScreen,
            });
            _prefabs[location] = prefab;
            _formIds[location] = formId;
        }

        public GameObject NewPlainPage(string name)
            => _scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        /// <summary>该页面的打开尝试次数（prefab 加载口被调 = 确实发起过一次打开）。</summary>
        public int LoadsOf(int formId)
        {
            int count = 0;
            for (int i = 0; i < _loads.Count; i++)
                if (_loads[i] == formId) count++;
            return count;
        }

        public bool TryGetErrorDialog(out UIDialog dialog)
            => Dialogs.TryGetActiveDialog("error", out dialog);

        public void Pump(int ticks = 20, float dt = 0.05f)
        {
            for (int i = 0; i < ticks; i++)
            {
                Dialogs.Tick(dt);
                Ui.Tick(dt);
            }
        }

        /// <summary>收场：先关掉反馈面（不让假件残留到其它用例的 FindObjectOfType 观测面），再摘服务。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UniTask shutdown = Ui.ShutdownAsync();
            for (int i = 0; i < 200 && shutdown.Status == UniTaskStatus.Pending; i++) Ui.Tick(0.05f);
            Feedback.Dispose();
            Dialogs.Dispose();
        }

        private UniTask<IUIPrefabLease> LoadPrefab(string location, CancellationToken ct)
        {
            if (!_prefabs.TryGetValue(location, out GameObject prefab))
                throw new InvalidOperationException("测试页面未登记:" + location);
            _loads.Add(_formIds[location]);
            return UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(prefab));
        }

        private static GameObject DialogPrefab(UnityTestScope scope, string name)
        {
            GameObject root = scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UIDialog dialog = root.AddComponent<UIDialog>();
            dialog.Title = TextNode(scope, root, "Title");
            dialog.Message = TextNode(scope, root, "Message");
            dialog.OkButton = ButtonNode(scope, root, "Ok", PlaceholderOk);
            dialog.CancelButton = ButtonNode(scope, root, "Cancel", PlaceholderCancel);
            return root;
        }

        private static GameObject LoadingPrefab(UnityTestScope scope, string name)
        {
            GameObject root = scope.CreateGameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            LoadingMask mask = root.AddComponent<LoadingMask>();
            GameObject blocker = scope.CreateGameObject("Blocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(root.transform, false);
            mask.Blocker = blocker.GetComponent<Image>();
            mask.Message = TextNode(scope, root, "Message");
            return root;
        }

        private static TMP_Text TextNode(UnityTestScope scope, GameObject parent, string name)
        {
            GameObject node = scope.CreateGameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent.transform, false);
            return node.AddComponent<TextMeshProUGUI>();
        }

        private static Button ButtonNode(UnityTestScope scope, GameObject parent, string name, string label)
        {
            GameObject node = scope.CreateGameObject(name, typeof(RectTransform), typeof(Button));
            node.transform.SetParent(parent.transform, false);
            TextNode(scope, node, "Label").text = label;
            return node.GetComponent<Button>();
        }
    }

    internal sealed class FakePageCatalog : IPageCatalog
    {
        private readonly Dictionary<int, PageDefinition> _rows = new();

        public void Add(int id, PageDefinition definition) => _rows[id] = definition;

        public PageDefinition Get(int id)
            => _rows.TryGetValue(id, out PageDefinition row)
                ? row
                : throw new KeyNotFoundException($"测试页面目录缺行:{id}");
    }

    internal sealed class InstantPageTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
        public UniTask PlayClose(UIForm form, CancellationToken ct) => UniTask.CompletedTask;
    }

    /// <summary>卸载抛异常的场景服务：加载照常登记持有，卸载失败保留场景（清理停机态可观察）。</summary>
    internal sealed class ThrowingUnloadSceneService : ISceneService
    {
        private readonly HashSet<string> _loaded = new();

        public int LoadCalls;
        public int UnloadCalls;
        public string LastUnload;

        public bool IsLoaded(string location) => _loaded.Contains(location);
        public string SingleSceneName => null;
        public int AdditiveCount => _loaded.Count;
        public IReadOnlyCollection<string> AdditiveLocations => new List<string>(_loaded);
        public float Progress => 0f;

        public UniTask LoadSingleAsync(string location, CancellationToken ct = default) => UniTask.CompletedTask;
        public UniTask UnloadSingleAsync(CancellationToken ct = default) => UniTask.CompletedTask;

        public UniTask LoadAdditiveAsync(string location, CancellationToken ct = default)
        {
            LoadCalls++;
            _loaded.Add(location);
            return UniTask.CompletedTask;
        }

        public UniTask UnloadAdditiveAsync(string location, CancellationToken ct = default)
        {
            UnloadCalls++;
            LastUnload = location;
            throw new InvalidOperationException("模拟对局场景卸载失败:" + location);
        }

        public void ReleaseAll() { }
    }
}
