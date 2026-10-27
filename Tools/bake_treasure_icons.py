"""고블린 보물 아이콘(모든 고블린이 아주 드물게 떨어뜨리는 7종)을 96x96 아이콘으로 굽는다.

    python tools/bake_treasure_icons.py [여섯 가지 보물.png] [사파이어 펜던트.png]

- 6종 시트(3x2): loot_41 ~ loot_46 (에메랄드 원석 · 금니 · 은 성배 / 루비 브로치 · 옛 왕국 금화 · 진주)
- 펜던트 한 장: loot_47 (사파이어 펜던트)
순서는 Economy/Loot.cs의 고블린 보물 아이콘 번호와 같다. 자르기·투명 처리는 bake_loot_icons.py와 같다.
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import DOWNLOADS, OUT_DIR, bake_cell  # noqa: E402

DEFAULT_SHEET = os.path.join(DOWNLOADS, "고블린이 모은 여섯 가지 보물.png")
DEFAULT_PENDANT = os.path.join(DOWNLOADS, "고블린이 훔친 사파이어 펜던트.png")
FIRST_INDEX = 41


def main():
    sheet, pendant = (sys.argv[1], sys.argv[2]) if len(sys.argv) == 3 else (DEFAULT_SHEET, DEFAULT_PENDANT)
    os.makedirs(OUT_DIR, exist_ok=True)
    index = FIRST_INDEX
    arr = np.array(Image.open(sheet).convert("RGBA"))
    h, w = arr.shape[:2]
    for r in range(2):
        for c in range(3):
            cell = arr[h * r // 2:h * (r + 1) // 2, w * c // 3:w * (c + 1) // 3]
            bake_cell(cell).save(os.path.join(OUT_DIR, f"loot_{index:02d}.png"))
            index += 1
    bake_cell(np.array(Image.open(pendant).convert("RGBA"))).save(os.path.join(OUT_DIR, f"loot_{index:02d}.png"))
    print(f"고블린 보물 아이콘 {index + 1 - FIRST_INDEX}개 → loot_{FIRST_INDEX}~{index}")


if __name__ == "__main__":
    main()
