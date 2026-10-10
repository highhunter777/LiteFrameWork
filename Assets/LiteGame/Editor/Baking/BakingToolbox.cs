using System;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 烘焙工具箱（菜单 <c>LiteGame/烘焙工具箱</c>——四件烘焙/调带工具的**统一入口与统一形态**）：
    /// 每件工具一页（说明 + 执行 + 报告），共享目标单源 <see cref="BakeTargets"/>；执行面统一为
    /// 「静态 <c>All()</c> 返回多行报告文本，预期失败返回 <c>ABORT</c> 报告不抛异常」。原四个独立菜单
    /// （烘焙身位圆柱/烘焙枪口偏移/烘焙持枪偏移/调整爆头带）收敛于此；run_script 仍可直调各类
    /// <c>All()</c>/<see cref="HeadHitLineTuner.Export"/>。
    /// </summary>
    public sealed class BakingToolbox : EditorWindow
    {
        private const int HeadTabIndex = 3;

        private static readonly GUIContent[] Tabs =
        {
            new GUIContent("身位圆柱"),
            new GUIContent("枪口偏移"),
            new GUIContent("持枪偏移"),
            new GUIContent("爆头带调带"),
        };

        private int _tab;
        private Vector2 _scroll;
        private string _report = "";
        private readonly HeadHitLineTuner _headTuner = new HeadHitLineTuner();

        [MenuItem("LiteGame/烘焙工具箱")]
        private static void Open()
        {
            var window = GetWindow<BakingToolbox>("烘焙工具箱");
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        private void OnDisable()
        {
            _headTuner.Deactivate();
        }

        private void OnGUI()
        {
            int newTab = GUILayout.Toolbar(_tab, Tabs);
            if (newTab != _tab)
            {
                if (_tab == HeadTabIndex) _headTuner.Deactivate();      // Scene 绘制与预览只挂在爆头带页在场期间
                _tab = newTab;
                if (_tab == HeadTabIndex) _headTuner.Activate();
            }

            switch (_tab)
            {
                case 0: DrawBodyPage(); break;
                case 1: DrawMuzzlePage(); break;
                case 2: DrawGripPage(); break;
                case 3: DrawHeadPage(); break;
            }
        }

        // ---- 三个一键烘焙页（同构：前提说明 → 执行 → 报告）----

        private void DrawBodyPage()
        {
            EditorGUILayout.HelpBox(
                "身位圆柱：读 prefab 的 CharacterController（美术可调、可视、唯一）烘成 BodyBake.g.cs——"
                + "服务端命中柱 / 移动去穿插 / 本地视图 CC 三方同值。校验 center.y == height/2（圆柱底=脚底），不满足不烘。\n"
                + "重跑时机：美术调 CC 后。命中柱半径 HitscanRadius 是判定裁决常量，非本页产物。", MessageType.Info);
            if (GUILayout.Button("烘焙 BodyBake.g.cs")) Run(BodyCylinderBaker.All);
            DrawReport();
        }

        private void DrawMuzzlePage()
        {
            EditorGUILayout.HelpBox(
                "枪口偏移：采样 prefab 的 Weapon_Rifle/Muzzle 锚点 @ AimIdle t=0（射弹时刻视觉姿态单族），"
                + "输出相对 prefab 根的 Sim 系三常量 → MuzzleBake.g.cs（进 CombatConfigDigest）。\n"
                + "重跑时机：美术调锚点/换枪/换参考姿态后。", MessageType.Info);
            if (GUILayout.Button("烘焙 MuzzleBake.g.cs")) Run(MuzzleOffsetBaker.All);
            DrawReport();
        }

        private void DrawGripPage()
        {
            EditorGUILayout.HelpBox(
                "持枪偏移：参考持枪姿态（AimIdle t=0）下采样「武器骨 add_weapon_r 相对持枪手（RightHand）」的局部变换，"
                + "写 WeaponGripOffset 资产——换弹期枪位姿态覆写（WeaponGripConstraint）的唯一来源。\n"
                + "重跑时机：换绑定骨/换枪后（重烘前无需改资产）；观感幅度在资产面板调 Torso Weight。", MessageType.Info);
            if (GUILayout.Button("烘焙 WeaponGripOffset.asset")) Run(WeaponGripBaker.All);
            DrawReport();
        }

        // ---- 爆头带调带页（交互工具件寄宿）----

        private void DrawHeadPage()
        {
            _headTuner.DrawGUI();
        }

        // ---- 公共执行与报告 ----

        private void Run(Func<string> action)
        {
            try { _report = action(); }
            catch (Exception ex) { _report = "ABORT " + ex.GetType().Name + ": " + ex.Message; }   // 防御兜底：工具本体预期失败走 ABORT 报告，不抛
            Debug.Log("[BakingToolbox]\n" + _report);
            Repaint();
        }

        private void DrawReport()
        {
            if (string.IsNullOrEmpty(_report)) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(90));
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }
}
