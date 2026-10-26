# T1 施工指引：构建链平台参数化

> 运行 android-adaptation · 任务 T1 · 红测已就绪：`scripts/tests/test_build_player.py`（19 例，当前红）
> 验证命令：`python -m unittest discover -s scripts/tests -p test_build_player.py -v`

## 目标

让 `build-player.py` 按 profile 驱动平台差异（NamedBuildTarget / 后端 / 产物后缀 / artifactGlob / 内置包平台白名单），`BuiltinBundleBuildInvoker` 支持 Android 目标，`toolchain.py build` 参数 bug 修复。Windows 档行为零变化。

---

## 改动 1／3：`scripts/build/build-player.py`

### 1a. 在 `def parse_args(argv=None):` 之前插入平台映射纯函数

定位锚点：`from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402` 之后、`def parse_args` 之前。

```python
# ---- 平台映射（profile → Unity 构建参数；纯函数，供 L1 单测） ----

# profile.platform → 默认 buildTargetGroup（profile 显式 buildTargetGroup 优先）。
PLATFORM_TARGET_GROUPS = {
    "StandaloneWindows64": "Standalone",
    "Android": "Android",
}

# profile.platform → Player 产物扩展名。
PLATFORM_PLAYER_EXTENSIONS = {
    "StandaloneWindows64": ".exe",
    "Android": ".apk",
}

# 内置内容包（YooAsset Offline）已接线的平台；新平台先接线 BuiltinBundleBuildInvoker 再登记。
BUNDLE_PLATFORMS = ("StandaloneWindows64", "Android")


def named_build_target(profile):
    group = profile.get("buildTargetGroup") or PLATFORM_TARGET_GROUPS.get(profile.get("platform") or "")
    if group not in ("Standalone", "Android"):
        raise RuntimeError(f"profile buildTargetGroup unsupported: {profile.get('buildTargetGroup')!r}")
    return group


def player_extension(profile):
    platform = profile.get("platform") or ""
    if platform not in PLATFORM_PLAYER_EXTENSIONS:
        raise RuntimeError(f"player extension unknown for platform: {platform!r}")
    return PLATFORM_PLAYER_EXTENSIONS[platform]


def artifact_glob(profile):
    return profile.get("artifactGlob") or "*" + player_extension(profile)


def bundles_supported(profile):
    return (profile.get("platform") or "") in BUNDLE_PLATFORMS


def backend_switch_cs(profile):
    target = named_build_target(profile)
    implementation = "Mono" if str(profile.get("backend") or "").lower() == "mono" else "IL2CPP"
    return (
        f"UnityEditor.Build.NamedBuildTarget t = UnityEditor.Build.NamedBuildTarget.{target};\n"
        f"UnityEditor.PlayerSettings.SetScriptingBackend(t, UnityEditor.ScriptingImplementation.{implementation});\n"
        "return UnityEditor.PlayerSettings.GetScriptingBackend(t).ToString();\n"
    )


def build_output_expr(profile):
    extension = player_extension(profile)
    return (
        'System.IO.Path.Combine("' + str(profile.get("output")) + '", '
        "UnityEditor.PlayerSettings.productName + \"" + extension + "\")"
    )


def bundles_cs(profile):
    platform = profile.get("platform") or ""
    return (
        "public static class BuildBundles\n"
        "{\n"
        "    public static string Start()\n"
        "    {\n"
        f"        LiteGame.EditorTools.BuiltinBundleBuildInvoker.RunAsync(UnityEditor.BuildTarget.{platform});\n"
        "        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status;\n"
        "    }\n"
        "\n"
        "    public static string Status()\n"
        "    {\n"
        "        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status\n"
        "             + \" | out=\" + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;\n"
        "    }\n"
        "}\n"
    )
```

### 1b. 替换后端切换内联 C#

定位锚点（整段 `builder.eval_file(...)` 三行字符串）：

```python
            builder.eval_file(
                "build-backend-switch.cs",
                "UnityEditor.Build.NamedBuildTarget t = UnityEditor.Build.NamedBuildTarget.Standalone;\n"
                "UnityEditor.PlayerSettings.SetScriptingBackend(t, UnityEditor.ScriptingImplementation.IL2CPP);\n"
                "return UnityEditor.PlayerSettings.GetScriptingBackend(t).ToString();\n",
            )
```

替换为：

```python
            builder.eval_file("build-backend-switch.cs", backend_switch_cs(profile))
```

### 1c. 替换内置内容包门禁与 C# 生成

定位锚点（`elif profile.get("platform") != "StandaloneWindows64":` 起，到 `builder.eval_file("build-bundles.cs", bundle_cs)` 止的整段 `elif/else`）。

替换为：

```python
        elif not bundles_supported(profile):
            raise RuntimeError(
                f"profile platform is {profile.get('platform')} - builtin bundles not wired for it; "
                "use --skip-bundles to acknowledge"
            )
        else:
            info("build/player", "== bundles: build builtin content bundles (Offline) ==")
            builder.eval_file("build-bundles.cs", bundles_cs(profile))
```

