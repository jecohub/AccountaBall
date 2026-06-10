#!/usr/bin/env python3
"""Generate the AccountaBall demo deck as a PDF using reportlab (vector, no deps).

Usage:
  python3 make_deck.py            # build full deck -> AccountaBall-Demo.pdf
  python3 make_deck.py 5          # build only slide 5 -> /tmp/slide5.pdf (for QA)
"""
import sys
from reportlab.lib.units import inch
from reportlab.lib.colors import HexColor
from reportlab.pdfgen import canvas

W, H = 13.333 * inch, 7.5 * inch  # 16:9

ORANGE   = HexColor("#F5821F")
ORANGE_D = HexColor("#D96E10")
INK      = HexColor("#161412")
CHAR     = HexColor("#26221E")
CREAM    = HexColor("#FFF7EE")
MUTE     = HexColor("#8A8178")
LINE     = HexColor("#000000")

OUT = "AccountaBall-Demo.pdf"
L = 0.9 * inch


def basketball(c, cx, cy, r, face=None):
    c.saveState()
    c.setFillColor(ORANGE); c.setStrokeColor(ORANGE_D)
    c.setLineWidth(max(1.0, r / 28))
    c.circle(cx, cy, r, stroke=1, fill=1)
    c.setStrokeColor(LINE); c.setLineWidth(max(1.4, r / 22))
    c.line(cx - r, cy, cx + r, cy)
    c.line(cx, cy - r, cx, cy + r)
    p = c.beginPath()
    p.moveTo(cx - r * 0.55, cy + r * 0.84)
    p.curveTo(cx - r * 0.05, cy + r * 0.35, cx - r * 0.05, cy - r * 0.35, cx - r * 0.55, cy - r * 0.84)
    c.drawPath(p, stroke=1, fill=0)
    p = c.beginPath()
    p.moveTo(cx + r * 0.55, cy + r * 0.84)
    p.curveTo(cx + r * 0.05, cy + r * 0.35, cx + r * 0.05, cy - r * 0.35, cx + r * 0.55, cy - r * 0.84)
    c.drawPath(p, stroke=1, fill=0)
    if face:
        c.setFillColor(LINE)
        ey, ex, er = cy + r * 0.18, r * 0.30, r * 0.085
        c.circle(cx - ex, ey, er, stroke=0, fill=1)
        c.circle(cx + ex, ey, er, stroke=0, fill=1)
        c.setStrokeColor(LINE); c.setLineWidth(max(1.6, r / 20))
        m = c.beginPath()
        if face == "happy":
            m.moveTo(cx - r * 0.32, cy - r * 0.18)
            m.curveTo(cx - r * 0.10, cy - r * 0.52, cx + r * 0.10, cy - r * 0.52, cx + r * 0.32, cy - r * 0.18)
        else:
            m.moveTo(cx - r * 0.32, cy - r * 0.46)
            m.curveTo(cx - r * 0.10, cy - r * 0.14, cx + r * 0.10, cy - r * 0.14, cx + r * 0.32, cy - r * 0.46)
        c.drawPath(m, stroke=1, fill=0)
    c.restoreState()


def text(c, x, y, s, size, color=INK, font="Helvetica", align="l"):
    c.setFillColor(color); c.setFont(font, size)
    {"l": c.drawString, "c": c.drawCentredString, "r": c.drawRightString}[align](x, y, s)


def chip(c, x, y, label, size=13):
    w = c.stringWidth(label, "Helvetica-Bold", size) + 22
    h = size + 12
    c.setFillColor(ORANGE); c.roundRect(x, y, w, h, h / 2, stroke=0, fill=1)
    text(c, x + 11, y + 7, label, size, INK, "Helvetica-Bold")


def bg(c, color):
    c.setFillColor(color); c.rect(0, 0, W, H, stroke=0, fill=1)


def footer(c, n, dark=False):
    col = HexColor("#6A635B") if dark else MUTE
    text(c, W - 0.55 * inch, 0.4 * inch, f"{n}/7", 11, col, "Helvetica", "r")
    text(c, 0.55 * inch, 0.4 * inch, "AccountaBall", 11, col, "Helvetica-Bold")


