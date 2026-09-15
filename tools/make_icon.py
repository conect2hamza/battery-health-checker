#!/usr/bin/env python3
"""Generate src/BatteryHealthChecker/app.ico.

The icon is committed as a binary asset because it is compiled *into* the
single-file EXE (SRS 1: no external assets). This script exists so the asset
stays reproducible. Requires only the Python standard library.

    python3 tools/make_icon.py
"""
import math
import os
import struct
import zlib

SIZES = [256, 128, 64, 48, 32, 16]

BG_TOP = (14, 20, 33)
BG_BOTTOM = (23, 34, 54)
SHELL = (226, 232, 240)
FILL_TOP = (52, 211, 153)
FILL_BOTTOM = (16, 163, 127)


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def rounded_rect_coverage(px, py, x0, y0, x1, y1, r):
    """Signed coverage of a rounded rectangle at a point, 0..1, antialiased."""
    # distance from the rounded-rect boundary (negative = inside)
    dx = max(x0 + r - px, 0.0, px - (x1 - r))
    dy = max(y0 + r - py, 0.0, py - (y1 - r))
    if dx > 0 and dy > 0:
        dist = math.hypot(dx, dy) - r
    else:
        dist = max(x0 - px, px - x1, y0 - py, py - y1)
        if dist < 0:
            # inside the straight-edge region; distance to nearest edge
            dist = max(x0 - px, px - x1, y0 - py, py - y1)
    return min(max(0.5 - dist, 0.0), 1.0)


def blend(dst, src, alpha):
    return tuple(round(dst[i] + (src[i] - dst[i]) * alpha) for i in range(3))


def render(size):
    s = size / 256.0
    ss = 3  # supersampling factor for the battery shapes
    px = bytearray()

    # Geometry in 256-space.
    bx0, by0, bx1, by1 = 34.0, 78.0, 196.0, 178.0   # battery shell
    br = 22.0
    wall = 13.0
    nub_x0, nub_y0, nub_x1, nub_y1 = 196.0, 104.0, 222.0, 152.0
    nub_r = 9.0
    ix0, iy0 = bx0 + wall, by0 + wall
    ix1, iy1 = bx1 - wall, by1 - wall
    ir = 9.0
    fill_ratio = 0.72
    fx1 = ix0 + (ix1 - ix0) * fill_ratio

    for y in range(size):
        row = bytearray()
        for x in range(size):
            t = (x + y) / (2.0 * max(size - 1, 1))
            col = lerp(BG_TOP, BG_BOTTOM, t)

            shell_a = 0.0
            fill_a = 0.0
            for sy in range(ss):
                for sx in range(ss):
                    ux = (x + (sx + 0.5) / ss) / s
                    uy = (y + (sy + 0.5) / ss) / s
                    outer = max(
                        rounded_rect_coverage(ux, uy, bx0, by0, bx1, by1, br),
                        rounded_rect_coverage(ux, uy, nub_x0, nub_y0, nub_x1, nub_y1, nub_r),
                    )
                    inner = rounded_rect_coverage(ux, uy, ix0, iy0, ix1, iy1, ir)
                    shell_a += max(outer - inner, 0.0)
                    fill_a += min(
                        inner,
                        rounded_rect_coverage(ux, uy, ix0, iy0, fx1, iy1, ir),
                    )
            n = float(ss * ss)
            shell_a /= n
            fill_a /= n

            fill_col = lerp(FILL_TOP, FILL_BOTTOM, y / max(size - 1, 1))
            col = blend(col, fill_col, fill_a)
            col = blend(col, SHELL, shell_a)
            row += bytes(col) + b"\xff"
        px += b"\x00" + row
    return bytes(px)


def png(size, raw):
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def main():
    images = [(n, png(n, render(n))) for n in SIZES]
    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for n, data in images:
        entries += struct.pack(
            "<BBBBHHII", n if n < 256 else 0, n if n < 256 else 0, 0, 0, 1, 32, len(data), offset
        )
        blobs += data
        offset += len(data)
    dest = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "src", "BatteryHealthChecker", "app.ico",
    )
    with open(dest, "wb") as fh:
        fh.write(out + entries + blobs)
    print(f"wrote {dest} ({len(out + entries + blobs)} bytes)")


if __name__ == "__main__":
    main()
