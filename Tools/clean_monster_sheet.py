"""몬스터 스프라이트 시트 정리: 옆 칸으로 삐져나온 그림을 제 프레임으로 돌려놓고, 여백 있는 정수 격자로 다시 만든다.

AI로 만든 4열×2행 시트는 칸 폭이 정수가 아니고(1774/4 = 443.5) 칸 사이 여백이 없어서,
검처럼 길게 뻗은 부분이 옆 칸으로 넘어가 있다. 격자대로 자르면 옆 프레임에 남의 검끝이 보이고 제 검끝은 잘린다.

하는 일:
  1. 알파 > ALPHA_MIN 픽셀을 연결 영역으로 나눈다.
  2. 각 영역을 무게중심이 들어 있는 원래 칸(프레임)에 배정한다(넘어간 검은 몸통과 이어져 있어 제 프레임으로 간다).
  3. 칸 CELL×CELL 정수 격자의 새 시트에, 프레임마다 원래 칸 원점 기준 위치를 그대로 유지한 채 사방 여백을 두고 그린다.
     (프레임 사이 발·몸 위치가 흔들리지 않고, 칸 사이가 투명이라 Bilinear가 옆 칸을 묻혀 오지 않는다)
  4. 새 시트의 칸 가장자리에 픽셀이 닿는지 검사해 알려 준다.

Unity 쪽(BattleMonsterSprite)은 CELL·SOURCE_CELL 값을 같은 것으로 쓴다.

사용법: python Tools/clean_monster_sheet.py [--flip] 원본.png 결과.png [원본.png 결과.png ...]
  --flip: 정리한 뒤 칸마다 좌우를 뒤집는다(프레임 순서는 그대로). 전투 UI는 오른쪽을 보는 그림을 기준으로
          적 쪽을 Facing으로 뒤집으므로, 왼쪽을 보는 원본에만 쓴다(고블린·늑대 시트는 오른쪽을 봐서 필요 없다).
필요: numpy, scipy, Pillow
"""
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

COLUMNS, ROWS = 4, 2
CELL = 544          # 새 칸 크기(정수)
ALPHA_MIN = 8       # 이보다 투명한 픽셀은 배경으로 본다
EDGE = 3            # 검사: 새 칸 가장자리 이 폭 안에 그림이 있으면 경고


def clean(src_path, dst_path, flip=False):
    src = np.array(Image.open(src_path).convert("RGBA"))
    height, width = src.shape[:2]
    cell_w, cell_h = width / COLUMNS, height / ROWS
    pad_x, pad_y = (CELL - cell_w) / 2, (CELL - cell_h) / 2

    mask = src[:, :, 3] > ALPHA_MIN
    labels, count = ndimage.label(mask)
    centers = ndimage.center_of_mass(mask, labels, range(1, count + 1))

    # 영역 번호 → 프레임 번호(무게중심이 든 원래 칸)
    frame_of = np.zeros(count + 1, dtype=np.int32)
    for i, (cy, cx) in enumerate(centers, start=1):
        col = min(COLUMNS - 1, max(0, int(cx // cell_w)))
        row = min(ROWS - 1, max(0, int(cy // cell_h)))
        frame_of[i] = row * COLUMNS + col
    pixel_frame = np.where(labels > 0, frame_of[labels], -1)

    out = np.zeros((CELL * ROWS, CELL * COLUMNS, 4), dtype=np.uint8)
    ys, xs = np.nonzero(pixel_frame >= 0)
    frames = pixel_frame[ys, xs]
    cols, rows = frames % COLUMNS, frames // COLUMNS
    # 원래 칸 원점 기준 위치 + 여백 → 새 칸 안 위치
    dst_x = np.round(xs - cols * cell_w + pad_x).astype(int) + cols * CELL
    dst_y = np.round(ys - rows * cell_h + pad_y).astype(int) + rows * CELL
    inside = (dst_x >= cols * CELL) & (dst_x < (cols + 1) * CELL) & (dst_y >= rows * CELL) & (dst_y < (rows + 1) * CELL)
    out[dst_y[inside], dst_x[inside]] = src[ys[inside], xs[inside]]
    lost = int((~inside).sum())

    # 반투명 가장자리(알파 ≤ ALPHA_MIN)는 연결 영역에서 빠진다. 격자 칸으로 배정하면 옆 프레임 검끝의 흐린 테두리가
    # 따라 들어오므로, 가장 가까운 그림 픽셀의 프레임을 따른다.
    faint = (src[:, :, 3] > 0) & ~mask
    _, (near_y, near_x) = ndimage.distance_transform_edt(~mask, return_indices=True)
    fy, fx = np.nonzero(faint)
    fframe = pixel_frame[near_y[fy, fx], near_x[fy, fx]]
    fcol, frow = fframe % COLUMNS, fframe // COLUMNS
    fdx = np.round(fx - fcol * cell_w + pad_x).astype(int) + fcol * CELL
    fdy = np.round(fy - frow * cell_h + pad_y).astype(int) + frow * CELL
    ok = (fdx >= fcol * CELL) & (fdx < (fcol + 1) * CELL) & (fdy >= frow * CELL) & (fdy < (frow + 1) * CELL)
    fy, fx, fdx, fdy = fy[ok], fx[ok], fdx[ok], fdy[ok]
    empty = out[fdy, fdx, 3] == 0
    out[fdy[empty], fdx[empty]] = src[fy[empty], fx[empty]]

    if flip:  # 칸마다 좌우 뒤집기(칸 안 위치 기준이라 발 높이·프레임 순서는 그대로)
        for r in range(ROWS):
            for c in range(COLUMNS):
                block = out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
                out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = block[:, ::-1].copy()

    Image.fromarray(out).save(dst_path)

    # 검사: 새 칸 가장자리에 그림이 닿는지
    problems = []
    for frame in range(COLUMNS * ROWS):
        c, r = frame % COLUMNS, frame // COLUMNS
        a = out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, 3] > ALPHA_MIN
        edges = [int(a[:, :EDGE].sum()), int(a[:, -EDGE:].sum()), int(a[:EDGE, :].sum()), int(a[-EDGE:, :].sum())]
        if any(edges):
            problems.append((frame, edges))
    # 원래 격자 경계를 가로지르던 영역 수(이번 정리로 한 프레임에 온전히 모인 것)
    crossing = 0
    for box in ndimage.find_objects(labels):
        if box is None:
            continue
        y0, y1, x0, x1 = box[0].start, box[0].stop - 1, box[1].start, box[1].stop - 1
        if int(x0 // cell_w) != int(x1 // cell_w) or int(y0 // cell_h) != int(y1 // cell_h):
            crossing += 1
    print(f"{dst_path}: {out.shape[1]}x{out.shape[0]}, 영역 {count}개, 칸 경계를 넘던 영역 {crossing}개 제 프레임으로, "
          f"잘린 픽셀 {lost}, 가장자리 닿음 {problems if problems else '없음'}")


if __name__ == "__main__":
    args = sys.argv[1:]
    flip = "--flip" in args
    args = [a for a in args if a != "--flip"]
    if len(args) < 2 or len(args) % 2:
        sys.exit(__doc__)
    for i in range(0, len(args), 2):
        clean(args[i], args[i + 1], flip)
