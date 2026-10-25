"""황금 진행률 픽셀 로딩 바 그림을 전투 시체 뒤지기 바(BattleUIController) 두 장으로 굽는다.

    python tools/bake_loot_bar.py [황금 진행률 픽셀 로딩 바.png]

- loot_bar_frame.png: 나무 테두리·금 장식만 남기고 안쪽 채움 칸을 빈칸 남색으로 칠한 "빈 바"
- loot_bar_fill.png: 주황 채움 띠(가로로 늘려 fillAmount로 채운다)
원본은 크게 그린 픽셀 그림(한 점 ≈ PIXEL px)이라 점 하나가 1px이 되게 줄인다(NEAREST, 점 가운데).
채움 칸이 테두리 안에서 차지하는 비율을 출력한다(BattleUIController.LootBarInset* 상수).
출력: Assets/_Project/Resources/Battle/UI/
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT = os.path.join(os.path.expanduser("~"), "Downloads", "황금 진행률 픽셀 로딩 바.png")
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Resources", "Battle", "UI")
PIXEL = 8                       # 원본 그림의 한 점 크기(px)
CHANNEL = (200, 322, 1980, 414)  # 채움 칸(x0, y0, x1, y1) — 원본 좌표, 바깥 검은 선 안쪽
FILL_X = (212, 1290)             # 주황이 칸 높이 전체에 차 있는 가로 구간(채움 그림으로 잘라 쓴다)
FILL_Y = (330, 406)              # 칸 위아래 검은 선을 뺀 주황 띠 높이
EMPTY = (52, 53, 63, 255)        # 빈칸 남색(원본 오른쪽 빈 부분)


def shrink(img):
    w, h = img.size
    return img.resize((max(1, round(w / PIXEL)), max(1, round(h / PIXEL))), Image.NEAREST)


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
    arr = np.array(Image.open(src).convert("RGBA"))
    rgb = arr[:, :, :3].astype(int)
    alpha = (arr[:, :, 3] > 128) & (rgb.sum(axis=2) < 690)  # 흰 배경 테두리 부스러기는 버린다
    # 바의 위아래 끝: 가로로 절반 넘게 차 있는 줄까지(아래 그림자 부스러기는 버린다)
    rows = np.nonzero(alpha.sum(axis=1) > alpha.shape[1] * 0.3)[0]
    y0, y1 = rows.min(), rows.max() + 1
    alpha[:y0] = False
    alpha[y1:] = False
    xs = np.nonzero(alpha.any(axis=0))[0]
    x0, x1 = xs.min(), xs.max() + 1
    arr[~alpha] = 0
    arr[alpha, 3] = 255

    fill = arr[FILL_Y[0]:FILL_Y[1], FILL_X[0]:FILL_X[1]].copy()
    frame = arr.copy()
    frame[CHANNEL[1]:CHANNEL[3], CHANNEL[0]:CHANNEL[2]] = EMPTY
    frame = frame[y0:y1, x0:x1]

    os.makedirs(OUT_DIR, exist_ok=True)
    shrink(Image.fromarray(frame, "RGBA")).save(os.path.join(OUT_DIR, "loot_bar_frame.png"))
    shrink(Image.fromarray(fill, "RGBA")).save(os.path.join(OUT_DIR, "loot_bar_fill.png"))
    w, h = x1 - x0, y1 - y0
    print("frame", w, h, "->", round(w / PIXEL), round(h / PIXEL))
    print("inset left %.4f right %.4f bottom %.4f top %.4f" % (
        (CHANNEL[0] - x0) / w, (x1 - CHANNEL[2]) / w, (y1 - CHANNEL[3]) / h, (CHANNEL[1] - y0) / h))


if __name__ == "__main__":
    main()
