"""Compile and run the check of which address the program listens on: Tailscale's, and no other.

    python tests/tailnet_check.py
"""
import os
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
with tempfile.TemporaryDirectory(prefix="remote-cli-tailnet-") as folder:
    exe = Path(folder) / "check.exe"
    subprocess.run([str(compiler), "/nologo", "/target:exe", "/platform:x64", "/codepage:65001", "/main:TailnetCheck", "/out:" + str(exe),
                    str(ROOT / "agent-windows/Tailnet.cs"), str(ROOT / "tests/TailnetCheck.cs")], check=True)
    subprocess.run([str(exe)], check=True, timeout=60)
