"""정면형 소품 시트(3x2)를 칸별 장식 원본 그림으로 자르고, 한 장짜리 소품(SINGLES — 허수아비)도 같이 다듬는다.

    python tools/bake_prop_sheet.py [정면형 픽셀 아트 소품 시트.png]

칸마다 알파가 ALPHA_MIN보다 큰 큰 조각만 남겨(옅은 불빛 후광은 버리고 남은 픽셀은 불투명하게) 경계로 자른다.
크기는 원본 그대로 두고, 마을 크기로 줄이는 일은 PixelBaker(DecorTemplateBuilder)가 한다.
출력: Assets/조정식/Decorations/소품/ (BuildingCatalog.Decorations의 소품 6종).
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bake_loot_icons import DOWNLOADS, ROOT, largest_parts  # noqa: E402

DEFAULT_SHEET = os.path.join(DOWNLOADS, "정면형 픽셀 아트 소품 시트.png")
OUT_DIR = os.path.join(ROOT, "Assets", "조정식", "Decorations", "소품")
NAMES = [["나무 상자", "술통", "랜턴 기둥"], ["모닥불", "깃발", "자루"]]
# 한 장짜리 소품 그림(다운로드 파일 이름 → 저장 이름)
SINGLES = [("살짝 돌아선 픽셀 허수아비 표적.png", "허수아비")]
ALPHA_MIN = 24


def cut(cell):
    mask = largest_parts(cell[:, :, 3] > ALPHA_MIN)
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        raise ValueError("빈 칸")
    out = cell.copy()
    out[:, :, 3] = np.where(mask, 255, 0)
    return Image.fromarray(out[ys.min():ys.max() + 1, xs.min():xs.max() + 1], "RGBA")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SHEET
    arr = np.array(Image.open(path).convert("RGBA"))
    h, w = arr.shape[:2]
    rows, cols = len(NAMES), len(NAMES[0])
    os.makedirs(OUT_DIR, exist_ok=True)
    for r in range(rows):
        for c in range(cols):
            cell = arr[h * r // rows:h * (r + 1) // rows, w * c // cols:w * (c + 1) // cols]
            img = cut(cell)
            img.save(os.path.join(OUT_DIR, NAMES[r][c] + ".png"))
            print(NAMES[r][c], img.size)
    for src, name in SINGLES:
        src_path = os.path.join(DOWNLOADS, src)
        if not os.path.exists(src_path):
            print("없음:", src)
            continue
        img = cut(np.array(Image.open(src_path).convert("RGBA")))
        img.save(os.path.join(OUT_DIR, name + ".png"))
        print(name, img.size)


if __name__ == "__main__":
    main()