def wrap(c, words, font, size, max_w):
    lines, cur = [], ""
    for w in words.split():
        t = (cur + " " + w).strip()
        if c.stringWidth(t, font, size) <= max_w:
            cur = t
        else:
            lines.append(cur); cur = w
    if cur:
        lines.append(cur)
    return lines


def slide1(c):
    bg(c, INK)
    c.setFillColor(ORANGE); c.rect(0, 0, 0.18 * inch, H, stroke=0, fill=1)
    basketball(c, W * 0.74, H * 0.52, 1.55 * inch, face="happy")
    text(c, L, H * 0.62, "AccountaBall", 64, CREAM, "Helvetica-Bold")
    c.setFillColor(ORANGE); c.rect(L, H * 0.595, 2.4 * inch, 5, stroke=0, fill=1)
    for i, ln in enumerate(["The accountability buddy that never gets", "tired of watching you work."]):
        text(c, L, H * 0.50 - i * 30, ln, 22, HexColor("#D9CFC4"), "Helvetica")
    text(c, L, H * 0.26, "A macOS floating widget · built with SwiftUI + on-device AI", 15, MUTE, "Helvetica")
    footer(c, 1, dark=True)


def slide2(c):
    bg(c, CREAM)
    chip(c, L, H - 1.25 * inch, "WHAT WE BUILT")
    basketball(c, W - 1.7 * inch, H * 0.52, 0.95 * inch, face="happy")
    y = H * 0.66
    for ln in ["A macOS floating", "basketball that watches", "your screen and keeps",
               "you honest about your", "declared tasks."]:
        text(c, L, y, ln, 40, INK, "Helvetica-Bold"); y -= 46
    text(c, L, 1.15 * inch, "…and calls you out the moment you drift off.", 18, ORANGE_D, "Helvetica-Bold")
    footer(c, 2)


def slide3(c):
    bg(c, INK)
    chip(c, L, H - 1.25 * inch, "THE PROBLEM")
    text(c, L, H - 1.9 * inch, "You lose hours to distraction every day.", 30, CREAM, "Helvetica-Bold")
    rows = [("The drift", "one notification, and 30 focused minutes are gone."),
            ("The fade", "willpower runs out long before your task list does."),
            ("The blind spot", "you don't notice you've strayed until the day is gone.")]
    y = H - 2.7 * inch
    for head, rest in rows:
        c.setFillColor(ORANGE); c.circle(L + 7, y + 6, 6, stroke=0, fill=1)
        text(c, L + 30, y, head, 20, ORANGE, "Helvetica-Bold")
        text(c, L + 30 + c.stringWidth(head, "Helvetica-Bold", 20) + 10, y, "— " + rest, 20, HexColor("#D9CFC4"), "Helvetica")
        y -= 0.72 * inch
    text(c, L, 1.15 * inch, "The cost: missed deadlines, 10-hour days for 5 hours of real work.",
         17, MUTE, "Helvetica-Oblique")
    footer(c, 3, dark=True)


def slide4(c):
    bg(c, CREAM)
    chip(c, L, H - 1.25 * inch, "THE SOLUTION")
    text(c, L, H - 1.95 * inch, "Active, AI-driven accountability.", 32, INK, "Helvetica-Bold")
    cards = [("Sees what you do", "Captures the screen every ~5s and reads it on-device (OCR)."),
             ("Knows your tasks", "An AI infers which declared task you're on — or that you've strayed."),
             ("Intervenes live", "Drifts off? The ball creeps in, demands an excuse, and judges it.")]
    cw = (W - 2 * L - 2 * 0.4 * inch) / 3
    for i, (h, b) in enumerate(cards):
        x = L + i * (cw + 0.4 * inch)
        c.setFillColor(HexColor("#FFFFFF")); c.roundRect(x, 1.5 * inch, cw, 3.4 * inch, 16, stroke=0, fill=1)
        c.setFillColor(ORANGE); c.roundRect(x, 4.72 * inch, cw, 0.18 * inch, 8, stroke=0, fill=1)
        basketball(c, x + cw / 2, 4.15 * inch, 0.5 * inch, face="happy" if i < 2 else "angry")
        text(c, x + cw / 2, 3.35 * inch, h, 18, INK, "Helvetica-Bold", "c")
        for j, ln in enumerate(wrap(c, b, "Helvetica", 13, cw - 36)):
            text(c, x + cw / 2, 2.95 * inch - j * 18, ln, 13, CHAR, "Helvetica", "c")
    text(c, L, 1.0 * inch, "Fully local — only a small text snippet ever leaves your Mac.", 15, ORANGE_D, "Helvetica-Bold")
    footer(c, 4)


