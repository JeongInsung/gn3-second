"""제자리에 뿌리박은 몬스터(거목 엔트) 시트의 발을 고정한다 — 장마다 몸이 좌우로 미끄러져 흔들흔들해 보이는 것을 없앤다.

AI가 그린 동작 장들은 장마다 그림이 조금씩 옆으로 비껴 그려져(엔트 두 손 뿌리내리기는 발이 55px까지 움직였다)
굽고 나면 뿌리박은 몸이 좌우로 흔들린다. 그래서 장마다 아랫몸 띠(발 줄에서 BAND px 위까지)의 실루엣이
기준(처음 넘긴 시트의 첫 장)과 가장 잘 겹치는 가로 이동(±SEARCH px)을 찾아 장 전체를 옮긴다.
띠가 낮으면(70px) 땅을 짚은 손·해머가 발로 잡혀 엉뚱하게 옮겨서, 다리·몸통 아래까지 넉넉히 본다.
세로 발 줄은 bake_monster_set.py가 이미 맞춰 둔다. 띠가 기준과 별로 안 겹치는 장은 그대로 둔다.
쓰러짐처럼 몸이 무너지며 옮겨 가는 시트는 넘기지 않는다.

사용법: python Tools/stabilize_sheet.py 입력.png 결과.png [입력.png 결과.png ...]
  첫 시트의 첫 장이 모든 시트의 기준 — 대기 시트를 맨 앞에 넘긴다.
필요: numpy, Pillow
"""
import sys

import numpy as np
from PIL import Image

CELL = 544
BAND = 240     # 발 줄에서 이만큼 위까지가 아랫몸 띠
SEARCH = 40    # 가로 이동을 찾는 범위(±px)
MAX_MISS = 0.5 # 가장 잘 맞춰도 띠가 이 비율 이상 어긋나면 옮기지 않는다


def frames(sheet):
    """칸 원점 목록(그림이 있는 칸만)."""
    rows, cols = sheet.shape[0] // CELL, sheet.shape[1] // CELL
    for r in range(rows):
        for c in range(cols):
            cell = sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
            if (cell[..., 3] > 127).any():
                yield r * CELL, c * CELL


def shift_x(a, dx):
    """가로로 dx px 옮긴다(밀려난 곳은 투명)."""
    out = np.zeros_like(a)
    if dx > 0:
        out[:, dx:] = a[:, :-dx]
    elif dx < 0:
        out[:, :dx] = a[:, -dx:]
    else:
        out[:] = a
    return out


def best_shift(band, ref):
    """band를 옮겨 ref와 가장 덜 어긋나는 dx와 어긋난 비율."""
    best, miss = 0, None
    total = max(ref.sum() + band.sum(), 1)
    for dx in range(-SEARCH, SEARCH + 1):
        m = (shift_x(band, dx) ^ ref).sum() / total
        if miss is None or m < miss or (m == miss and abs(dx) < abs(best)):
            best, miss = dx, m
    return best, miss


def stabilize(pairs):
    """pairs = [(입력, 결과)]. 결과가 None이면 저장 안 함. [(시트, [장별 dx])]를 돌려준다."""
    ref = None
    outs = []
    for src, dst in pairs:
        sheet = np.array(Image.open(src).convert("RGBA"))
        out = np.zeros_like(sheet)
        shifts = []
        for y, x in frames(sheet):
            cell = sheet[y:y + CELL, x:x + CELL]
            mask = cell[..., 3] > 127
            if ref is None:
                bottom = np.nonzero(mask.any(axis=1))[0].max()
                ref = (bottom, mask[bottom - BAND:bottom + 1])
            bottom, ref_band = ref
            dx, miss = best_shift(mask[bottom - BAND:bottom + 1], ref_band)
            if miss > MAX_MISS:
                dx = 0
            out[y:y + CELL, x:x + CELL] = shift_x(cell, dx)
            shifts.append(dx)
        if dst:
            Image.fromarray(out).save(dst)
        outs.append((out, shifts))
    return outs


def main(args):
    if len(args) < 2 or len(args) % 2:
        sys.exit(__doc__)
    pairs = [(args[i], args[i + 1]) for i in range(0, len(args), 2)]
    for (src, _), (_, shifts) in zip(pairs, stabilize(pairs)):
        print(src.replace("\\", "/").split("/")[-1], "가로 이동", shifts)


if __name__ == "__main__":
    main(sys.argv[1:])
