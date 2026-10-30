# -*- coding: utf-8 -*-
"""Android 主清单明文（cleartext）通道策略契约。

源码清单是**安全默认**（不放明文）；development 通道出包时由构建链注入，收尾还原；
其它通道出包前断言清单干净——明文泄漏必须让出包失败，而不是静默出包。
"""

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts" / "build"))
sys.path.insert(0, str(ROOT / "scripts"))

import importlib.util  # noqa: E402

SPEC = importlib.util.spec_from_file_location("build_player_manifest", ROOT / "scripts" / "build" / "build-player.py")
MOD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MOD)

MANIFEST = ROOT / "Assets" / "Plugins" / "Android" / "AndroidManifest.xml"

CLEAN = """<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <application>
        <activity android:name="com.unity3d.player.UnityPlayerActivity" />
    </application>
</manifest>
"""


class SourceManifestTests(unittest.TestCase):
    def test_source_manifest_has_no_cleartext(self):
        self.assertFalse(MOD.manifest_allows_cleartext(MANIFEST.read_text(encoding="utf-8")),
                         "源码清单必须保持不放明文（安全默认）——明文只在 development 出包时注入")

    def test_source_manifest_keeps_required_declarations(self):
        text = MANIFEST.read_text(encoding="utf-8")
        self.assertIn("android.permission.INTERNET", text)
        self.assertIn("com.unity3d.player.UnityPlayerActivity", text)
        self.assertIn("android:screenOrientation=\"landscape\"", text)


class ApplyTests(unittest.TestCase):
    def test_apply_inserts_attribute_into_application_tag(self):
        patched = MOD.apply_cleartext(CLEAN, True)
        self.assertTrue(MOD.manifest_allows_cleartext(patched))
        self.assertIn('<application android:usesCleartextTraffic="true">', patched)
        # 其余结构不变
        self.assertIn("com.unity3d.player.UnityPlayerActivity", patched)

    def test_apply_is_idempotent(self):
        once = MOD.apply_cleartext(CLEAN, True)
        self.assertEqual(once, MOD.apply_cleartext(once, True))

    def test_remove_strips_attribute(self):
        patched = MOD.apply_cleartext(CLEAN, True)
        self.assertEqual(CLEAN, MOD.apply_cleartext(patched, False))

    def test_missing_application_tag_is_rejected(self):
        with self.assertRaises(RuntimeError):
            MOD.apply_cleartext("<manifest></manifest>", True)


class ChannelTests(unittest.TestCase):
    def test_development_channel_allows_cleartext(self):
        self.assertTrue(MOD.channel_allows_cleartext({"channel": "development"}))

    def test_release_channel_forbids_cleartext(self):
        self.assertFalse(MOD.channel_allows_cleartext({"channel": "release"}))
        self.assertFalse(MOD.channel_allows_cleartext({}))

    def test_release_rejects_leftover_cleartext(self):
        with self.assertRaises(RuntimeError):
            MOD.assert_cleartext_absent(MOD.apply_cleartext(CLEAN, True))

    def test_release_accepts_clean_manifest(self):
        MOD.assert_cleartext_absent(CLEAN)


class SyncTests(unittest.TestCase):
    def _project(self, text):
        temp = tempfile.TemporaryDirectory()
        path = Path(temp.name) / MOD.ANDROID_MANIFEST
        path.parent.mkdir(parents=True)
        path.write_text(text, encoding="utf-8", newline="\n")
        return temp, path

    def test_development_sync_injects_and_returns_backup(self):
        temp, path = self._project(CLEAN)
        with temp:
            backup = MOD.sync_android_cleartext(Path(temp.name), {"channel": "development"})
            self.assertTrue(MOD.manifest_allows_cleartext(path.read_text(encoding="utf-8")))
            self.assertEqual(CLEAN, backup)

    def test_development_sync_on_patched_file_is_noop(self):
        temp, path = self._project(MOD.apply_cleartext(CLEAN, True))
        with temp:
            self.assertIsNone(MOD.sync_android_cleartext(Path(temp.name), {"channel": "development"}))

    def test_release_sync_leaves_clean_manifest_untouched(self):
        temp, path = self._project(CLEAN)
        with temp:
            self.assertIsNone(MOD.sync_android_cleartext(Path(temp.name), {"channel": "release"}))
            self.assertEqual(CLEAN, path.read_text(encoding="utf-8"))

    def test_restore_puts_source_back(self):
        temp, path = self._project(CLEAN)
        with temp:
            backup = MOD.sync_android_cleartext(Path(temp.name), {"channel": "development"})
            MOD.restore_android_cleartext(Path(temp.name), backup)
            self.assertEqual(CLEAN, path.read_text(encoding="utf-8"))

    def test_missing_manifest_is_an_error(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(RuntimeError):
                MOD.sync_android_cleartext(Path(temp), {"channel": "development"})


if __name__ == "__main__":
    unittest.main()
