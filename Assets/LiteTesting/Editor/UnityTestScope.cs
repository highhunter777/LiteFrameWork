using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LiteTesting.Unity
{
    /// <summary>
    /// Per-test ownership for transient Unity objects and AssetDatabase folders.
    /// </summary>
    public sealed class UnityTestScope : IDisposable
    {
        private readonly TestScope _core;
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<string> _assetFolders = new List<string>();
        private bool _disposed;

        public UnityTestScope(string testId, TestRunSettings settings = null)
        {
            _core = new TestScope(testId, settings);
        }

        public TestScope Core => _core;

        public GameObject CreateGameObject(string name, params Type[] components)
        {
            ThrowIfDisposed();
            var gameObject = new GameObject(name, components ?? Array.Empty<Type>());
            _objects.Add(gameObject);
            return gameObject;
        }

        public T Track<T>(T instance) where T : Object
        {
            ThrowIfDisposed();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (EditorUtility.IsPersistent(instance))
            {
                throw new ArgumentException("Persistent assets must be owned through CreateTempAssetFolder.", nameof(instance));
            }

            _objects.Add(instance);
            return instance;
        }

        public string CreateTempAssetFolder()
        {
            ThrowIfDisposed();
            string name = "__LiteTesting_" + Guid.NewGuid().ToString("N");
            string guid = AssetDatabase.CreateFolder("Assets", name);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("AssetDatabase did not create a temporary test folder.");
            _assetFolders.Add(path);
            return path;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = null;

            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            for (int i = _assetFolders.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (AssetDatabase.IsValidFolder(_assetFolders[i]) && !AssetDatabase.DeleteAsset(_assetFolders[i]))
                    {
                        throw new InvalidOperationException("Failed to delete temporary asset folder: " + _assetFolders[i]);
                    }
                }
                catch (Exception exception)
                {
                    AddFailure(ref failures, exception);
                }
            }

            try
            {
                _core.Dispose();
            }
            catch (Exception exception)
            {
                AddFailure(ref failures, exception);
            }

            if (failures != null) throw new AggregateException("Unity test cleanup failed.", failures);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityTestScope));
        }

        private static void AddFailure(ref List<Exception> failures, Exception exception)
        {
            if (failures == null) failures = new List<Exception>();
            failures.Add(exception);
        }
    }
}
