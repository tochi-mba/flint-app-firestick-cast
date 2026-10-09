#!/usr/bin/env python3
"""List the changed lines in src that no test reached.

Every change to Flint is held to one bar: each line and branch it adds or modifies outside the
tests is covered. This script is how that bar is checked. It reads the lines a change touched from
git, reads any number of coverage reports - cobertura from coverlet for C#, JaCoCo for Kotlin - takes
the best result for each line across all of them, and prints, file by file, what is not fully
covered. A line that cannot be reached without hardware is moved behind an interface so only the
one-line call is left, and that line is named in the pull request.

Standard library only, so it runs wherever the tests do.

Usage:
    dotnet test apps/windows/tests/Flint.Core.Tests --collect "XPlat Code Coverage" --results-directory cov
    python tools/scripts/changed-line-coverage.py --base origin/master <each coverage.cobertura.xml under cov>

Exits 1 when any changed line or branch is not covered, so it can gate a script.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[2]

# The languages whose coverage is measured. Rust has cargo llvm-cov, which reports on its own.
MEASURED = ("*.cs", "*.kt")


class LineCoverage:
    """The best a line did across every report: whether it ran, and how many of its branches did."""

    def __init__(self) -> None:
        self.hits = 0
        self.branches_covered = 0
        self.branches = 0

    def merge(self, hits: int, branches_covered: int, branches: int) -> None:
        self.hits = max(self.hits, hits)
        if branches:
            self.branches_covered = max(self.branches_covered, branches_covered)
            self.branches = max(self.branches, branches)

    @property
    def missed(self) -> bool:
        return self.hits == 0

    @property
    def partial(self) -> bool:
        return self.hits > 0 and self.branches_covered < self.branches


def changed_lines(base: str, head: str) -> dict[str, set[int]]:
    """The lines each source file gained or changed between two revisions, numbered as in head."""
    diff = subprocess.run(
        ["git", "diff", "-U0", "--no-color", base, head, "--", *MEASURED],
        capture_output=True, text=True, encoding="utf-8", check=True, cwd=REPOSITORY,
    ).stdout
    lines: dict[str, set[int]] = defaultdict(set)
    path = None
    for text in diff.splitlines():
        if text.startswith("+++ "):
            path = None if text.endswith("/dev/null") else text[6:]
        elif text.startswith("@@") and path and not is_test(path):
            hunk = re.search(r"\+(\d+)(?:,(\d+))?", text)
            start, count = int(hunk.group(1)), int(hunk.group(2) or "1")
            lines[path].update(range(start, start + count))
    return lines


def is_test(path: str) -> bool:
    """Tests are what reach the code, not code to be reached."""
    parts = path.split("/")
    return "tests" in parts or "test" in parts or "androidTest" in parts


def read_reports(paths: list[str]) -> dict[str, dict[int, LineCoverage]]:
    """Every line in every report, keyed by the file name as the report gives it."""
    coverage: dict[str, dict[int, LineCoverage]] = defaultdict(lambda: defaultdict(LineCoverage))
    for path in paths:
        root = ET.parse(path).getroot()
        if root.tag == "coverage":
            for owner in root.iter("class"):
                name = owner.get("filename", "").replace("\\", "/")
                for line in owner.iter("line"):
                    covered = branches = 0
                    condition = line.get("condition-coverage")
                    if line.get("branch") == "True" and condition:
                        covered, branches = (int(part) for part in condition.split("(")[1].rstrip(")").split("/"))
                    coverage[name][int(line.get("number", "0"))].merge(int(line.get("hits", "0")), covered, branches)
        elif root.tag == "report":
            for package in root.iter("package"):
                for source in package.iter("sourcefile"):
                    name = f"{package.get('name')}/{source.get('name')}"
                    for line in source.iter("line"):
                        missed, covered = int(line.get("mb", "0")), int(line.get("cb", "0"))
                        ran = 1 if int(line.get("ci", "0")) > 0 else 0
                        coverage[name][int(line.get("nr", "0"))].merge(ran, covered, missed + covered)
        else:
            raise SystemExit(f"{path} is neither a cobertura nor a JaCoCo report.")
    return coverage


def ends_with(longer: str, shorter: str) -> bool:
    """Whole path components only, so FileSoundMemory.cs never stands in for SoundMemory.cs."""
    return longer == shorter or longer.endswith("/" + shorter.lstrip("/"))


def reports_for(path: str, coverage: dict[str, dict[int, LineCoverage]]) -> list[str]:
    """The report entries that describe one repository file.

    Coverlet names a file by its absolute path, and JaCoCo by its package, which mirrors the folders
    it sits in, so one is a suffix of the repository path and the other has it as a suffix.
    """
    return [name for name in coverage if ends_with(path, name) or ends_with(name, path)]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--base", required=True, help="the revision the change starts from, such as origin/master")
    parser.add_argument("--head", default="HEAD", help="the revision the change ends at (default: HEAD)")
    parser.add_argument("reports", nargs="+", help="cobertura or JaCoCo XML reports")
    arguments = parser.parse_args()

    coverage = read_reports(arguments.reports)
    gaps = 0
    for path, wanted in sorted(changed_lines(arguments.base, arguments.head).items()):
        names = reports_for(path, coverage)
        if not names:
            print(f"NO DATA  {path}")
            continue

        lines: dict[int, LineCoverage] = defaultdict(LineCoverage)
        for name in names:
            for number, line in coverage[name].items():
                if number in wanted:
                    lines[number].merge(line.hits, line.branches_covered, line.branches)
        if not lines:
            continue

        missed = sorted(number for number, line in lines.items() if line.missed)
        partial = sorted(number for number, line in lines.items() if line.partial)
        ran = sum(1 for line in lines.values() if not line.missed)
        branches = sum(line.branches for line in lines.values())
        covered = sum(line.branches_covered for line in lines.values())
        print(f"{'GAPS' if missed or partial else 'OK':<8} {path}  lines {ran}/{len(lines)}  branches {covered}/{branches}")
        if missed:
            print(f"         not run: {missed}")
        if partial:
            print(f"         branches missed: {[f'{n} ({lines[n].branches_covered}/{lines[n].branches})' for n in partial]}")
        gaps += len(missed) + len(partial)

    print()
    print(f"{gaps} changed line(s) not fully covered." if gaps else "Every changed line and branch is covered.")
    return 1 if gaps else 0


if __name__ == "__main__":
    sys.exit(main())
