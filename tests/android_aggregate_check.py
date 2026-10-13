"""Run the aggregation model, task reminders and connection health against real Java fixtures without an Android device,
and check that the app's sign-in check sends the page's cookie.

    python tests/android_aggregate_check.py --jdk <existing JDK directory>
"""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jdk", default=os.environ.get("JAVA_HOME"))
    args = parser.parse_args()
    if not args.jdk:
        parser.error("Pass --jdk with the existing JDK directory")
    root = Path(__file__).resolve().parents[1]
    # The sign-in check asks /api/session outside the WebView: without the page's cookie the computer always answers
    # signed_in false, and a phone that opened a computer on an earlier sign-in was sent back to scanning.
    main_activity = (root / "android/src/io/github/jokercold/phoneremotecli/MainActivity.java").read_text(encoding="utf-8")
    check = main_activity[main_activity.index("private void checkConnection()"):main_activity.index("private void signedOut(")]
    if 'setRequestProperty("Cookie", cookie)' not in check:
        raise SystemExit("checkConnection asks /api/session without the page's cookie")
    javac = Path(args.jdk) / "bin/javac.exe"
    java = Path(args.jdk) / "bin/java.exe"
    with tempfile.TemporaryDirectory(prefix="remote-cli-aggregate-") as output:
        subprocess.run([str(javac), "-J-Duser.language=en", "-Xlint:-options", "--release", "8", "-encoding", "UTF-8", "-d", output,
                        str(root / "android/src/io/github/jokercold/phoneremotecli/AggregateSessions.java"),
                        str(root / "android/src/io/github/jokercold/phoneremotecli/Watch.java"),
                        str(root / "android/src/io/github/jokercold/phoneremotecli/ConnectionHealth.java"),
                        str(root / "tests/AggregateSessionsCheck.java"), str(root / "tests/WatchCheck.java"),
                        str(root / "tests/ConnectionHealthCheck.java")], check=True)
        subprocess.run([str(java), "-cp", output, "io.github.jokercold.phoneremotecli.AggregateSessionsCheck"], check=True)
        subprocess.run([str(java), "-cp", output, "io.github.jokercold.phoneremotecli.WatchCheck"], check=True)
        subprocess.run([str(java), "-cp", output, "io.github.jokercold.phoneremotecli.ConnectionHealthCheck"], check=True)


if __name__ == "__main__":
    main()
