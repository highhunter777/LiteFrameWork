using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 程序化灰盒实体（对局可见性的兜底件）：内容包尚无角色 prefab 时，用引擎内置基本体造一个
    /// "能看见、能朝向"的替身，保证 SimView/相机/输入链路端到端可验。
    ///
    /// **不是美术方案**：正式角色 prefab 随"地图提取/角色动画"前置批入库后，
    /// <see cref="ProcedureBattle"/> 的租约获取会命中真资源，本件自然退场（无需删代码）。
    /// 基本体与材质由引擎提供（不引资源包、不依赖 Unity 导入），因此也不受
    /// <c>Assets/Art/</c> 被 VCS 忽略的影响——灰盒阶段的可复现性靠它保。
    ///
    /// **枢轴契约**：实体根位于**脚下**（Sim 的 <c>EntitySlot.Pos</c> 语义），可见网格挂在
    /// 抬升了半高的子节点上——与真角色 prefab 的枢轴约定一致，View 侧无需为灰盒特判。
    ///
    /// `Resources.GetBuiltinResource` 在本工程实测取不到内置胶囊/默认材质（返回 null，
    /// 见批次记录），故用 <see cref="GameObject.CreatePrimitive"/>——实测可用且渲染器自带
    /// 当前渲染管线（URP/Lit）的共享材质，无需自建材质。
    /// </summary>
    internal static class GreyboxEntity
    {
        /// <summary>实体高度（胶囊中心抬升量 = 半高）；与 <c>CombatConfig.HitscanHeight</c> 同量级。</summary>
        private const float VisualHeight = 2f;

        /// <summary>造一个胶囊替身：根在脚下，网格子节点抬半高。共享网格/材质，不泄漏材质实例。</summary>
        public static GameObject Make()
        {
            // CreatePrimitive 出的对象自带当前管线的共享材质；本次只取其网格/材质，不保留该对象
            var proto = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Mesh mesh = proto.GetComponent<MeshFilter>().sharedMesh;
            Material material = proto.GetComponent<MeshRenderer>().sharedMaterial;

            var root = new GameObject("GreyboxEntity");            // 根 = 脚下（SimView 写 position 的落点）

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, VisualHeight * 0.5f, 0f);

            var filter = visual.AddComponent<MeshFilter>();
            var renderer = visual.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;

            DestroyProto(proto);                                    // 原型只借网格/材质
            return root;
        }

        private static void DestroyProto(GameObject proto)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { Object.DestroyImmediate(proto); return; }
#endif
            Object.Destroy(proto);
        }
    }
}
