# -*- coding: utf-8 -*-
"""Local CDN config loader shared by publish-candidate / serve-cdn."""

import json
import sys
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402


@dataclass(frozen=True)
class LocalCdnConfig:
    config_path: Path
    directory: Path
    bind: str
    port: int


def default_config_path():
    return Path(__file__).resolve().with_name("local-cdn.json")


def load_config(config_path=None):
    path = Path(config_path).resolve() if config_path else default_config_path()
    if not path.is_file():
        raise FileNotFoundError(f"local CDN config missing: {path} (it is the single source for serve directory/address)")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"local CDN config invalid: {path}: {exc}") from exc
    missing = [key for key in ("directory", "bind", "port") if not data.get(key)]
    if missing:
        raise ValueError(f"local CDN config incomplete: {path} needs {missing} (directory/bind/port)")
    directory = Path(str(data["directory"]))
    if not directory.is_absolute():
        directory = repo_root() / directory
    return LocalCdnConfig(
        config_path=path,
        directory=directory.resolve(),
        bind=str(data["bind"]),
        port=int(data["port"]),
    )
