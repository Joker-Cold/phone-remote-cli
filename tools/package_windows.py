"""Builds the portable Windows program: the two programs, the relay, the app's pages and an official embeddable
Python in the folder dist/phone-remote-cli/, and the same folder packed as dist/phone-remote-cli.zip. Nothing is
installed; RemoteCli.exe is opened from wherever the folder is. Windows only; uses the C# compiler that ships with Windows.

    python tools/package_windows.py
"""
import hashlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.0.6"
PORTABLE = "phone-remote-cli"     # the folder and the zip have the same name in every release
PYTHON = ("3.12.10", "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3")   # version, SHA-256 of the embeddable zip
# Parts of the embeddable Python the relay never loads.
UNUSED = ("_msi.pyd", "_sqlite3.pyd", "sqlite3.dll", "_elementtree.pyd", "pyexpat.pyd", "_wmi.pyd", "_zoneinfo.pyd", "winsound.pyd",
          "_decimal.pyd", "_lzma.pyd", "_bz2.pyd", "_asyncio.pyd", "_overlapped.pyd", "_multiprocessing.pyd", "_ctypes.pyd", "libffi-8.dll", "pythonw.exe", "python.cat")


def python_zip():
    name = "python-%s-embed-amd64.zip" % PYTHON[0]
    path = ROOT / ".cache" / name
    if not path.is_file():
        raise SystemExit("missing %s: put the official Windows embeddable package of Python %s there" % (path, PYTHON[0]))
    if hashlib.sha256(path.read_bytes()).hexdigest() != PYTHON[1]:
        raise SystemExit("unexpected content: " + str(path))
    return path


def main():
    build, dist = ROOT / ".build" / "windows", ROOT / "dist"
    shutil.rmtree(build, ignore_errors=True)
    stage = build / "stage"
    stage.mkdir(parents=True)
    dist.mkdir(exist_ok=True)
    subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "agent-windows" / "build.ps1"), "-OutDir", str(stage)], check=True)
    (stage / "relay").mkdir()
    for name in ("server.py", "relay.py", "screen.py", "websocket.py"):
        shutil.copy2(ROOT / "relay" / name, stage / "relay" / name)
    shutil.copytree(ROOT / "web", stage / "web")
    with zipfile.ZipFile(python_zip()) as archive:
        archive.extractall(stage / "python", [n for n in archive.namelist() if n not in UNUSED])
    for name in ("LICENSE", "THIRD_PARTY.md", "README.md"):
        shutil.copy2(ROOT / name, stage / name)
    # the packed relay must start with the packed Python before it is shipped
    check = subprocess.run([str(stage / "python" / "python.exe"), "-c", "import sys; sys.path.insert(0, r'%s'); import server, relay; print(server.VERSION)" % (stage / "relay")],
                           capture_output=True, text=True)
    if check.returncode != 0 or check.stdout.strip() != VERSION:
        raise SystemExit("the packed relay does not start:\n" + check.stdout + check.stderr)
    portable = dist / PORTABLE
    try:
        if portable.exists():
            shutil.rmtree(portable)
    except PermissionError:
        raise SystemExit("%s is in use: quit Phone Remote CLI there first (tray icon, right click, 退出)" % portable)
    shutil.copytree(stage, portable, ignore=shutil.ignore_patterns("__pycache__"))
    packed = dist / (PORTABLE + ".zip")
    with zipfile.ZipFile(packed, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(portable.rglob("*")):
            if path.is_file():
                archive.write(path, (Path(PORTABLE) / path.relative_to(portable)).as_posix())
    (dist / "phone-remote-cli-setup.exe").unlink(missing_ok=True)       # the installer of 1.0.5 and earlier
    print("%s  RemoteCli.exe in it opens the program" % portable)
    print("%s  %d bytes  sha256 %s" % (packed.name, packed.stat().st_size, hashlib.sha256(packed.read_bytes()).hexdigest()))


if __name__ == "__main__":
    sys.exit(main())
