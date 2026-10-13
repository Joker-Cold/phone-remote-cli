"""Raises the version everywhere it is kept: the computer program, the relay, the packaging and the Android App, whose
versionCode goes up by one. Without an argument the last number goes up (1.0.6 -> 1.0.7).

    python tools/bump_version.py [1.1.0]
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
# file, pattern around the version; group 1 is the version
PLACES = [("agent-windows/RemoteCliApp.cs", r'const string Version = "([\d.]+)"'),
          ("relay/server.py", r'^VERSION = "([\d.]+)"'),
          ("tools/package_windows.py", r'^VERSION = "([\d.]+)"'),
          ("android/AndroidManifest.xml", r'android:versionName="([\d.]+)"')]
CODE = ("android/AndroidManifest.xml", r'android:versionCode="(\d+)"')


def found(path, pattern):
    text = (ROOT / path).read_bytes().decode("utf-8")
    match = re.search(pattern, text, re.M)
    if not match:
        raise SystemExit("no version in " + path)
    return text, match


def put(path, text, match, value):
    (ROOT / path).write_bytes((text[:match.start(1)] + value + text[match.end(1):]).encode("utf-8"))


def main():
    now = {found(path, pattern)[1].group(1) for path, pattern in PLACES}
    if len(now) != 1:
        raise SystemExit("the versions differ: " + ", ".join(sorted(now)))
    old = now.pop()
    if len(sys.argv) > 1:
        new = sys.argv[1]
        if not re.fullmatch(r"\d+(\.\d+)*", new):
            raise SystemExit("not a version: " + new)
    else:
        parts = old.split(".")
        new = ".".join(parts[:-1] + [str(int(parts[-1]) + 1)])
    for path, pattern in PLACES:
        put(path, *found(path, pattern), new)
    text, match = found(*CODE)
    code = int(match.group(1)) + 1
    put(CODE[0], text, match, str(code))
    print("%s -> %s, versionCode %d" % (old, new, code))


if __name__ == "__main__":
    main()
