"""전리품 아이콘 시트 두 장을 칸별 96x96 아이콘으로 굽는다.

    python tools/bake_loot_icons.py [몬스터 아틀라스.png] [고블린 시트.png]

- 몬스터 전리품 아틀라스(4x4): loot_00 ~ loot_15 (줄마다 늑대기수 · 늑대 · 독버섯 · 엔트)
- 고블린 전사·궁수 시트(4x2): loot_16 ~ loot_23 (윗줄 전사 · 아랫줄 궁수)
칸마다 알파가 ALPHA_MIN보다 큰 픽셀의 경계로 잘라(옅은 후광은 버리고 남은 픽셀은 불투명하게)
비율을 지켜 96x96 가운데에 넣는다. 출력: Assets/_Project/Resources/Loot/ (Economy/Loot.cs가 읽는다).
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Resources", "Loot")
DOWNLOADS = os.path.join(os.path.expanduser("~"), "Downloads")
DEFAULT_SHEETS = [
    (os.path.join(DOWNLOADS, "픽셀 판타지 몬스터 전리품 아틀라스.png"), 4, 4),
    (os.path.join(DOWNLOADS, "고블린 전사와 궁수의 전리품 아이콘 시트.png"), 4, 2),
]
SIZE = 96
PADDING = 4          # 아이콘 가장자리 여백(px)
ALPHA_MIN = 24       # 이보다 옅은 픽셀(후광)은 버린다
MIN_PART = 40        # 이보다 작은 조각(옆 칸에서 넘어온 부스러기)은 버린다


def largest_parts(mask):
    """칸 안에서 남길 조각만의 마스크. 아이콘이 여러 조각(동전·물방울·포자)이라 큰 조각들은 다 남기되,
    칸 가장자리에 닿은 조각(옆 칸 아이콘이 넘어온 것)과 작은 부스러기는 버린다. 가장 큰 조각은 늘 남긴다."""
    from scipy import ndimage
    labels, n = ndimage.label(mask)
    if n == 0:
        return mask
    idx = range(1, n + 1)
    sizes = np.asarray(ndimage.sum(mask, labels, idx))
    edge = set(np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]]))) - {0}
    keep = np.zeros(n + 1, bool)
    biggest = int(np.argmax(sizes)) + 1
    for i, size in zip(idx, sizes):
        keep[i] = i == biggest or (i not in edge and size >= max(MIN_PART, sizes.max() * 0.01))
    return keep[labels]


def bake_cell(cell):
    a = cell[:, :, 3]
    mask = largest_parts(a > ALPHA_MIN)
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        raise ValueError("빈 칸")
    out = cell.copy()
    out[:, :, 3] = np.where(mask, 255, 0)
    crop = out[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    img = Image.fromarray(crop, "RGBA")
    inner = SIZE - PADDING * 2
    scale = inner / max(img.width, img.height)
    w, h = max(1, round(img.width * scale)), max(1, round(img.height * scale))
    img = img.resize((w, h), Image.LANCZOS)
    # 크기를 줄이며 생긴 반투명 가장자리는 그대로 둔다(아이콘은 Bilinear로 그린다)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(img, ((SIZE - w) // 2, (SIZE - h) // 2), img)
    return canvas


def main():
    sheets = DEFAULT_SHEETS
    if len(sys.argv) == 3:
        sheets = [(sys.argv[1], 4, 4), (sys.argv[2], 4, 2)]
    os.makedirs(OUT_DIR, exist_ok=True)
    index = 0
    for path, cols, rows in sheets:
        arr = np.array(Image.open(path).convert("RGBA"))
        h, w = arr.shape[:2]
        for r in range(rows):
            for c in range(cols):
                y0, y1 = h * r // rows, h * (r + 1) // rows
                x0, x1 = w * c // cols, w * (c + 1) // cols
                icon = bake_cell(arr[y0:y1, x0:x1])
                icon.save(os.path.join(OUT_DIR, f"loot_{index:02d}.png"))
                index += 1
    print(f"{index}개 아이콘 → {OUT_DIR}")


if __name__ == "__main__":
    main()