### 1d. 替换产物输出表达式

定位锚点：

```python
        output_expr = (
            'System.IO.Path.Combine("' + str(profile.get("output")) + '", '
            "UnityEditor.PlayerSettings.productName + \".exe\")"
        )
```

替换为：

```python
        output_expr = build_output_expr(profile)
```

### 1e. 替换产物 glob 局部变量（注意改名，避免遮蔽同名函数）

定位锚点：

```python
        artifact_glob = profile.get("artifactGlob") or "*.exe"
        artifacts = sorted(path for path in out_dir.glob(artifact_glob) if path.is_file()) if out_dir.is_dir() else []
        if not artifacts:
            raise RuntimeError(f"no artifact ({artifact_glob}) under {out_dir} - artifact verification failed")
```

替换为：

```python
        glob_pattern = artifact_glob(profile)
        artifacts = sorted(path for path in out_dir.glob(glob_pattern) if path.is_file()) if out_dir.is_dir() else []
        if not artifacts:
            raise RuntimeError(f"no artifact ({glob_pattern}) under {out_dir} - artifact verification failed")
```

---

## 改动 2／3：`Assets/LiteGame/Editor/Resource/BuiltinBundleBuildInvoker.cs`

### 2a. `RunAsync()` → 委托 + 显式平台重载

定位锚点：`public static void RunAsync()` 整个方法。

替换为：

```csharp
        /// <summary>异步启动构建（默认 Windows 档；保留既有调用方语义）。</summary>
        public static void RunAsync() => RunAsync(BuildTarget.StandaloneWindows64);

        /// <summary>异步启动构建——显式目标平台（调用方轮询 <see cref="Status"/>）。</summary>
        public static void RunAsync(BuildTarget buildTarget)
        {
            Status = "starting";
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Status = "running";
                    Run(buildTarget);
                    Status = "success";
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Status = "failed: " + ex.Message;
                }
            };
        }
```

### 2b. `RunCandidateAsync()` 同形参数化

定位锚点：`public static void RunCandidateAsync()` 整个方法。

替换为：

```csharp
        /// <summary>候选构建异步入口（默认 Windows 档）。</summary>
        public static void RunCandidateAsync() => RunCandidateAsync(BuildTarget.StandaloneWindows64);

        /// <summary>候选构建异步入口——显式目标平台。</summary>
        public static void RunCandidateAsync(BuildTarget buildTarget)
        {
            Status = "starting";
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Status = "running";
                    RunForCandidate(buildTarget, out _);
                    Status = "success";
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Status = "failed: " + ex.Message;
                }
            };
        }
```

### 2c. `RunForCandidate` 参数化

定位锚点：

```csharp
        public static void RunForCandidate(out string outputDirectory)
            => RunCore(BuildTarget.StandaloneWindows64, EBundledCopyOption.None, out outputDirectory);
```

替换为：

```csharp
        public static void RunForCandidate(out string outputDirectory)
            => RunForCandidate(BuildTarget.StandaloneWindows64, out outputDirectory);

        public static void RunForCandidate(BuildTarget buildTarget, out string outputDirectory)
            => RunCore(buildTarget, EBundledCopyOption.None, out outputDirectory);
```

### 2d. `RunCore` 目标组不再写死

定位锚点：

```csharp
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, buildTarget);
            }
```

替换为：

```csharp
            if (EditorUserBuildSettings.activeBuildTarget != buildTarget)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(buildTarget), buildTarget);
            }
```

同时把该处上方 XML 注释里"目标固定并主动切换"改为"目标由调用方显式传入并主动切换"（注释只描述现有实现）。

---

## 改动 3／3：`scripts/toolchain.py`

定位锚点：

```python
def cmd_build(args):
    command = [sys.executable, str(ROOT / "scripts" / "build" / "build-player.py")]
    if args.profile:
        command += ["-Profile", args.profile]
    return _run_child(command)
```

替换为：

```python
def build_command(profile=None):
    command = [sys.executable, str(ROOT / "scripts" / "build" / "build-player.py")]
    if profile:
        command += ["--profile", profile]
    return command


def cmd_build(args):
    return _run_child(build_command(args.profile))
```

---

## 完成后

回复"T1 改完"（或贴报错）。主代理随后执行：

1. `python -m unittest discover -s scripts/tests -p test_build_player.py -v`（19 例转绿）
2. `python scripts/gate/test.py --suite L1 --profile PullRequest`（含纪律扫描）
3. `python scripts/toolchain.py check`
4. `unity_editor.refresh`（Unity 编译 0 错）

## 注意

- 不要改 `scripts/tests/test_build_player.py`（测试是主代理的职责；断言与意图冲突请直接说）。
- 本次只动这三个文件；`BuildProfiles/android-il2cpp.json` 与 PlayerSettings 不碰（前者已就绪，后者属 T3）。
