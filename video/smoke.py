# -*- coding: utf-8 -*-
"""冒烟测试：每个场景渲染一帧，检查无异常并便于肉眼检查。"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_video as mv

out = os.path.join(HERE, "smoke")
os.makedirs(out, exist_ok=True)
for name, dur, fn in mv.SCENES:
    img = fn(0.55)
    path = os.path.join(out, name + ".png")
    img.convert("RGB").save(path)
    print("ok", name, img.size)
print("done ->", out)
