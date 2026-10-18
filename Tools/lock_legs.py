"""뿌리박은 몬스터(거목 엔트)의 동작 시트에서 다리를 대기 다리로 바꿔 끼운다 — 공격할 때 다리가 움직이지 않게.

AI가 그린 동작 장들은 장마다 다리를 새로 그려(해머 슬램은 앞으로 내딛고, 두 손 뿌리내리기는 웅크려 다리를 벌린다)
발을 고정해도(stabilize_sheet.py) 다리가 꿈틀거린다. 그래서 장마다:
  1. 대기 첫 장의 다리(엉덩이 줄 HIP 아래, 양옆에 늘어진 손은 IDLE_DROP 다각형으로 뺀다)를 깐다
     — 이음매가 비지 않게 엉덩이 줄 SEAM px 위부터.
  2. 그 장의 엉덩이 줄 위를 그대로 덮는다.
  3. 엉덩이 줄 아래는 그 장의 다리를 버리고 손·주먹·흙만 덮는다:
     대기 다리 자리(ZONE px 넓힌 것) 밖은 손으로 보고 덮고, 다리 앞으로 온 손은 KEEP 다각형, 다리 자리 밖으로 벌어진
     옛 다리 조각은 DROP 다각형으로 장마다 손으로 정한다.
픽셀화(pixelize_sheet.py) 전에 한다. 다리 원본 픽셀이 모든 장에서 같아 픽셀화해도 같은 블록이 나온다.

사용법: python Tools/lock_legs.py 대기.png 입력.png 결과.png [입력.png 결과.png ...]
  다각형 표는 결과 파일 이름(ent_attack, ent_slam)으로 찾는다. 좌표는 칸(544px) 안 픽셀.
필요: numpy, scipy, Pillow
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

CELL = 544
HIP = 355     # 엉덩이 줄(칸 안 y) — 이 아래가 다리
SEAM = 15     # 대기 다리를 엉덩이 줄 위로 이만큼 더 깐다(이음매 빈틈 막기)
ZONE = 22     # 대기 다리 자리를 이만큼 넓힌 곳 안의 그 장 픽셀은 옛 다리로 보고 버린다
HAND_SHARE = 0.4  # 선으로 둘러싸인 조각이 이 비율 이상 다리 자리 밖이면 조각째 손
LINE_LUMA = 60    # 이보다 어두우면 선
LINE_GROW = 4     # 손 조각 둘레 선을 이만큼 붙인다
CAP = 14      # 잘린 끝을 둥글게 깎는 깊이(px)
LINE_W = 3    # 잘린 끝을 닫는 선 두께(px)
LUMA = np.array([0.299, 0.587, 0.114])
# 대기 첫 장에서 다리가 아닌 것(양옆에 늘어진 손)
IDLE_DROP = [
    [(125, 300), (224, 300), (224, 392), (214, 422), (125, 422)],  # 왼손
    [(349, 320), (430, 320), (430, 432), (351, 432)],              # 오른손
]
# 장마다 엉덩이 줄 아래에서 덮을 것(다리 앞으로 온 손·주먹·흙)과 버릴 것(다리 자리 밖으로 벌어진 옛 다리)
_LEFT_LEG = [(50, 355), (222, 355), (222, 420), (182, 440), (182, 480), (50, 480)]  # 왼쪽으로 벌어진 옛 왼다리·허벅지
_SLAM_HANDS = [
    [(240, 345), (302, 345), (346, 400), (350, 466), (240, 466), (232, 420)],  # 가운데: 왼손을 땅에 박음
    [(345, 340), (410, 340), (446, 410), (452, 472), (338, 472), (338, 400)],  # 오른쪽: 오른손을 땅에 박음
]
KEEP = {
    "ent_attack": {  # 해머: 7번째 장 팔을 내리고 8·9번째 장 주먹이 다리 앞 땅을 친다
        7: [[(245, 358), (330, 358), (420, 382), (448, 420), (448, 478), (305, 478), (300, 420), (255, 395)]],
        8: [[(270, 340), (340, 340), (420, 395), (462, 405), (462, 482), (288, 482), (280, 420)]],
        9: [[(250, 345), (335, 345), (420, 390), (462, 400), (462, 482), (275, 482), (265, 420), (245, 395)]],
    },
    "ent_slam": {k: _SLAM_HANDS for k in (8, 9, 10, 11)},  # 두 손을 다리 앞 땅에 박는다
}
DROP = {
    "ent_attack": {k: [_LEFT_LEG] for k in (6, 7, 8, 9)},
    "ent_slam": {k: [_LEFT_LEG] for k in (7, 8, 9, 10, 11)},
}


def polys_mask(polys):
    """다각형들 → 칸 크기 마스크."""
    img = Image.new("L", (CELL, CELL), 0)
    d = ImageDraw.Draw(img)
    for p in polys:
        d.polygon(p, fill=255)
    return np.array(img) > 0


def idle_legs(idle_sheet):
    """대기 첫 장 → (다리 픽셀 RGBA, 다리 마스크). 엉덩이 줄 SEAM 위부터, 손은 뺀다."""
    cell = idle_sheet[:CELL, :CELL].copy()
    legs = cell[..., 3] > 127
    legs[:HIP - SEAM] = False
    legs &= ~polys_mask(IDLE_DROP)
    return cell, legs


def lock(frame, base, legs, keep, drop):
    """장 하나: 대기 다리 깔고 → 엉덩이 위 덮고 → 엉덩이 아래 손만 덮는다."""
    out = np.zeros_like(frame)
    out[legs] = base[legs]
    solid = frame[..., 3] > 127
    above = solid.copy()
    above[HIP:] = False
    out[above] = frame[above]
    zone = ndimage.binary_dilation(legs, iterations=ZONE)
    below = solid.copy()
    below[:HIP] = False
    # 손가락·주먹은 검은 선으로 둘러싸인 조각들: 조각의 HAND_SHARE 이상이 다리 자리 밖이면 조각째 손으로 본다
    # (다리 자리 경계에서 손이 칼로 자른 듯 끊기지 않게). 선은 손 조각 옆에 붙은 것만.
    inner = below & (frame[..., :3] @ LUMA >= LINE_LUMA)
    labels, count = ndimage.label(inner)
    if count:
        outside = ndimage.sum(~zone, labels, range(1, count + 1))
        size = ndimage.sum(np.ones_like(zone), labels, range(1, count + 1))
        is_hand = np.concatenate([[False], outside / np.maximum(size, 1) >= HAND_SHARE])
        hand = is_hand[labels]
    else:
        hand = np.zeros_like(inner)
    hand |= below & ~zone  # 다리 자리 밖은 선까지 모두
    hand = (hand | (ndimage.binary_dilation(hand, iterations=LINE_GROW) & below & ~inner))
    hands = (hand | (below & polys_mask(keep))) & ~polys_mask(drop)
    out[hands] = frame[hands]
    cap_cut(out, frame)
    return out


def cap_cut(out, frame):
    """엉덩이 줄에서 잘린 채 아래가 빈 윗몸 조각(웅크린 장의 허벅지 등) 끝을 둥글게 깎고 선으로 닫는다 — 칼로 자른 듯한 일자 끝을 없앤다."""
    solid = out[..., 3] > 127
    cut = solid[HIP - 1] & ~solid[HIP]
    dark = frame[(frame[..., 3] > 127) & (frame[..., :3] @ LUMA < 40)][:, :3]
    line = np.median(dark, axis=0).astype(np.uint8) if len(dark) else np.array([20, 12, 8], np.uint8)
    x = 0
    while x < CELL:
        if not cut[x]:
            x += 1
            continue
        a = x
        while x < CELL and cut[x]:
            x += 1
        b = x - 1
        w = b - a + 1
        r = min(CAP, w / 2)
        open_l = a == 0 or not solid[HIP, a - 1]  # 다리에 붙은 쪽은 그대로, 빈 곳을 향한 쪽으로 둥글게 올린다
        open_r = b == CELL - 1 or not solid[HIP, b + 1]
        for col in range(a, b + 1):
            if open_l and open_r:  # 반 타원
                t = (col - (a + b) / 2) / (w / 2)
            elif open_l:           # 다리 쪽(오른쪽)에서 빈 쪽(왼쪽)으로 4분의 1 타원
                t = (b - col) / w
            elif open_r:
                t = (col - a) / w
            else:
                t = 0.0
            depth = r * (1 - np.sqrt(max(0.0, 1 - t * t)))
            bottom = HIP - 1 - int(round(depth))
            out[bottom + 1:HIP, col] = 0
            rows = slice(max(bottom - LINE_W + 1, 0), bottom + 1)
            keep = out[rows, col, 3] > 127
            out[rows, col, :3][keep] = line


def main(args):
    if len(args) < 3 or len(args) % 2 == 0:
        sys.exit(__doc__)
    base, legs = idle_legs(np.array(Image.open(args[0]).convert("RGBA")))
    for src, dst in zip(args[1::2], args[2::2]):
        name = os.path.splitext(os.path.basename(dst))[0]
        sheet = np.array(Image.open(src).convert("RGBA"))
        out = np.zeros_like(sheet)
        rows, cols = sheet.shape[0] // CELL, sheet.shape[1] // CELL
        for k in range(rows * cols):
            r, c = divmod(k, cols)
            sl = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            if not (sheet[sl][..., 3] > 127).any():
                continue
            out[sl] = lock(sheet[sl], base, legs,
                           [p for p in KEEP.get(name, {}).get(k, [])], [p for p in DROP.get(name, {}).get(k, [])])
        Image.fromarray(out).save(dst)
        print(name, "다리 고정", rows * cols, "장")


if __name__ == "__main__":
    main(sys.argv[1:])
