using System.Collections.Generic;
using LiteFramework;
using LiteSim;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame
{
    /// <summary>
    /// Sim 灰盒沙盒（一次性调试件——不承担产品职责）。用途：让 Sim 层肉眼可见
    /// （跑动/开火/命中/掉血），早期验证手感与数值。
    ///
    /// **不要把本目录移进任何 `Editor/` 下**：`Assets/.../Editor/` 是特殊文件夹，其中的
    /// MonoBehaviour **不能 AddComponent**（本件为代码创建形态，会直接失效）——故程序集独立为
    /// `LiteGame.DevHUD`。
    ///
    /// - 驱动：FrameDriver.Tick(Time.deltaTime)（不吃 IGameClock）——追帧/防死亡螺旋由 FrameDriver 承担。
    /// - 输入：键盘 WASD + 鼠标朝向 + 左键开火，直接组装 SimInputFrame（不做控制器抽象/键位重绑）。
    /// - 可视：Gizmos 画活体（胶囊近似）/朝向射线/hitscan 线/地图边界——不走 SimView、不做插值。
    /// - 飘字：命中/击杀复用 FlyTextPool（场景缺失时静默降级——只画 Gizmos）。
    /// - HUD：IModuleStats 段并入 DevHUD（帧号/活体数/checksum/追帧数/暂停态/靶标数）。
    /// - 自创建形态（同 DevHUD 手法）：场景不挂组件，由 SimSandboxBoot 按场景名自建——
    ///   避免三宏 #if 剥离时场景序列化组件报 "not derived from MonoBehaviour"。
    ///
    /// 操作：WASD 走位｜鼠标=朝向｜左键(按住)=开火｜P=暂停开关｜暂停中 N=单步推进一逻辑帧｜R=靶标清空后重铺。
    /// </summary>
    public sealed class SimSandbox : MonoBehaviour, IModuleStats
    {
        private const int TargetCount = 5;
        private const float VisualLineSeconds = 0.4f; // hitscan 线/命中点的 Gizmos 存留时长（表现层瞬态）
        private const ulong WorldSeed = 0x5EEDBEEF12345678UL; // 与 SimChecksumBaselineSpec 同源（便于比对行为）

        /// <summary>近期可视化线（环形复用，零分配；Time&lt;=0 = 空位）。</summary>
        private struct FireVis
        {
            public Vector3 From;
            public Vector3 To;
            public float Time;
            public bool IsHit;
        }

        private SimWorldState _state;
        private SimMapData _map;
        private FrameDriver _driver;
        private SimInputFrame[] _inputs;
        private long _playerId;
        private float _lastYaw;
        private Vector3 _aimPointWorld = new Vector3(1f, CombatConfig.MuzzleOffsetHeight, 0f); // 瞄准点（AimPoint 单口径；默认朝 +X 眼高位）
        private bool _paused;
        private int _targetsLeft;
        private Camera _cam;
        private LiteGame.UI.FlyTextPool _fly;
        private Vector3 _lastFireOrigin;
        private bool _firePending;
        private readonly FireVis[] _vis = new FireVis[64];
        private int _visHead;
        private readonly Plane _groundPlane = new Plane(Vector3.up, 0f);

        private void Start()
        {
            _cam = Camera.main;
            _fly = FindAnyObjectByType<LiteGame.UI.FlyTextPool>();

            _map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            _map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);
            _map.SpawnPointCount = 1;

            _state = new SimWorldState { RngState = WorldSeed };
            _state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int slot);
            _playerId = _state.Entities[slot].Id;

            _driver = new FrameDriver();
            _inputs = new SimInputFrame[1];

            SpawnTargets();
        }

        private void Update()
        {
            if (_state == null) return;

            if (Input.GetKeyDown(KeyCode.P)) _paused = !_paused;
            if (Input.GetKeyDown(KeyCode.R) && _targetsLeft == 0) SpawnTargets();

            if (_paused)
            {
                if (Input.GetKeyDown(KeyCode.N)) // 单步：恰一逻辑帧（dt = 1 步长 → 累加器推进一步后归零）
                {
                    BuildInput();
                    _driver.Tick(SimConfig.Dt, _state, _map, _inputs, CombatConfig.Loaded, WeaponConfig.Loaded, OnLogicalFrame);
                }
                return;
            }

            BuildInput();
            _driver.Tick(Time.deltaTime, _state, _map, _inputs, CombatConfig.Loaded, WeaponConfig.Loaded, OnLogicalFrame);
        }

        /// <summary>键盘+鼠标 → SimInputFrame（采集侧契约：移动向量长度 ≤ 1）。</summary>
        private void BuildInput()
        {
            float mx = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float mz = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float mag2 = mx * mx + mz * mz;
            if (mag2 > 1f) // 斜向归一（长度 ≤ 1 契约）
            {
                float inv = 1f / Mathf.Sqrt(mag2);
                mx *= inv;
                mz *= inv;
            }

            if (_cam != null && _state.TryResolve(_playerId, out int slot))
            {
                // 瞄准点解算与生产采集侧同口径（《固定斜视角射击方案专项设计》§3）：相机射线先与
                // **实体圆柱**求交（同一 SimRaycast），未命中落地面 y=0——命中实体时点**带高度**
                // （准心在敌人头上 ⇒ 弹道抬进头部带，沙盒里也能打出爆头）。
                var ray = _cam.ScreenPointToRay(Input.mousePosition);
                if (SimRaycast.RaycastEntities(_state, slot,
                        new SimVector3(ray.origin.x, ray.origin.y, ray.origin.z),
                        new SimVector3(ray.direction.x, ray.direction.y, ray.direction.z),
                        CombatConfig.HitscanRange, out _, out float t))
                {
                    _aimPointWorld = ray.GetPoint(t);
                }
                else if (_groundPlane.Raycast(ray, out float d))
                {
                    _aimPointWorld = ray.GetPoint(d);
                }
                ref EntitySlot e = ref _state.Entities[slot];
                _lastYaw = Mathf.Atan2(_aimPointWorld.z - e.Pos.Z, _aimPointWorld.x - e.Pos.X); // 仅供 Gizmos 显示（朝向由 Sim 派生）
            }

            _inputs[0].EntityId = _playerId;
            _inputs[0].MoveX = mx;
            _inputs[0].MoveZ = mz;
            _inputs[0].AimPointX = _aimPointWorld.x;
            _inputs[0].AimPointY = _aimPointWorld.y;
            _inputs[0].AimPointZ = _aimPointWorld.z;
            _inputs[0].Buttons = Input.GetMouseButton(0) ? SimInputFrame.ButtonFire : 0u;
        }

        /// <summary>每逻辑帧末消费帧事件（FrameDriver 在回调后清空，决策⑥）——Gizmos 线 + 飘字。</summary>
        private void OnLogicalFrame(SimWorldState s)
        {
            for (int i = 0; i < s.Events.Count; i++)
            {
                FrameEvent e = s.Events.Items[i];
                switch (e.Kind)
                {
                    case FrameEventKind.Fire:
                        _lastFireOrigin = new Vector3(e.Pos.X, e.Pos.Y + CombatConfig.HitscanHeight * 0.5f, e.Pos.Z);
                        _firePending = true;
                        break;

                    case FrameEventKind.Hit:
                    {
                        var hitPos = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                        PushVis(_firePending ? _lastFireOrigin : hitPos, hitPos, true);
                        _firePending = false;
                        ShowFly("-" + e.Value, hitPos);
                        break;
                    }

                    case FrameEventKind.Death:
                    {
                        var deathPos = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                        PushVis(deathPos, deathPos + Vector3.up * 2f, false);
                        ShowFly("K.O.", deathPos);
                        _targetsLeft--; // 沙盒中只有靶标会死（靶标无输入不还手）
                        break;
                    }
                }
            }

            if (_firePending) // 本帧开火未命中 → 画一条到射程终点的未命中线
            {
                var dir = new Vector3(Mathf.Cos(_lastYaw), 0f, Mathf.Sin(_lastYaw));
                PushVis(_lastFireOrigin, _lastFireOrigin + dir * CombatConfig.HitscanRange, false);
                _firePending = false;
            }
        }

        private void PushVis(Vector3 from, Vector3 to, bool isHit)
        {
            _vis[_visHead].From = from;
            _vis[_visHead].To = to;
            _vis[_visHead].Time = Time.time;
            _vis[_visHead].IsHit = isHit;
            _visHead = (_visHead + 1) & 63; // 环形覆盖，零分配
        }

        private void ShowFly(string text, Vector3 worldPos)
        {
            if (_fly == null || _cam == null) return;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(_cam, worldPos + Vector3.up * 1.5f);
            _fly.Show(text, screen);
        }

        private void SpawnTargets()
        {
            for (int i = 0; i < TargetCount; i++)
            {
                float z = -10f + 5f * i; // 与 SimChecksumBaselineSpec 靶标布点一致
                _state.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(20f, 0f, z) }, out int _);
            }
            _targetsLeft = TargetCount;
        }

        private void OnDrawGizmos()
        {
            if (_state == null) return;

            // 地图边界（判定半可视化）
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(_map.HalfWidth * 2f, 2f, _map.HalfDepth * 2f));

            // 活体：胶囊近似（半径/高度 = hitscan 判定圆柱）+ 朝向射线
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!_state.IsAlive(i)) continue;

                ref EntitySlot e = ref _state.Entities[i];
                Vector3 pos = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                bool isPlayer = e.Id == _playerId;

                Gizmos.color = isPlayer ? Color.green : new Color(1f, 0.5f, 0.2f);
                Gizmos.DrawWireSphere(pos + Vector3.up * (CombatConfig.HitscanHeight * 0.5f), CombatConfig.HitscanRadius);
                Gizmos.DrawLine(pos, pos + Vector3.up * CombatConfig.HitscanHeight);
                Vector3 mid = pos + Vector3.up * (CombatConfig.HitscanHeight * 0.5f);
                Gizmos.DrawRay(mid, new Vector3(Mathf.Cos(e.Yaw), 0f, Mathf.Sin(e.Yaw)) * 2f);
            }

            // 近期 hitscan 线与命中点（按存留时长淡出）
            for (int k = 0; k < _vis.Length; k++)
            {
                ref FireVis v = ref _vis[k];
                if (v.Time <= 0f) continue;
                if (Time.time - v.Time > VisualLineSeconds)
                {
                    v.Time = 0f;
                    continue;
                }

                Gizmos.color = v.IsHit ? Color.red : new Color(1f, 0.9f, 0.2f, 0.7f);
                Gizmos.DrawLine(v.From, v.To);
                if (v.IsHit) Gizmos.DrawWireSphere(v.To, 0.12f);
            }
        }

        public string StatsName => "SimSandbox";

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear(); // 契约：实现 Clear + 填（HUD 不清）
            if (_state == null) return;

            into["frame"] = _state.Frame.ToString();
            into["alive"] = _state.AliveCount().ToString();
            into["checksum"] = SimChecksum.ComputeChecksum(_state).ToString();
            into["catchUp"] = _driver.StepsLastTick.ToString();
            into["paused"] = _paused.ToString();
            into["targets"] = _targetsLeft.ToString();
            into["eventOverflow"] = _state.Events.OverflowCount.ToString();
        }
    }

    /// <summary>沙盒自创建入口（场景不挂组件；仅 SimSandbox 场景自建，幂等）。release 构建随外层 #if 剥离。</summary>
    internal static class SimSandboxBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (SceneManager.GetActiveScene().name != "SimSandbox") return;
            if (UnityEngine.Object.FindAnyObjectByType<SimSandbox>() != null) return;
            new GameObject("[SimSandbox]").AddComponent<SimSandbox>();
        }
    }
}
#endif
