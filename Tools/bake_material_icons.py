"""건설 재료 아이콘 시트 두 장을 칸별 96x96 아이콘으로 굽는다.

    python tools/bake_material_icons.py [재료 4종.png] [재료 8종.png]

- 4종 시트(2x2): loot_29 ~ loot_32 (천 · 통나무 · 밧줄 · 무두질한 가죽)
- 8종 시트(4x2): loot_33 ~ loot_40 (쇠못 · 쇠고리 · 나무 말뚝 · 실타래 / 바늘 · 가죽끈 · 밀랍 · 푸른 염료)
순서는 Economy/Loot.cs의 건설 재료 아이콘 번호와 같다. 자르기·투명 처리는 bake_loot_icons.py와 같다.
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import OUT_DIR, bake_cell  # noqa: E402

SOURCE_DIR = os.path.join(os.path.expanduser("~"), "OneDrive", "바탕 화면", "개발", "건설", "재료")
DEFAULT_SHEETS = [
    (os.path.join(SOURCE_DIR, "중세 텐트 제작 재료 4종 아이콘.png"), 2, 2),
    (os.path.join(SOURCE_DIR, "중세 판타지 텐트 제작 재료 아이콘 8종.png"), 4, 2),
]
FIRST_INDEX = 29


def main():
    sheets = DEFAULT_SHEETS
    if len(sys.argv) == 3:
        sheets = [(sys.argv[1], 2, 2), (sys.argv[2], 4, 2)]
    os.makedirs(OUT_DIR, exist_ok=True)
    index = FIRST_INDEX
    for path, cols, rows in sheets:
        arr = np.array(Image.open(path).convert("RGBA"))
        h, w = arr.shape[:2]
        for r in range(rows):
            for c in range(cols):
                y0, y1 = h * r // rows, h * (r + 1) // rows
                x0, x1 = w * c // cols, w * (c + 1) // cols
                bake_cell(arr[y0:y1, x0:x1]).save(os.path.join(OUT_DIR, f"loot_{index:02d}.png"))
                index += 1
    print(f"재료 아이콘 {index - FIRST_INDEX}개 → loot_{FIRST_INDEX}~{index - 1}")


if __name__ == "__main__":
    main()
