"""왼쪽 위 메뉴 버튼 아이콘 시트(3x2)를 칸별 96x96 아이콘으로 굽는다.

    python tools/bake_menu_icons.py [메뉴 아이콘 6종.png]

순서(왼→오, 위→아래): 용병시장 · 파티 구성 · 퀘스트 / 도감 · 근처 길드 · 건설
→ Assets/_Project/Resources/UIIcons/menu_*.png (MainMenuBootstrapper.CreateMenuBar가 읽는다).
자르기·투명 처리는 bake_loot_icons.py와 같다(옅은 후광은 버린다).
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import ROOT, bake_cell  # noqa: E402

DEFAULT_SHEET = os.path.join(os.path.expanduser("~"), "Downloads", "중세 판타지 RPG 메뉴 아이콘 6종.png")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Resources", "UIIcons")
NAMES = ["menu_market", "menu_party", "menu_quest", "menu_bestiary", "menu_guilds", "menu_build"]
COLS, ROWS = 3, 2


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SHEET
    os.makedirs(OUT_DIR, exist_ok=True)
    arr = np.array(Image.open(path).convert("RGBA"))
    h, w = arr.shape[:2]
    for i, name in enumerate(NAMES):
        r, c = divmod(i, COLS)
        y0, y1 = h * r // ROWS, h * (r + 1) // ROWS
        x0, x1 = w * c // COLS, w * (c + 1) // COLS
        bake_cell(arr[y0:y1, x0:x1]).save(os.path.join(OUT_DIR, f"{name}.png"))
    print(f"메뉴 아이콘 {len(NAMES)}개 → {OUT_DIR}")


if __name__ == "__main__":
    main()
