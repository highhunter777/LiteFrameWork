using System;
using UnityEditor;
using UnityEngine;

namespace LiteGame.EditorTools
{
    /// <summary>
    /// 烘焙工具箱（菜单 <c>LiteGame/烘焙工具箱</c>——六件烘焙/调带工具的**统一入口与统一形态**）：
    /// 每件工具一页（说明 + 执行 + 报告），共享目标单源 <see cref="BakeTargets"/>；执行面统一为
    /// 「静态 <c>All()</c> 返回多行报告文本，预期失败返回 <c>ABORT</c> 报告不抛异常」。原独立菜单
    /// （烘焙身位圆柱/烘焙枪口偏移/烘焙持枪偏移/调整爆头带/LiteSim 根位移烘焙）收敛于此；
    /// 场景采样族「地图」（<see cref="SimMapBaker"/>）直落本箱第六页；
    /// run_script 仍可直调各类 <c>All()</c>/<see cref="HeadHitLineTuner.Export"/>。
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
            new GUIContent("根位移"),
            new GUIContent("地图"),
        };

        private int _tab;
        private Vector2 _scroll;
        private string _report = "";
        private readonly HeadHitLineTuner _headTuner = new HeadHitLineTuner();

        public static void Open()
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
                case 4: DrawRootMotionPage(); break;
                case 5: DrawMapPage(); break;
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
                + "输出相对 prefab 根的 Sim 系三常量 → MuzzleBake.g.cs（随代码进 buildHash 闭包）。\n"
                + "重跑时机：美术调锚点/换枪/换参考姿态后。", MessageType.Info);
            if (GUILayout.Button("烘焙 MuzzleBake.g.cs")) Run(MuzzleOffsetBaker.All);
            DrawReport();
        }

        private void DrawGripPage()
        {
            EditorGUILayout.HelpBox(
                "持枪偏移：参考持枪姿态（AimIdle t=0）下采样「武器骨 add_weapon_r 相对持枪手（RightHand）」的局部变换，"
                + "烘成 GripBake.g.cs 常量（落 LiteGame.Modules.View——纯表现参数，刻意不进 BuildHash 源集）——换弹期枪位姿态覆写（WeaponGripConstraint）的唯一来源。\n"
                + "重跑时机：换绑定骨/换枪后；对局内观感幅度走 GM 面板「持枪幅度」滑杆（GripTuning 覆写，不落盘）。", MessageType.Info);
            if (GUILayout.Button("烘焙 GripBake.g.cs")) Run(WeaponGripBaker.All);
            DrawReport();
        }

        // ---- 根位移页（片段采样族——来源是 RootMotionBakeConfig 引用的动作片段，非 prefab）----

        private void DrawRootMotionPage()
        {
            EditorGUILayout.HelpBox(
                "根位移：按 RootMotionBakeConfig 逐动作登记（ActionId/片段/策略），采样人形 RootT 曲线 60Hz 帧间差分"
                + "→ 毫米量化 → fail-fast 校验（NaN/超速/重复 ID）→ RootMotionData.g.cs（两端共享，进 BuildHash 源集）+ digest。\n"
                + "重跑时机：改片段/导入设置/配置登记后；同配置重烘逐字节一致。片段包不入版本管理——需本地在位。", MessageType.Info);
            if (GUILayout.Button("烘焙 RootMotionData.g.cs")) Run(RootMotionBaker.All);
            DrawReport();
        }

        // ---- 地图页（场景采样族——来源是地图场景，非 prefab/片段）----

        private void DrawMapPage()
        {
            EditorGUILayout.HelpBox(
                "地图烘焙：按目标注册表 BakeTargets.Maps 逐图出按钮（加图＝注册表加一条，本页自动跟随）。"
                + "采集场景的判定几何（MapCollision 代理树：Box/Sphere/Capsule；MeshCollider 默认拒载）＋出生点"
                + "（spawn_*，按名排序定 index）＋边界（map_bounds，半宽/半深须显式赋值——缺省 0 即拒载），"
                + "经校验（fail-fast→不产半成品；出生点不落障碍内、离障碍 ≥ 身位半径、不越边界）→毫米量化→digest，"
                + "写 Luban 数据源（write-if-changed；勿手改——改布局＝改场景重烘）。\n"
                + "产物是数据源不是 .bytes：需手动跑 Luban/gen.bat 生成 Map/tbmap.bytes，跑完重跑 python scripts/codegen/gen-build-hash.py。\n"
                + "重跑时机：改地图场景（代理/出生/边界）后；代理名前缀 surf_<surface>_ 标表面（无前缀＝Default）。", MessageType.Info);
            for (int i = 0; i < BakeTargets.Maps.Length; i++)
            {
                BakeTargets.MapTarget target = BakeTargets.Maps[i];
                if (GUILayout.Button("烘焙 " + target.MapName + " → " + target.Output)) Run(() => SimMapBaker.Bake(target));
            }
            if (GUILayout.Button("烘焙全部地图（" + BakeTargets.Maps.Length + " 张）")) Run(SimMapBaker.BakeAll);
            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "训练场布点（多图批）：布点即内容——手工在场景里拖 spawn_*/掩体即调（重跑「补齐缺失件」不抹掉）；"
                + "「全量重建种子」= 清旧 MapCollision 回到种子布点（回滚用）。两条路改完都要重烘训练场（上一行按钮）。", MessageType.Info);
            if (GUILayout.Button("训练场布点：补齐缺失件（非破坏）")) Run(TrainingGroundLayout.EnsureSeed);
            if (GUILayout.Button("训练场布点：全量重建种子")) Run(TrainingGroundLayout.RebuildSeed);
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
