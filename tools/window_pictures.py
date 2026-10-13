"""Draws the Windows program's pages into docs/images/ for the README, with sample settings in a temporary
data folder (the real settings are not touched). The program's relay listens on 127.0.0.1 (REMOTECLI_ADDRESS)
instead of this computer's Tailscale address, which stays out of the pictures. The windows stay off the screen.
Run after tools/package_windows.py.

    python tools/window_pictures.py
"""
import json
import os
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROGRAM = ROOT / ".build" / "windows" / "stage" / "RemoteCli.exe"
PAGES = {"windows.png": ["0"], "windows-projects.png": ["1"], "windows-settings.png": ["3"]}


def main():
    public = Path(os.environ.get("PUBLIC", r"C:\Users\Public"))
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as data:
        settings = {"Port": 8735, "RemoteDirs": ["web-shop=%s" % (public / "Documents"), "thesis=%s" % (public / "Pictures"), "data-tools=%s" % (public / "Music")]}
        Path(data, "config.json").write_text(json.dumps(settings), encoding="utf-8")
        for name, pressed in PAGES.items():
            picture = ROOT / "docs" / "images" / name
            subprocess.run([str(PROGRAM), "--screenshot", str(picture), *pressed], env=dict(os.environ, REMOTECLI_DATA=data, REMOTECLI_ADDRESS="127.0.0.1"), check=True, timeout=120)
            print(name)


if __name__ == "__main__":
    main()
