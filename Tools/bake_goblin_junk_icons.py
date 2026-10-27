"""고블린 잡동사니 아이콘(모든 고블린이 흔하게 떨어뜨리는 36종)을 96x96 아이콘으로 굽는다.

    python tools/bake_goblin_junk_icons.py [그림 폴더]

폴더(기본: 바탕 화면/개발/전리품/고블린)의 시트 6장을 파일 이름 끝 번호(-1 ~ -6) 순서로, 각 3x2 칸을 왼쪽 위부터
loot_48 ~ loot_83으로 굽는다(고철 · 낡은 잡동사니 · 식량 · 소박한 잡동사니 · 재료 · 폐품 장비).
순서는 Economy/Loot.cs의 고블린 잡동사니 아이콘 번호와 같다. 자르기·투명 처리는 bake_loot_icons.py와 같다.
"""
import os
import re
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import OUT_DIR, bake_cell  # noqa: E402

DEFAULT_DIR = os.path.join(os.path.expanduser("~"), "OneDrive", "바탕 화면", "개발", "전리품", "고블린")
FIRST_INDEX = 48
SHEET_COUNT = 6


def sheets(folder):
    """이름 끝이 -1 ~ -6인 시트를 번호 순서로."""
    numbered = {}
    for name in os.listdir(folder):
        match = re.search(r"-(\d+)\.png$", name)
        if match:
            numbered[int(match.group(1))] = os.path.join(folder, name)
    missing = [n for n in range(1, SHEET_COUNT + 1) if n not in numbered]
    if missing:
        sys.exit(f"시트가 모자랍니다: -{missing}")
    return [numbered[n] for n in range(1, SHEET_COUNT + 1)]


def main():
    folder = sys.argv[1] if len(sys.argv) == 2 else DEFAULT_DIR
    os.makedirs(OUT_DIR, exist_ok=True)
    index = FIRST_INDEX
    for path in sheets(folder):
        arr = np.array(Image.open(path).convert("RGBA"))
        h, w = arr.shape[:2]
        for r in range(2):
            for c in range(3):
                cell = arr[h * r // 2:h * (r + 1) // 2, w * c // 3:w * (c + 1) // 3]
                bake_cell(cell).save(os.path.join(OUT_DIR, f"loot_{index:02d}.png"))
                index += 1
    print(f"고블린 잡동사니 아이콘 {index - FIRST_INDEX}개 → loot_{FIRST_INDEX}~{index - 1}")


if __name__ == "__main__":
    main()
