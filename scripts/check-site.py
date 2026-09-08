#!/usr/bin/env python3
"""Check the GitHub Pages site before it is published.

A static site has no compiler, so nothing otherwise catches a broken anchor, a missing image, or a
claim that has drifted out of step with what the software actually does. This script is that gate,
and CI runs it on every push.

Standard library only, deliberately: the Pages workflow should not need a dependency install to
verify a page with no build step.

Usage:
    python scripts/check-site.py
"""

from __future__ import annotations

import re
import sys
from html.parser import HTMLParser
from pathlib import Path

SITE = Path(__file__).resolve().parent.parent / "site"

# Claims the site must keep making. Each one exists because dropping it would make the page
# misleading rather than merely incomplete.
REQUIRED_CONTENT = {
    "the Vega limitation": "Vega OS",
    "that Vega is permanent": "no future version will change that",
    "the unsigned-build warning": "Windows protected your PC",
    "the platform requirement": "Windows 10 or 11",
    "the availability statement": "Unavailable",
    "the privacy position": "no analytics",
    "that tokens are not encryption": "they do not encrypt anything",
    "the maker": "REX Technologies",
}

# Text that means a draft escaped.
FORBIDDEN_PATTERNS = [
    r"\bTODO\b",
    r"\bFIXME\b",
    r"\bTBD\b",
    r"\bLorem ipsum\b",
    r"\bXXX\b",
    r"\bcoming soon\b",
]


class PageParser(HTMLParser):
    """Collects the ids, links and asset references a page depends on."""

    def __init__(self) -> None:
        super().__init__()
        self.ids: set[str] = set()
        self.hrefs: list[str] = []
        self.assets: list[str] = []
        self.title: str | None = None
        self._in_title = False
        self.images_without_alt: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = {key: (value or "") for key, value in attrs}

        if "id" in values:
            self.ids.add(values["id"])

        if tag == "title":
            self._in_title = True

        if tag == "a" and "href" in values:
            self.hrefs.append(values["href"])

        if tag == "img":
            source = values.get("src", "")
            if source:
                self.assets.append(source)
            # An image with no alt text is invisible to a screen reader.
            if not values.get("alt", "").strip():
                self.images_without_alt.append(source or "(no src)")

        if tag == "link" and "href" in values:
            self.assets.append(values["href"])

    def handle_endtag(self, tag: str) -> None:
        if tag == "title":
            self._in_title = False

    def handle_data(self, data: str) -> None:
        if self._in_title:
            self.title = (self.title or "") + data


def check() -> list[str]:
    """Returns a list of problems. Empty means the site is publishable."""
    problems: list[str] = []

    index = SITE / "index.html"
    if not index.exists():
        return [f"{index} is missing; there is no site to publish."]

    html = index.read_text(encoding="utf-8")
    parser = PageParser()
    parser.feed(html)

    if not parser.title or "Flint" not in parser.title:
        problems.append("The page title does not name Flint.")

    # Local assets must exist. A remote one is the network's problem, not this check's.
    for asset in parser.assets:
        if asset.startswith(("http://", "https://", "data:", "//")):
            continue
        if not (SITE / asset).exists():
            problems.append(f"Asset '{asset}' is referenced but missing from site/.")

    # Every in-page anchor must land somewhere.
    for href in parser.hrefs:
        if not href.startswith("#"):
            continue
        target = href[1:]
        if target and target not in parser.ids:
            problems.append(f"Anchor '{href}' points at an id that does not exist.")

    for asset in parser.images_without_alt:
        problems.append(f"Image '{asset}' has no alt text.")

    for description, needle in REQUIRED_CONTENT.items():
        if needle not in html:
            problems.append(f"The site no longer states {description} (looked for '{needle}').")

    for pattern in FORBIDDEN_PATTERNS:
        match = re.search(pattern, html, re.IGNORECASE)
        if match:
            problems.append(f"Draft text left in the page: '{match.group(0)}'.")

    # The repository slug appears in several links; one stale copy sends people to a 404. A clone
    # URL legitimately carries a .git suffix, so that is normalised rather than flagged.
    slugs = {
        slug.removesuffix(".git")
        for slug in re.findall(r"https://github\.com/([^/\"'\s]+/[^/\"'\s]+)", html)
    }
    if len(slugs) > 1:
        problems.append(f"The page points at more than one repository: {sorted(slugs)}.")

    if not (SITE / ".nojekyll").exists():
        problems.append(
            "site/.nojekyll is missing. Without it Pages runs the content through Jekyll, which "
            "discards files beginning with an underscore."
        )

    return problems


def main() -> int:
    problems = check()

    if problems:
        print(f"  {len(problems)} problem(s) with the site:\n")
        for problem in problems:
            print(f"  - {problem}")
        print()
        return 1

    print("  Site checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
