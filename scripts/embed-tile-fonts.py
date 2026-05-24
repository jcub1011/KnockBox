#!/usr/bin/env python3
"""
Embed subsetted Google Fonts as base64 @font-face declarations in each tile SVG.

SVGs loaded via <img src="..."> run in a sandboxed mode that won't fetch external
fonts — so even though the host page loads Lexend / Cormorant Garamond / etc. from
Google, the tile SVGs can't see them. Embedding a subsetted WOFF2 inline as a
data URL is the only way to get the right typography without giving up the <img>
rendering path.

Run from repo root: python scripts/embed-tile-fonts.py
Requires: pyftsubset (fonttools) on PATH, internet access to fonts.googleapis.com.
"""

import base64
import re
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")

# Per-SVG configuration. `fonts` is a list of (family, weight, italic) faces; the
# union of all glyph strings in `texts` is subsetted from each face.
TILES = {
    "sdk/KnockBox.Platform/wwwroot/wip-overlay.svg": {
        "fonts": [("Lexend", 900, False)],
        "texts": ["WORK IN PROGRESS"],
    },
    "host/KnockBox.CardCounter/wwwroot/tile.svg": {
        "fonts": [("Lexend", 900, False), ("Lexend", 700, False)],
        "texts": ["CARD COUNTER", "Bluff, count cards, bust the table."],
    },
    "host/KnockBox.Codeword/wwwroot/tile.svg": {
        "fonts": [("Share Tech Mono", 400, False)],
        "texts": [
            "> CODEWORD",
            "[INTEL]",
            "CLASSIFIED",
            "Decode the intel. Smoke out the spy.",
        ],
    },
    "host/KnockBox.DiceSimulator/wwwroot/tile.svg": {
        "fonts": [
            ("Lexend", 900, False),
            ("Lexend", 600, False),
            ("Google Sans Code", 800, False),
            ("Google Sans Code", 700, False),
        ],
        "texts": [
            "DICE SIMULATOR",
            "Roll any dice. Chase the nat-20.",
            "20",
            "3d20+5",
        ],
    },
    "host/KnockBox.DndMapper/wwwroot/tile.svg": {
        "fonts": [
            ("Cormorant Garamond", 700, True),
            ("Cormorant Garamond", 500, True),
            ("Lexend", 700, False),
            ("Lexend", 600, False),
        ],
        "texts": [
            "D",
            "nD Mapper",
            "Collaborative Tabletop Maps",
            "20",
        ],
    },
    "host/KnockBox.DrawnToDress/wwwroot/tile.svg": {
        "fonts": [("Lexend", 900, False), ("Lexend", 700, False)],
        "texts": ["DRAWN TO DRESS", "Sketch outfits. Vote the runway."],
    },
    "host/KnockBox.HiddenAgenda/wwwroot/tile.svg": {
        "fonts": [
            ("Playfair Display", 700, False),
            ("Playfair Display", 400, True),
        ],
        "texts": ["Hidden Agenda", "The Great Exhibition"],
    },
    "host/KnockBox.Operator/wwwroot/tile.svg": {
        "fonts": [
            ("Lexend", 900, False),
            ("Google Sans Code", 800, False),
            ("Google Sans Code", 700, False),
        ],
        "texts": [
            "OPERATOR",
            "Stack operators. Strike opponents.",
            "+",
            "+1",
            "7",
        ],
    },
    "host/KnockBox.Spardle/wwwroot/tile.svg": {
        "fonts": [
            ("Bowlby One SC", 400, False),
            ("JetBrains Mono", 700, False),
            ("Manrope", 600, False),
        ],
        "texts": [
            "SPARDLE–",
            "Head-to-head word combat.",
        ],
    },
    "host/KnockBox.TaskMaster/wwwroot/tile.svg": {
        # Lexend has no italic — tag uses regular Lexend with browser-synthesized italics.
        "fonts": [("Lexend", 700, False), ("Lexend", 400, False)],
        "texts": ["Task Master", "Complete tasks before time runs out."],
    },
}

BEGIN_MARK = "/* EMBEDDED FONTS BEGIN */"
END_MARK = "/* EMBEDDED FONTS END */"


