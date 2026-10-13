"""The checks to run before every commit: those that need no browser and no built package, about a minute in all.
Stops at the first that fails. The JDK for the Android check comes from --jdk, JAVA_HOME or D:\\java17.

    python tests/quick.py [--jdk <JDK directory>]
"""
import argparse
import os
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PY, NODE = [sys.executable], ["node"]
CHECKS = [PY + ["-m", "unittest", "discover", "-s", "relay", "-p", "test_*.py"],
          PY + ["tests/tailnet_check.py"],
          NODE + ["tests/bridge_check.js"],
          NODE + ["tests/terminal_scroll_check.js"],
          NODE + ["tests/session_state_check.js"],
          PY + ["tests/said_check.py"],
          PY + ["tests/session_copies_check.py"],
          PY + ["tests/history_check.py"],
          PY + ["tests/native_owner_check.py"],
          PY + ["tests/e2e_windows.py"],
          PY + ["tests/look_check.py"],
          PY + ["tests/files_check.py"],
          PY + ["tests/move_check.py"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jdk", default=os.environ.get("JAVA_HOME") or r"D:\java17")
    checks = CHECKS + [PY + ["tests/android_aggregate_check.py", "--jdk", parser.parse_args().jdk]]
    began = time.time()
    for command in checks:
        name = " ".join(command[1:])
        start = time.time()
        done = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if done.returncode != 0:
            print("FAILED  %s\n%s%s" % (name, done.stdout[-3000:], done.stderr[-3000:]))
            return 1
        print("ok %5.1fs  %s" % (time.time() - start, name))
    print("all %d checks passed in %.0fs" % (len(checks), time.time() - began))
    return 0


if __name__ == "__main__":
    sys.exit(main())
