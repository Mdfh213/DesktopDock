# -*- coding: utf-8 -*-
"""
DesktopDock 介绍视频生成器
逐帧绘制 1920x1080 画面，再用 ffmpeg 编码成 MP4。
所有图形均由代码合成（不使用真实桌面截图，避免暴露个人图标）。
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFont, ImageFilter

W, H = 1920, 1080
FPS = 30
FRAMES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "frames")

FONT_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_REG = "C:/Windows/Fonts/msyh.ttc"

# 配色
BG_TOP = (12, 15, 21)
BG_BOTTOM = (20, 24, 34)
PANEL_BG = (26, 30, 40)
PANEL_EDGE = (52, 58, 72)
ACCENT = (109, 94, 248)
ACCENT_SOFT = (139, 126, 255)
TEXT = (232, 235, 240)
MUTED = (150, 158, 172)
GREEN = (52, 199, 123)

# 面板布局（与程序一致的比例）
PANEL_W = 400
CELL = 100
GAP = 16
PAD = 24
PITCH = CELL + 42          # 图标 + 文字行高
TOP = 132

_font_cache = {}
_TILE_CACHE = {}
_BG_CACHE = None
_DESK_CACHE = None


def font(size, bold=False):
    key = (size, bold)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)
    return _font_cache[key]


def ease_out(p):
    return 1 - (1 - p) ** 3


def ease_in_out(p):
    return p * p * (3 - 2 * p)


def clamp01(v):
    return max(0.0, min(1.0, v))


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    return tuple(int(round(lerp(c1[i], c2[i], t))) for i in range(3))


def rounded(draw, box, r, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def text_center(draw, xy, s, f, fill):
    draw.text(xy, s, font=f, fill=fill, anchor="mm")


def text_left(draw, xy, s, f, fill):
    draw.text(xy, s, font=f, fill=fill, anchor="lm")


def shadowed_text(img, xy, s, f, fill, blur=10, offset=(0, 4), shadow=(0, 0, 0, 160)):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text((xy[0] + offset[0], xy[1] + offset[1]), s, font=f, fill=shadow, anchor="mm")
    layer = layer.filter(ImageFilter.GaussianBlur(blur))
    img.alpha_composite(layer)
    ImageDraw.Draw(img).text(xy, s, font=f, fill=fill, anchor="mm")


def gradient_bg():
    global _BG_CACHE
    if _BG_CACHE is not None:
        return _BG_CACHE.copy()
    img = Image.new("RGB", (W, H), BG_TOP)
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        d.line([(0, y), (W, y)], fill=mix(BG_TOP, BG_BOTTOM, t))
    # 光晕
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([-300, -500, 900, 600], fill=(109, 94, 248, 42))
    gd.ellipse([W - 700, H - 500, W + 300, H + 300], fill=(52, 199, 123, 26))
    glow = glow.filter(ImageFilter.GaussianBlur(120))
    img = img.convert("RGBA")
    img.alpha_composite(glow)
    return img


# ---------------- 图标与卡片 ----------------

APPS = [
    ("E", (47, 107, 255)), ("D", (34, 197, 94)), ("L", (249, 168, 37)),
    ("M", (236, 72, 153)), ("A", (14, 165, 233)), ("S", (139, 92, 246)),
    ("T", (16, 185, 129)), ("V", (59, 130, 246)), ("W", (244, 63, 94)),
    ("C", (234, 179, 8)), ("Q", (6, 182, 212)), ("N", (168, 85, 247)),
    ("P", (248, 113, 113)), ("R", (34, 211, 238)), ("F", (251, 146, 60)),
    ("G", (74, 222, 128)), ("H", (96, 165, 250)), ("J", (192, 132, 252)),
]
NAMES = ["Edge", "DeepSeek", "Launcher", "Magpie", "Hub", "Steam",
         "Typora", "VSCode", "Wallpaper", "Capture", "Quick", "NVIDIA",
         "Player", "Reader", "Firewall", "Games", "Helper", "Editor"]


def app_tile(size, idx, label=None, alpha=255):
    key = (size, idx, alpha)
    if key in _TILE_CACHE:
        return _TILE_CACHE[key].copy()
    """仿应用图标：圆角方块 + 字母"""
    s = size
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    letter, color = APPS[idx % len(APPS)]
    r = int(s * 0.24)
    # 渐变底座
    for i in range(s):
        t = i / max(1, s - 1)
        d.line([(0, i), (s, i)], fill=mix(color, mix(color, (255, 255, 255), 0.35), t))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=255)
    base = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    base.paste(img, (0, 0), mask)
    d2 = ImageDraw.Draw(base)
    f = font(int(s * 0.46), True)
    d2.text((s / 2, s / 2 + s * 0.02), letter, font=f, fill=(255, 255, 255, 245), anchor="mm")
    if alpha < 255:
        a = base.getchannel("A").point(lambda v: int(v * alpha / 255))
        base.putalpha(a)
    return base


def group_tile(size, members, alpha=255):
    """分组格子：2x2 迷你预览"""
    s = size
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rounded(d, [0, 0, s - 1, s - 1], int(s * 0.24), fill=(38, 43, 56, 255), outline=(66, 73, 90, 255), width=2)
    mini = int(s * 0.36)
    pad = int(s * 0.12)
    for i in range(4):
        cx = pad + (i % 2) * (mini + int(s * 0.04))
        cy = pad + (i // 2) * (mini + int(s * 0.04))
        if i < len(members):
            img.alpha_composite(app_tile(mini, members[i]), (cx, cy))
        else:
            d.rounded_rectangle([cx, cy, cx + mini, cy + mini], radius=int(mini * 0.24), fill=(30, 34, 44, 255))
    return img


def draw_panel(base, x, cells, cols=3, cell=CELL, gap=GAP, pad=PAD, top=TOP, alpha=255, height=None):
    """画整块侧边栏（含标题栏、格子、翻页按钮）"""
    pw = PANEL_W
    ph = H if height is None else height
    layer = Image.new("RGBA", (W, ph), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # 主体
    rounded(d, [x, 0, x + pw, ph], 0, fill=(PANEL_BG[0], PANEL_BG[1], PANEL_BG[2], 248))
    # 右侧高光边
    d.line([(x + pw - 1, 0), (x + pw - 1, ph)], fill=(70, 78, 96, 200), width=2)
    # 标题
    text_left(d, (x + pad + 4, 62), "DesktopDock", font(30, True), TEXT)
    text_left(d, (x + pad + 4, 100), "双击图标打开 · 拖拽调整", font(19), MUTED)
    # 格子
    for i, c in enumerate(cells):
        col = i % cols
        row = i // cols
        cx = int(x + pad + col * (cell + gap))
        cy = int(top + row * PITCH)
        if cy > H + cell:
            continue
        if c[0] == "app":
            layer.alpha_composite(app_tile(cell, c[1], alpha=alpha), (cx, cy))
        else:
            layer.alpha_composite(group_tile(cell, c[1], alpha=alpha), (cx, cy))
        name = c[2] if len(c) > 2 else NAMES[c[1] % len(NAMES)]
        f = font(16)
        d.text((cx + cell / 2, cy + cell + 16), name, font=f, fill=(212, 217, 226, 255), anchor="mm")
    if alpha < 255:
        a = layer.getchannel("A").point(lambda v: int(v * alpha / 255))
        layer.putalpha(a)
    base.alpha_composite(layer)


def caption(base, text, t_alpha=1.0):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    f = font(34, True)
    tw = d.textlength(text, font=f)
    bw, bh = tw + 84, 78
    bx = (W - bw) / 2
    by = H - 132
    rounded(d, [bx, by, bx + bw, by + bh], 39, fill=(16, 19, 27, 225), outline=(70, 78, 96, 200), width=2)
    d.text((W / 2, by + bh / 2), text, font=f, fill=TEXT, anchor="mm")
    if t_alpha < 1:
        a = layer.getchannel("A").point(lambda v: int(v * t_alpha))
        layer.putalpha(a)
    base.alpha_composite(layer)


def fake_desktop(base, seed=7):
    global _DESK_CACHE
    if _DESK_CACHE is not None:
        base.alpha_composite(_DESK_CACHE)
        return
    """背景桌面：散落的图标"""
    rnd = random.Random(seed)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    placed = []
    for i in range(22):
        for _ in range(80):
            x = rnd.randint(180, W - 200)
            y = rnd.randint(120, H - 200)
            if all(abs(x - p[0]) > 150 or abs(y - p[1]) > 150 for p in placed):
                placed.append((x, y))
                break
        else:
            continue
        size = rnd.choice([72, 78, 84])
        layer.alpha_composite(app_tile(size, rnd.randint(0, len(APPS) - 1)), (x, y))
    base.alpha_composite(layer)


def cursor(base, x, y, pressed=False):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    pts = [(x, y), (x, y + 34), (x + 9, y + 26), (x + 15, y + 40), (x + 22, y + 36), (x + 16, y + 23), (x + 27, y + 22)]
    d.polygon(pts, fill=(255, 255, 255, 255), outline=(20, 20, 20, 255))
    if pressed:
        d.ellipse([x - 22, y - 22, x + 22, y + 22], outline=(109, 94, 248, 220), width=4)
    base.alpha_composite(layer)


# ---------------- 场景 ----------------

def scene_title(p):
    img = gradient_bg()
    d = ImageDraw.Draw(img)
    # 背景浮起的图标
    for i in range(10):
        ang = i * 36 + p * 20
        cx = W / 2 + math.cos(math.radians(ang)) * 620
        cy = H / 2 + math.sin(math.radians(ang)) * 330
        a = int(60 + 40 * math.sin(p * 6 + i))
        t = app_tile(86, i, alpha=max(0, min(255, a)))
        img.alpha_composite(t, (int(cx) - 43, int(cy) - 43))

    a = clamp01(p * 3) * clamp01((1 - p) * 4)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.text((W / 2, H / 2 - 70), "DesktopDock", font=font(132, True), fill=TEXT + (int(255 * a),), anchor="mm")
    ld.text((W / 2, H / 2 + 40), "Windows 边缘抽屉启动器", font=font(46), fill=MUTED + (int(255 * a),), anchor="mm")
    ld.text((W / 2, H / 2 + 120), "单文件 70 KB  ·  零依赖  ·  免安装", font=font(30), fill=ACCENT_SOFT + (int(235 * a),), anchor="mm")
    img.alpha_composite(layer)
    return img


def scene_slide(p):
    img = gradient_bg()
    fake_desktop(img)
    # 面板从左侧滑入
    slide = ease_out(clamp01(p / 0.55)) * 340
    x = -340 + slide
    cells = [("app", i, NAMES[i]) for i in range(15)]
    draw_panel(img, x, cells)
    # 光标从左边缘进来
    cxp = lerp(-40, 120, ease_out(clamp01(p / 0.5)))
    cursor(img, cxp, H / 2 + 200)
    if p > 0.5:
        caption(img, "鼠标贴边 → 侧边栏滑出")
    return img


def scene_click(p):
    img = gradient_bg()
    fake_desktop(img)
    cells = [("app", i, NAMES[i]) for i in range(15)]
    draw_panel(img, 0, cells)
    pad, cell, gap = 22, 104, 16
    tx, ty = pad + cell // 2, 120 + cell // 2
    down = 0.25 < p < 0.45
    cursor(img, tx - 18, ty - 14, pressed=down)
    # 点击涟漪
    if 0.3 < p < 0.8:
        r = (p - 0.3) / 0.5
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        rr = int(30 + r * 90)
        d.ellipse([tx - rr, ty - rr, tx + rr, ty + rr], outline=ACCENT + (int(220 * (1 - r)),), width=5)
        img.alpha_composite(layer)
    # 启动提示
    if p > 0.6:
        a = clamp01((p - 0.6) / 0.2)
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        f = font(26, True)
        s = "已启动：DeepSeek"
        tw = d.textlength(s, font=f)
        bx, by = 40, 40
        rounded(d, [bx, by, bx + tw + 56, by + 62], 18, fill=(22, 26, 34, int(235 * a)), outline=GREEN + (int(220 * a),), width=2)
        d.text((bx + 28, by + 31), s, font=f, fill=TEXT + (int(255 * a),), anchor="lm")
        img.alpha_composite(layer)
    caption(img, "双击图标 → 启动程序")
    return img


def scene_group(p):
    img = gradient_bg()
    fake_desktop(img)
    cells = [("app", i, NAMES[i]) for i in range(15)]
    draw_panel(img, 0, cells)
    pad, cell, gap = 22, 104, 16

    def cell_pos(i):
        return pad + (i % 3) * (cell + gap), 120 + (i // 3) * (cell + gap)

    ax, ay = cell_pos(0)
    bx, by = cell_pos(1)
    # 阶段：0-0.35 拖拽；0.35-0.75 长按高亮；0.75+ 合并
    if p < 0.7:
        t = clamp01(p / 0.7)
        mx = lerp(ax + cell / 2, bx + cell / 2, ease_in_out(t))
        my = lerp(ay + cell / 2, by + cell / 2, ease_in_out(t))
        # 拖拽中的图标
        ghost = app_tile(cell, 0, alpha=200)
        img.alpha_composite(ghost, (int(mx - cell / 2), int(my - cell / 2)))
        cursor(img, int(mx - 20), int(my - 14))
        if p > 0.42:
            layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            d = ImageDraw.Draw(layer)
            d.rounded_rectangle([bx - 8, by - 8, bx + cell + 8, by + cell + 8], radius=int(cell * 0.28),
                                outline=ACCENT + (235,), width=6)
            img.alpha_composite(layer)

        caption(img, "拖到另一个图标上，长按合并")
    else:
        t = clamp01((p - 0.7) / 0.3)
        # 两个格子合并成一个分组格子
        new_cells = [("group", [0, 1], "分组 1")] + [("app", i, NAMES[i]) for i in range(2, 15)]
        draw_panel(img, 0, new_cells)
        # 合并高亮
        gx, gy = cell_pos(0)
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        d.rounded_rectangle([gx - 10, gy - 10, gx + cell + 10, gy + cell + 10], radius=int(cell * 0.3),
                            outline=GREEN + (int(200 * (1 - t)),), width=6)
        img.alpha_composite(layer)
        caption(img, "合并为「分组」格子")
    return img


def scene_scroll(p):
    img = gradient_bg()
    fake_desktop(img)
    cells = [("app", i, NAMES[i]) for i in range(18)]
    offset = -int(ease_in_out(clamp01(p / 0.8)) * 300)

    body = Image.new("RGBA", (W, H + 340), (0, 0, 0, 0))
    draw_panel(body, 0, cells, height=H + 340)
    visible = body.crop((0, -offset, W, -offset + H))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    layer.alpha_composite(visible)
    # 头部固定：遮住滚上来的格子，重画标题
    hd = ImageDraw.Draw(layer)
    hd.rectangle([0, 0, PANEL_W, TOP - 8], fill=(PANEL_BG[0], PANEL_BG[1], PANEL_BG[2], 255))
    hd.text((PAD + 4, 62), "DesktopDock", font=font(30, True), fill=TEXT, anchor="lm")
    hd.text((PAD + 4, 100), "双击图标打开 · 拖拽调整", font=font(19), fill=MUTED, anchor="lm")
    img.alpha_composite(layer)

    d = ImageDraw.Draw(img)
    # 翻页按钮贴在面板右上/右下角
    for cy, glyph in [(44, "▲"), (H - 44, "▼")]:
        rounded(d, [PANEL_W - 52, cy - 18, PANEL_W - 16, cy + 18], 10,
                fill=(40, 45, 58, 240), outline=(80, 88, 105, 220), width=2)
        d.text((PANEL_W - 34, cy), glyph, font=font(18), fill=TEXT, anchor="mm")
    # 滚动条贴在面板右缘
    bar_h = 240
    bar_y = TOP + (H - TOP - 80 - bar_h) * clamp01(p / 0.8)
    rounded(d, [PANEL_W - 10, bar_y, PANEL_W - 4, bar_y + bar_h], 3, fill=(150, 158, 175, 200))
    caption(img, "滚轮 / 贴边自动滚动 / ▲▼ 翻页")
    return img

def scene_fullscreen(p):
    img = gradient_bg()
    fake_desktop(img)
    cells = [("app", i, NAMES[i]) for i in range(15)]
    # 面板先显示，然后被全屏窗口遮住并滑出
    if p < 0.35:
        draw_panel(img, 0, cells)
    else:
        slide = ease_in_out(clamp01((p - 0.35) / 0.35))
        draw_panel(img, int(-PANEL_W * slide), cells, alpha=int(255 * (1 - slide)))
    # 全屏应用
    if p > 0.3:
        a = clamp01((p - 0.3) / 0.3)
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        d.rectangle([0, 0, W, H], fill=(8, 10, 14, int(245 * a)))
        text_center(d, (W / 2, H / 2 - 40), "全屏应用运行中", font(64, True), TEXT + (int(255 * a),))
        text_center(d, (W / 2, H / 2 + 50), "侧边栏已自动隐藏", font(34), MUTED + (int(255 * a),))
        img.alpha_composite(layer)
    caption(img, "检测到全屏程序 → 自动隐藏")
    return img


def scene_screenshot(p):
    img = gradient_bg()
    fake_desktop(img)
    cells = [("app", i, NAMES[i]) for i in range(15)]
    draw_panel(img, 0, cells)
    # 截图选框
    x0, y0 = lerp(60, 520, ease_in_out(clamp01(p / 0.6))), lerp(240, 300, ease_in_out(clamp01(p / 0.6)))
    x1, y1 = lerp(70, 1180, ease_in_out(clamp01(p / 0.6))), lerp(250, 820, ease_in_out(clamp01(p / 0.6)))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rectangle([x0, y0, x1, y1], outline=ACCENT + (255,), width=4)
    for (cx, cy) in [(x0, y0), (x1, y0), (x0, y1), (x1, y1)]:
        d.rectangle([cx - 7, cy - 7, cx + 7, cy + 7], fill=ACCENT + (255,))
    img.alpha_composite(layer)
    caption(img, "截图时侧边栏保持展开，不会被挡住")
    return img


def scene_end(p):
    img = gradient_bg()
    d = ImageDraw.Draw(img)
    a = clamp01(p * 2.5)
    ld = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dd = ImageDraw.Draw(ld)
    dd.text((W / 2, 300), "桌面，干净一点", font=font(96, True), fill=TEXT + (int(255 * a),), anchor="mm")
    dd.text((W / 2, 400), "DesktopDock v1.4.3", font=font(40), fill=ACCENT_SOFT + (int(240 * a),), anchor="mm")

    # 特性清单
    feats = ["边缘唤出", "双击启动", "手机式分组", "拖拽排序", "可调图标大小", "全屏自动隐藏"]
    f = font(30)
    y = 520
    for i, s in enumerate(feats):
        aa = clamp01((p - 0.1) * 2 - i * 0.08)
        if aa <= 0:
            continue
        cx = W / 2 - 520 + (i % 3) * 360
        cy = y + (i // 3) * 90
        dd.ellipse([cx - 10, cy - 10, cx + 10, cy + 10], fill=GREEN + (int(230 * aa),))
        dd.text((cx + 26, cy), s, font=f, fill=TEXT + (int(255 * aa),), anchor="lm")

    # 仓库信息
    aa = clamp01((p - 0.45) * 2)
    dd.text((W / 2, 800), "gitee.com/Dusk_Luminance/DesktopDock", font=font(30), fill=MUTED + (int(255 * aa),), anchor="mm")
    dd.text((W / 2, 850), "github.com/Mdfh213/DesktopDock", font=font(30), fill=MUTED + (int(255 * aa),), anchor="mm")
    dd.text((W / 2, 960), "由 DeepSeek Harness 生成", font=font(26), fill=(165, 172, 188, int(240 * aa)), anchor="mm")
    img.alpha_composite(ld)
    return img


SCENES = [
    ("title", 3.2, scene_title),
    ("slide", 4.2, scene_slide),
    ("click", 4.0, scene_click),
    ("group", 5.0, scene_group),
    ("scroll", 3.6, scene_scroll),
    ("fullscreen", 3.8, scene_fullscreen),
    ("screenshot", 3.4, scene_screenshot),
    ("end", 4.6, scene_end),
]


def main():
    os.makedirs(FRAMES, exist_ok=True)
    for f in os.listdir(FRAMES):
        if f.endswith(".png"):
            os.remove(os.path.join(FRAMES, f))

    total = sum(d for _, d, _ in SCENES)
    n_frames = int(total * FPS)
    print("总时长 %.1fs, 帧数 %d" % (total, n_frames))

    idx = 0
    for name, dur, fn in SCENES:
        frames = int(dur * FPS)
        for i in range(frames):
            p = i / max(1, frames - 1)
            img = fn(p)
            img.convert("RGB").save(os.path.join(FRAMES, "f%05d.png" % idx), compress_level=1)
            idx += 1
        print("  %-11s %3d 帧" % (name, frames))

    print("完成: %d 帧 -> %s" % (idx, FRAMES))


if __name__ == "__main__":
    main()
