"""Draws the app's pages into docs/images/ for the README: the list, a project and a terminal in the light and the
night skin (tests/pages_check.py), the Word and PowerPoint previews (tests/files_check.py, needs python-docx and
python-pptx). Everything runs against a temporary relay and agent on 127.0.0.1 with made-up projects in a neutral
folder; the real settings are not touched. Build the agent first. The .jpg pictures in docs/images/ are taken on a phone.

    python tools/app_pictures.py <chrome.exe>
"""
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGES = ROOT / "docs" / "images"

DEMO = Path(os.environ.get("PUBLIC", r"C:\Users\Public")) / "Documents" / "projects"      # a path that appears in the pictures
PAGES = {"": {"overview.png": "overview.png", "project.png": "project.png", "terminal.png": "terminal.png"},
         "night": {"overview.png": "list-dark.png", "terminal.png": "terminal-dark.png"}}
FILES = {"word.png": "files-word.png", "slides.png": "files-slides.png"}


def main():
    chrome = sys.argv[1]
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
        for skin, names in PAGES.items():
            out = Path(temp) / (skin or "paper")
            env = dict(os.environ, RCLI_TEST_SKIN=skin, RCLI_TEST_PORT="8737" if skin else "8736")
            subprocess.run([sys.executable, str(ROOT / "tests" / "pages_check.py"), chrome, str(out), str(DEMO)], env=env, check=True, timeout=600)
            for made, name in names.items():
                shutil.copyfile(out / made, IMAGES / name)
                print(name)
        out = Path(temp) / "files"
        subprocess.run([sys.executable, str(ROOT / "tests" / "files_check.py"), chrome, str(out)], check=True, timeout=900)
        for made, name in FILES.items():
            shutil.copyfile(out / made, IMAGES / name)
            print(name)


if __name__ == "__main__":
    main()
