"""Conservative repository secret-pattern gate. Not a substitute for provider secret scanning."""
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PATTERNS = {
    "private-key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"),
    "github-token": re.compile(r"\b(?:ghp_|gho_|ghu_|ghs_|ghr_)[A-Za-z0-9]{36,}\b"),
    "github-fine-grained-token": re.compile(r"\bgithub_pat_[A-Za-z0-9_]{60,}\b"),
    "aws-access-key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
}


def scan(content: str) -> list[str]:
    return [name for name, pattern in PATTERNS.items() if pattern.search(content)]


def main() -> int:
    paths = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).split(b"\0")
    failures = []
    for raw in paths:
        if not raw:
            continue
        path = ROOT / raw.decode("utf-8", "surrogateescape")
        if path == pathlib.Path(__file__).resolve():
            continue  # scanner definitions are not credentials
        if not path.is_file() or path.stat().st_size > 2_000_000:
            continue
        try:
            contents = path.read_text(encoding="utf-8")
        except (UnicodeError, OSError):
            continue
        for kind in scan(contents):
            failures.append(f"{path.relative_to(ROOT)}: {kind}")
    for finding in failures:
        print(finding)
    print(f"Secret-pattern scan: {len(failures)} findings")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