def slide5(c):
    bg(c, INK)
    chip(c, L, H - 1.2 * inch, "SHOW IT IN ACTION")
    steps = [("1", "Declare", "Type 1–5 tasks + context, hit “Let's go!”"),
             ("2", "Minimize", "Ball shrinks to the screen edge with live timers."),
             ("3", "Drift off", "Open Twitter → it creeps in, judgemental: “What are you doing?”"),
             ("4", "Be judged", "Real reason → approved: “Carry on.”   Weak one → “Get back to it.”"),
             ("5", "Win", "Finish all tasks → 3-point shot, confetti, session summary.")]
    y = H - 1.95 * inch
    for num, head, body in steps:
        c.setFillColor(ORANGE); c.circle(L + 16, y + 5, 17, stroke=0, fill=1)
        text(c, L + 16, y - 2, num, 18, INK, "Helvetica-Bold", "c")
        text(c, L + 48, y, head, 21, CREAM, "Helvetica-Bold")
        text(c, L + 48 + 1.7 * inch, y, body, 16, HexColor("#D9CFC4"), "Helvetica")
        y -= 0.86 * inch
    footer(c, 5, dark=True)


def slide6(c):
    bg(c, CREAM)
    chip(c, L, H - 1.25 * inch, "UNDER THE HOOD")
    text(c, L, H - 1.95 * inch, "Native, local, and fast.", 32, INK, "Helvetica-Bold")
    items = [("Interface", "SwiftUI + a borderless always-on-top NSPanel"),
             ("Capture", "ScreenCaptureKit → Vision.framework OCR (offline)"),
             ("Intelligence", "Claude via API today; on-device Ollama next"),
             ("Storage", "100% local — UserDefaults + JSON, no servers")]
    y = H - 2.7 * inch
    for h, b in items:
        c.setFillColor(ORANGE); c.rect(L, y - 2, 5, 22, stroke=0, fill=1)
        text(c, L + 18, y, h, 19, INK, "Helvetica-Bold")
        text(c, L + 2.0 * inch, y, b, 18, CHAR, "Helvetica")
        y -= 0.66 * inch
    basketball(c, W - 1.8 * inch, H * 0.45, 1.05 * inch, face="happy")
    text(c, L, 1.0 * inch, "Privacy by design: your screen never leaves the device.", 15, ORANGE_D, "Helvetica-Bold")
    footer(c, 6)


def slide7(c):
    bg(c, INK)
    basketball(c, W / 2, H * 0.62, 1.4 * inch, face="happy")
    text(c, W / 2, H * 0.36, "AccountaBall", 48, CREAM, "Helvetica-Bold", "c")
    text(c, W / 2, H * 0.28, "The accountability buddy that never gets tired of watching you work.",
         19, ORANGE, "Helvetica-Bold", "c")
    text(c, W / 2, H * 0.18, "Stay on task. Sink the shot.", 15, MUTE, "Helvetica-Oblique", "c")
    footer(c, 7, dark=True)


SLIDES = [slide1, slide2, slide3, slide4, slide5, slide6, slide7]

if __name__ == "__main__":
    if len(sys.argv) > 1:
        n = int(sys.argv[1])
        out = f"/tmp/slide{n}.pdf"
        c = canvas.Canvas(out, pagesize=(W, H))
        SLIDES[n - 1](c); c.showPage(); c.save()
        print("wrote", out)
    else:
        c = canvas.Canvas(OUT, pagesize=(W, H))
        for fn in SLIDES:
            fn(c); c.showPage()
        c.save()
        print("wrote", OUT)
