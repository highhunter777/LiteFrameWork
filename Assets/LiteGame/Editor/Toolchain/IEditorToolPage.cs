using UnityEngine;

namespace LiteGame.Toolchain
{
    /// <summary>
    /// 开发工具页件契约：一页一职责，生命周期由 Hub 壳驱动。
    /// 原 IGmPage 继承本接口，GM 六页无需改业务即可被 Hub 宿主。
    /// </summary>
    public interface IEditorToolPage
    {
        GUIContent Title { get; }

        void OnShow();

        void OnHide();

        void Draw();

        void Poll();
    }

    public abstract class EditorToolPageBase : IEditorToolPage
    {
        private readonly GUIContent _title;

        protected EditorToolPageBase(GUIContent title)
        {
            _title = title ?? new GUIContent("工具");
        }

        public GUIContent Title { get { return _title; } }

        public virtual void OnShow() { }

        public virtual void OnHide() { }

        public abstract void Draw();

        public virtual void Poll() { }
    }
}