def fetch_font_url(family: str, weight: int, italic: bool) -> str:
    """Returns the LATIN subset URL for the requested face.

    Google Fonts splits each face into per-script subsets (cyrillic, greek,
    vietnamese, latin-ext, latin) and emits one @font-face block per subset.
    We always want the `latin` one — that's the block whose unicode-range
    covers U+0000-00FF (basic Latin). Picking the first block by accident
    gave Vietnamese-only subsets that contained no A-Z, which was why most
    of the tile letters were rendering with system fallbacks.
    """
    italic_flag = "1" if italic else "0"
    family_url = family.replace(" ", "+")
    css_url = (
        f"https://fonts.googleapis.com/css2?family={family_url}"
        f":ital,wght@{italic_flag},{weight}&display=swap"
    )
    req = urllib.request.Request(css_url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req) as resp:
        css = resp.read().decode("utf-8")

    # Parse every @font-face block and find the one whose unicode-range
    # covers U+0000-00FF (basic Latin). That block is always the one we want.
    blocks = re.findall(
        r"src:\s*url\((https://fonts\.gstatic\.com/[^)]+)\)\s*format\(['\"]?woff2['\"]?\)\s*;\s*"
        r"unicode-range:\s*([^;}]+)",
        css,
    )
    for url, ranges in blocks:
        if "U+0000-00FF" in ranges:
            return url

    # Single-subset fonts (e.g. Share Tech Mono) have no `unicode-range`
    # selector — fall back to picking any URL.
    m = re.search(
        r"url\((https://fonts\.gstatic\.com/[^)]+)\)\s*format\(['\"]?woff2['\"]?\)",
        css,
    )
    if m:
        return m.group(1)

    sys.exit(
        f"Couldn't find a latin font URL for {family} weight={weight} italic={italic}.\n"
        f"CSS:\n{css}"
    )


def http_get(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req) as resp:
        return resp.read()


def subset_to_woff2(font_bytes: bytes, glyphs: str) -> bytes:
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        in_path = tmp / "in.font"
        out_path = tmp / "out.woff2"
        text_path = tmp / "text.txt"
        in_path.write_bytes(font_bytes)
        text_path.write_text(glyphs, encoding="utf-8")
        subprocess.run(
            [
                "pyftsubset",
                str(in_path),
                f"--text-file={text_path}",
                f"--output-file={out_path}",
                "--flavor=woff2",
                "--no-hinting",
                "--desubroutinize",
                "--layout-features=*",
            ],
            check=True,
        )
        return out_path.read_bytes()


def make_font_face_css(family: str, weight: int, italic: bool, woff2: bytes) -> str:
    b64 = base64.b64encode(woff2).decode("ascii")
    style = "italic" if italic else "normal"
    return (
        f"@font-face {{\n"
        f"    font-family: '{family}';\n"
        f"    font-style: {style};\n"
        f"    font-weight: {weight};\n"
        f"    src: url('data:font/woff2;base64,{b64}') format('woff2');\n"
        f"}}"
    )


def inject_into_svg(svg_path: Path, css_block: str) -> None:
    content = svg_path.read_text(encoding="utf-8")
    block = f"{BEGIN_MARK}\n{css_block}\n{END_MARK}"

    if BEGIN_MARK in content and END_MARK in content:
        pattern = re.escape(BEGIN_MARK) + r".*?" + re.escape(END_MARK)
        content = re.sub(pattern, block, content, flags=re.DOTALL)
    elif "<style>" in content:
        content = content.replace("<style>", "<style>\n" + block, 1)
    else:
        # No <style> element yet — inject one right after the opening <svg ...>.
        m = re.search(r"<svg\b[^>]*>", content)
        if not m:
            sys.exit(f"No <svg> tag in {svg_path}")
        insert_at = m.end()
        content = (
            content[:insert_at]
            + "\n    <style>\n"
            + block
            + "\n    </style>"
            + content[insert_at:]
        )

    svg_path.write_text(content, encoding="utf-8")


def main() -> None:
    cache: dict[tuple[str, int, bool], bytes] = {}

    for rel_path, conf in TILES.items():
        path = REPO_ROOT / rel_path
        if not path.exists():
            sys.exit(f"Missing SVG: {path}")

        # Include both case variants so subsets cover CSS text-transform:
        # uppercase / lowercase. Cheap (a few extra glyphs per face) and
        # avoids per-tile per-class scraping of the SVG.
        raw = "".join(conf["texts"])
        glyph_set = set(raw) | set(raw.upper()) | set(raw.lower())
        glyphs = "".join(sorted(glyph_set))
        print(f"\n{rel_path}")
        print(f"  glyphs: {sorted(glyph_set)!r}")

        css_parts = []
        for family, weight, italic in conf["fonts"]:
            key = (family, weight, italic)
            if key not in cache:
                print(f"  downloading {family} weight={weight} italic={italic}")
                url = fetch_font_url(family, weight, italic)
                cache[key] = http_get(url)
                print(f"    raw: {len(cache[key]):>7d} bytes")
            woff2 = subset_to_woff2(cache[key], glyphs)
            print(f"  subset {family} {weight}{' italic' if italic else ''}: {len(woff2):>5d} bytes")
            css_parts.append(make_font_face_css(family, weight, italic, woff2))

        inject_into_svg(path, "\n".join(css_parts))
        print(f"  injected {len(css_parts)} @font-face decl(s)")

    print("\nDone.")


if __name__ == "__main__":
    main()
