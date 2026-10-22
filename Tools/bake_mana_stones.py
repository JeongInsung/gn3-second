"""다섯 빛깔 몬스터 마정석 시트(가로 5칸: 빨·파·초·보·주)를 96x96 아이콘으로 굽는다.

    python tools/bake_mana_stones.py [다섯 빛깔 몬스터 마나석.png]

값 순서(파·보·초·금·빨)로 loot_24 ~ loot_28에 저장한다(Economy/Loot.cs의 ManaColor 순서).
자르기·투명 처리는 bake_loot_icons.py와 같다.
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import DOWNLOADS, OUT_DIR, bake_cell  # noqa: E402

DEFAULT_SHEET = os.path.join(DOWNLOADS, "다섯 빛깔 몬스터 마나석.png")
FIRST_INDEX = 24
SHEET_ORDER = ["red", "blue", "green", "purple", "gold"]  # 그림의 왼쪽부터
VALUE_ORDER = ["blue", "purple", "green", "gold", "red"]  # 싼 것부터(저장 순서)


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SHEET
    arr = np.array(Image.open(path).convert("RGBA"))
    h, w = arr.shape[:2]
    cols = len(SHEET_ORDER)
    os.makedirs(OUT_DIR, exist_ok=True)
    for i, color in enumerate(VALUE_ORDER):
        c = SHEET_ORDER.index(color)
        x0, x1 = w * c // cols, w * (c + 1) // cols
        icon = bake_cell(arr[:, x0:x1])
        icon.save(os.path.join(OUT_DIR, f"loot_{FIRST_INDEX + i:02d}.png"))
    print(f"마정석 {cols}개 → loot_{FIRST_INDEX}~{FIRST_INDEX + cols - 1}")


if __name__ == "__main__":
    main()
