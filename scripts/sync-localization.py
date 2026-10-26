#!/usr/bin/env python3
"""Check DiskPilot-X localization resources stay in sync.

Run after pulling upstream changes:
    python scripts/sync-localization.py --check

The script verifies that every localized .resx has exactly the same keys as
Localization/Strings.resx and reports likely hard-coded UI strings in XAML/C#.
"""

from __future__ import annotations

import argparse
import re
import sys
import io
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parents[1]
APP_ROOT = ROOT / "src" / "DiskPilotX.App"
LOC_ROOT = APP_ROOT / "Localization"
SOURCE_RESX = LOC_ROOT / "Strings.resx"
LOCALIZED_RESX = [LOC_ROOT / "Strings.zh-CN.resx", LOC_ROOT / "Strings.zh-TW.resx"]

XAML_ATTR_RE = re.compile(r'\b(Content|Header|Title|ToolTip)="([^"{}&][^"]*[A-Za-z][^"]*)"')
XAML_TEXT_RE = re.compile(r'\bText="([^"{}&][^"]*[A-Za-z][^"]*)"')
CS_LITERAL_RE = re.compile(r'(?<![A-Za-z0-9_])"([^"\\]*(?:\\.[^"\\]*)*)"')

IGNORED_CS_PREFIXES = (
    "http://",
    "https://",
    "clr-namespace:",
    "pack://",
    "/select,",
)
IGNORED_CS_VALUES = {
    "Anthropic",
    "explorer.exe",
    "yyyy-MM-dd HH:mm:ss",
    "powershell",
    "bash",
    "0 bytes",
    "bytes",
    "1.5 MB",
    "512 bytes",
    "FileSizeConverter does not support ConvertBack.",
    "Converting from Brush to SafetyRating is not supported.",
    "g",
    "#1E1E1E",
    "#4CAF50",
    "#FF9800",
    "#F44336",
    "#9E9E9E",
}
IGNORED_XAML_VALUES = {
    "X",
    " &#x2588;",
}


def load_resx_keys(path: Path) -> set[str]:
    tree = ET.parse(path)
    return {node.attrib["name"] for node in tree.findall("data") if "name" in node.attrib}


def check_resource_keys() -> list[str]:
    errors: list[str] = []
    source_keys = load_resx_keys(SOURCE_RESX)

    for localized_path in LOCALIZED_RESX:
        localized_keys = load_resx_keys(localized_path)
        missing = sorted(source_keys - localized_keys)
        extra = sorted(localized_keys - source_keys)

        if missing:
            errors.append(f"{localized_path.relative_to(ROOT)} missing keys: {', '.join(missing)}")
        if extra:
            errors.append(f"{localized_path.relative_to(ROOT)} extra keys: {', '.join(extra)}")

    return errors


def is_probably_user_facing_cs(value: str) -> bool:
    if not value.strip() or not any(char.isalpha() for char in value):
        return False
    if value in IGNORED_CS_VALUES:
        return False
    if value.startswith(IGNORED_CS_PREFIXES):
        return False
    if re.fullmatch(r"[Nn][0-9]+", value):
        return False
    if re.fullmatch(r"#[0-9A-Fa-f]{6,8}", value):
        return False
    if re.fullmatch(r"[A-Za-z0-9_.-]+", value) and "." in value:
        return False
    if "{" in value and "}" in value and any(token in value for token in ("bytes", "KB", "MB", "GB", "TB")):
        return False
    return True


def scan_hardcoded_strings() -> list[str]:
    warnings: list[str] = []

    for path in APP_ROOT.rglob("*.xaml"):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        for line_no, line in enumerate(text.splitlines(), start=1):
            for regex in (XAML_ATTR_RE, XAML_TEXT_RE):
                for match in regex.finditer(line):
                    value = match.group(2) if regex is XAML_ATTR_RE else match.group(1)
                    if value not in IGNORED_XAML_VALUES:
                        warnings.append(f"{path.relative_to(ROOT)}:{line_no}: hard-coded XAML string: {value}")

    for path in APP_ROOT.rglob("*.cs"):
        if any(part in {"bin", "obj"} for part in path.parts):
            continue
        if path.name.endswith(".g.cs") or "Localization" in path.parts:
            continue
        text = path.read_text(encoding="utf-8-sig")
        for line_no, line in enumerate(text.splitlines(), start=1):
            stripped = line.strip()
            if stripped.startswith("//") or "L.Text(" in line or "L.Format(" in line or "nameof(" in line:
                continue
            for match in CS_LITERAL_RE.finditer(line):
                try:
                    value = bytes(match.group(1), "utf-8").decode("unicode_escape")
                except UnicodeDecodeError:
                    value = match.group(1)
                if is_probably_user_facing_cs(value):
                    warnings.append(f"{path.relative_to(ROOT)}:{line_no}: review hard-coded C# string: {value}")

    return warnings


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="return non-zero when resources or likely UI strings are out of sync")
    args = parser.parse_args()

    issues = check_resource_keys() + scan_hardcoded_strings()

    if issues:
        print("Localization sync found issues:")
        for issue in issues:
            print(f"- {issue}")
        return 1 if args.check else 0

    print("Localization resources are in sync.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
