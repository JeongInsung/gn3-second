"""몬스터 한 종의 시트 여러 장을 같은 크기·같은 발밑으로 맞춰 전투용 시트로 굽는다.

AI로 만든 시트는 동작마다 칸 크기(1774×887 → 443.5, 1618×972 → 404.5×486)와 그림 크기가 제각각이다.
그대로 BattleMonsterSprite(544 칸, 원본 칸 443.5 기준)에 넣으면 공격·쓰러짐에서 몬스터가 갑자기 커지거나 발이 뜬다.

하는 일:
  1. 시트마다 4열×2행 칸으로 나누고, 그림 픽셀을 연결 영역별로 무게중심이 든 칸(프레임)에 배정한다
     (옆 칸으로 넘친 그림을 제 프레임으로 — clean_monster_sheet.py와 같은 방식).
  2. 기준 시트(첫 번째 인자) 첫 장의 키·발 높이·가로 가운데를 잰다.
  3. 다른 시트는 각자 첫 장(서 있는 모습)을 기준 첫 장과 같은 키·발밑·가운데로 맞추는 배율·이동을 구해 모든 장에 똑같이 적용한다
     (프레임 사이 움직임 — 주저앉기·움츠리기 — 은 그대로).
  4. 544 칸 4×2 시트로 저장한다. 원본 칸 443.5 기준 위치를 쓰므로 BattleMonsterSprite.Layout과 맞는다.
  5. --idle 결과.png: 기준 시트 첫 장을 발밑 고정으로 숨 쉬듯(세로 100→97%) 늘였다 줄인 대기 8장을 만든다(대기 시트가 없을 때).

사용법:
  python Tools/bake_monster_set.py 기준(걷기).png 결과.png [다른.png 결과.png ...] [--idle 대기결과.png] [--ground]
  --ground: 기준이 아닌 시트는 장마다 몸통 아래끝을 발밑에 맞춘다(아랫줄이 떠 있는 쓰러짐·공격 시트).
            기준 결과가 필요 없으면 결과 자리에 임시 경로를 준다.
  --shrink k: 모든 시트를 k배로 줄여 굽는다(발밑·가운데는 그대로). 작게 그려진 공격 시트를 키워 맞추면 칸 밖으로 넘칠 때.
              화면 크기는 BattleMonsterSprite의 SizeScale을 1/k로 두어 되돌린다.

굽는 명령(2026-10-16):
  버섯: 걷기(기준) · 포자 공격 · 사망 → mushroom_walk/attack/die, --idle mushroom_idle, --ground
  늑대 쓰러짐: 늑대 대기 시트(기준, 임시 경로로) · 옆으로 쓰러지는 늑대 → wolf_die, --ground
  고블린 늑대기수: 대기(기준) · 걷기 · 공격 · 전투(쓰러짐) → goblin_rider_idle/walk/attack/die, --ground --shrink
                   (공격·쓰러짐 시트가 약 16% 작게 그려져 키워 맞추면 뛰어오르는 장이 칸을 넘었다)
  거목 엔트(2026-10-19): 걷기(기준) · 한 팔 휘두르기 · 뿌리 내려찍기 · 등장 포효 → ent_walk/attack/slam/roar,
                   --idle ent_idle, --ground --shrink 0.7 (동작 시트가 약 17~21% 작게 그려져 0.8·0.75에선 칸을 넘었다)
                   뿌리 가시 이펙트(root_spikes)는 크기를 맞추지 않는다: clean_monster_sheet.py로 넘침만 정리한 뒤
                   장마다 아래끝을 칸 85% 높이에 맞췄다(원본 아랫줄이 약 50px 떠 있어 솟을 때 튀었다).
                   2026-10-19 같은 명령에 쓰러짐 → ent_die, 붉은 눈 포효(기절 패턴) → ent_roar_stun을 더했다(기존 결과는 바이트까지 같음).
                   두 장은 원본에 몸에서 멀리 떨어진 옅은 얼룩이 있어, 굽은 뒤 작은 조각(몸에서 먼 것만)을 지웠다 — 나뭇잎은 남김.
                   같은 날 12장(4×3) 시트 두 장으로 교체: 엔트 킹의 하강 해머 슬램 → ent_attack(기본 공격),
                   엔트 킹의 두 손 뿌리내리기 → ent_slam(뿌리 내려찍기). 12장 시트는 행 수를 칸 모양으로 알아내 4×3(2176×1632)으로 굽는다.
                   (원본 폴더에서 예전 한 팔 휘두르기·뿌리 내려찍기 그림은 빠졌다 — 지금 명령: 걷기 · 등장 포효 · 두 손 뿌리내리기 · 해머 슬램)
                   굽고 나서 (1) 발 고정: python Tools/stabilize_sheet.py로 idle(맨 앞)·walk·attack·slam·roar·roar_stun
                   (die는 무너지며 옮겨 가 빼고) — 장마다 좌우로 미끄러지던 몸을 멈춘다.
                   (1.5) 다리 고정: python Tools/lock_legs.py <발 고정한 idle> <attack> <결과> <slam> <결과>
                   — 해머·뿌리내리기 장의 다리를 대기 다리로 바꿔 끼워 공격 중에도 다리가 움직이지 않는다(손·주먹·흙은 남김).
                   (2) 숲 픽셀화: 7장(idle 맨 앞)을 한 번에 넘겨 공용 팔레트로
                   python Tools/pixelize_sheet.py --block 2 --colors 64 --env Assets/_Project/Resources/Battle/oak_sway.png --breathe
                   — 숲 참나무와 같은 픽셀 크기·색(캐릭터가 아니라 숲의 일부처럼), 대기는 허리 위만 블록 단위로 숨 쉰다.
                   Unity에선 Bilinear·밉맵 끔·압축 HQ(BC7 — 블록이 DXT 4×4와 안 맞으면 색이 번진다).
  거목 엔트 교체(2026-10-19, 바탕 화면 몬스터/앤트 보스 2 — 하체 고정 12장 4×3 시트, 위 명령들은 예전 그림):
                   대기-1(기준, 임시 결과) · 대기-1 · 망치 강타-2 · 발을 고정한 엔트 왕의 내려치기-1 · 포효-5 · 붉은 눈-6 · 죽음-7
                   (같은 날 강타-3 · 양팔 땅찍기-4를 망치 강타 · 내려치기로 바꿨다 — 타격 장은 7번째 · 9번째)
                   → (임시)/ent_idle/attack/slam/roar/roar_stun/die, --ground --shrink 0.832
                   (대기를 두 번 넘겨야 대기도 --ground가 걸린다 — 원본 아랫줄 대기는 발이 30px 떠 있다.
                   0.832 = 예전 대기 몸 키 293px에 맞춘 값 → 화면 크기 SizeScale 그대로).
                   이어서 stabilize_sheet.py(대기 맨 앞, 6장 모두), 모든 칸을 가로 -12·세로 +8 옮겨 예전 발밑(479)·가운데에 맞추고,
                   pixelize_sheet.py --block 2 --colors 64 --env .../oak_sway.png(--breathe 없이 — 진짜 대기 12장).
                   lock_legs.py는 쓰지 않는다(새 그림은 다리가 이미 고정). 고정된 뿌리 다리의 엔트 왕-2는 쓰지 않음.
필요: numpy, scipy, Pillow
"""
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

COLUMNS = 4             # 열은 늘 4. 행은 시트 모양으로(rows_of — 8장 2행, 12장 3행)
CELL = 544              # 결과 칸
SOURCE_CELL = 443.5     # BattleMonsterSprite.SourceCell — 결과 칸 안 원본 칸 크기
ALPHA_MIN = 8
MERGED = 1.15           # 영역이 칸의 이 배보다 넓거나 높으면 옆 장과 붙은 것으로 보고 칸 경계에서 나눈다
IDLE_SQUASH = [1.0, 0.993, 0.983, 0.972, 0.968, 0.975, 0.986, 0.996]


def rows_of(w, h):
    """칸이 정사각이라 보고 행 수를 정한다(1774×887 → 2행 8장, 1448×1086 → 3행 12장)."""
    return max(1, round(h / (w / COLUMNS)))


def frames_of(path):
    """시트 → 프레임 8장(4×3 시트면 12장). 각 장은 (RGBA 배열, 칸 원점 기준 좌상단 x, y) — 칸 밖으로 넘친 부분까지 담는다."""
    src = np.array(Image.open(path).convert("RGBA"))
    h, w = src.shape[:2]
    rows = rows_of(w, h)
    cw, ch = w / COLUMNS, h / rows
    # 알파 > ALPHA_MIN으로 영역을 나눈다(알파 > 0이면 옅은 번짐이 옆 프레임끼리 이어 붙인다).
    mask = src[:, :, 3] > ALPHA_MIN
    labels, count = ndimage.label(mask)
    centers = ndimage.center_of_mass(mask, labels, range(1, count + 1))
    frame_of = np.zeros(count + 1, dtype=np.int32)
    for i, (cy, cx) in enumerate(centers, start=1):
        col = min(COLUMNS - 1, max(0, int(cx // cw)))
        row = min(rows - 1, max(0, int(cy // ch)))
        frame_of[i] = row * COLUMNS + col
    pixel_frame = np.where(labels > 0, frame_of[labels], -1)
    # 옆 장끼리 팔끝이 맞닿아 한 덩어리가 된 것(칸보다 훨씬 큰 영역)은 장마다 몸 한가운데에서 영역 안으로만 번져 나가
    # 먼저 닿은 장으로 나눈다 — 칸 경계로 자르면 경계를 넘은 손끝이 옆 장에 조각으로 남는다
    # (엔트 왕 포효 12장 — 팔을 펼친 장들이 이어 붙어 한 장이 통째로 비었다).
    for i, sl in enumerate(ndimage.find_objects(labels), start=1):
        if sl is None or (sl[1].stop - sl[1].start <= cw * MERGED and sl[0].stop - sl[0].start <= ch * MERGED):
            continue
        comp = labels[sl] == i
        owner = np.full(comp.shape, -1, np.int32)
        yy, xx = np.mgrid[sl[0], sl[1]]
        for f in range(COLUMNS * rows):
            c, r = f % COLUMNS, f // COLUMNS
            core = comp & (np.abs(xx - (c + 0.5) * cw) < cw * 0.2) & (np.abs(yy - (r + 0.5) * ch) < ch * 0.3)
            owner[core] = f
        while True:  # 영역 안에서 한 칸씩 번진다(먼저 닿은 장이 갖는다)
            free = comp & (owner < 0)
            if not free.any():
                break
            grown = False
            for f in np.unique(owner[owner >= 0]):
                reach = ndimage.binary_dilation(owner == f) & free & (owner < 0)
                if reach.any():
                    owner[reach] = f
                    grown = True
            if not grown:
                break
        left = comp & (owner < 0)  # 몸 한가운데와 이어지지 않은 곳은 칸으로
        owner[left] = (np.clip((yy[left] // ch).astype(int), 0, rows - 1) * COLUMNS
                       + np.clip((xx[left] // cw).astype(int), 0, COLUMNS - 1))
        pixel_frame[sl][comp] = owner[comp]
    # 옅은 가장자리(0 < 알파 ≤ ALPHA_MIN)는 가장 가까운 그림 픽셀의 프레임을 따른다.
    faint = (src[:, :, 3] > 0) & ~mask
    _, (near_y, near_x) = ndimage.distance_transform_edt(~mask, return_indices=True)
    fy, fx = np.nonzero(faint)
    pixel_frame[fy, fx] = pixel_frame[near_y[fy, fx], near_x[fy, fx]]

    frames = []
    for f in range(COLUMNS * rows):
        ys, xs = np.nonzero(pixel_frame == f)
        c, r = f % COLUMNS, f // COLUMNS
        if len(ys) == 0:
            frames.append((np.zeros((1, 1, 4), np.uint8), 0.0, 0.0))
            continue
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        crop = np.zeros((y1 - y0, x1 - x0, 4), np.uint8)
        crop[ys - y0, xs - x0] = src[ys, xs]
        frames.append((crop, x0 - c * cw, y0 - r * ch))  # 칸 원점 기준 위치
    return frames, (cw, ch)


def body_box(frame):
    """몸통(진한 픽셀의 가장 큰 덩어리 — 흩날리는 포자 등은 빼고) 위·아래·가운데(칸 원점 기준)."""
    crop, ox, oy = frame
    a = crop[:, :, 3] > 64
    labels, count = ndimage.label(a)
    if count > 1:
        sizes = np.bincount(labels.ravel())[1:]
        a = labels == (int(np.argmax(sizes)) + 1)
    ys, xs = np.nonzero(a)
    return oy + ys.min(), oy + ys.max() + 1, ox + (xs.min() + xs.max() + 1) / 2


def place(out, f, crop, x, y):
    """crop을 결과 칸 f 안 (x, y)(칸 원점 기준)에 그린다. 칸 밖은 자르고, 잘린 픽셀 수를 돌려준다."""
    c, r = f % COLUMNS, f // COLUMNS
    h, w = crop.shape[:2]
    x, y = int(round(x)), int(round(y))
    lost = 0
    for dy in range(h):
        ty = y + dy
        row = crop[dy]
        visible = row[:, 3] > 0
        if not (0 <= ty < CELL):
            lost += int(visible.sum())
            continue
        for dx in np.nonzero(visible)[0]:
            tx = x + dx
            if 0 <= tx < CELL:
                out[r * CELL + ty, c * CELL + tx] = row[dx]
            else:
                lost += 1
    return lost


def bake(frames, transform, dst, ground=False):
    """transform(scale, src_anchor(bottom, cx), dst_anchor(bottom, cx))를 모든 장에 적용해 굽는다.
    ground=True면 장마다 몸통 아래끝을 발밑(dst bottom)에 맞춘다 — 원본 시트의 아랫줄이 윗줄보다 떠 있는 경우
    (쓰러짐·공격처럼 발이 늘 바닥에 있는 동작에만 쓴다)."""
    scale, (sb, sx), (db, dx) = transform
    pad = (CELL - SOURCE_CELL) / 2
    rows = (len(frames) + COLUMNS - 1) // COLUMNS  # 입력과 같은 행 수(8장 → 2행, 12장 → 3행)
    out = np.zeros((CELL * rows, CELL * COLUMNS, 4), np.uint8)
    lost = 0
    for f, (crop, ox, oy) in enumerate(frames):
        if crop.shape[0] <= 1:
            continue
        if ground:
            _, bottom, _ = body_box((crop, ox, oy))
            oy += sb - bottom  # 이 장의 몸통 아래끝을 첫 장의 아래끝 높이로
        if scale != 1.0:
            img = Image.fromarray(crop).resize((max(1, round(crop.shape[1] * scale)), max(1, round(crop.shape[0] * scale))), Image.NEAREST)
            crop = np.array(img)
        x = (ox - sx) * scale + dx + pad
        y = (oy - sb) * scale + db + pad
        lost += place(out, f, crop, x, y)
    Image.fromarray(out).save(dst)
    return out, lost


def edge_report(out):
    bad = []
    for f in range(COLUMNS * (out.shape[0] // CELL)):
        c, r = f % COLUMNS, f // COLUMNS
        a = out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL, 3] > ALPHA_MIN
        if a[:, :3].any() or a[:, -3:].any() or a[:3, :].any() or a[-3:, :].any():
            bad.append(f)
    return bad


def main(args):
    ground = "--ground" in args
    args = [a for a in args if a != "--ground"]
    shrink = 1.0
    if "--shrink" in args:
        i = args.index("--shrink")
        shrink = float(args[i + 1])
        args = args[:i] + args[i + 2:]
    idle_dst = None
    if "--idle" in args:
        i = args.index("--idle")
        idle_dst = args[i + 1]
        args = args[:i] + args[i + 2:]
    if len(args) < 2 or len(args) % 2:
        sys.exit(__doc__)

    ref_frames, ref_cell = frames_of(args[0])
    ref_top, ref_bottom, ref_cx = body_box(ref_frames[0])
    ref_height = ref_bottom - ref_top
    # 기준 시트 칸이 원본 칸(443.5)과 다르면 그 비율로 맞춘다(보통 1774×887 → 443.5라 1).
    ref_scale = SOURCE_CELL / ref_cell[0]
    target = (ref_bottom * ref_scale, ref_cx * ref_scale)

    for i in range(0, len(args), 2):
        frames, _ = frames_of(args[i]) if i else (ref_frames, ref_cell)
        top, bottom, cx = body_box(frames[0])
        scale = ref_height * ref_scale / (bottom - top) * shrink
        out, lost = bake(frames, (scale, (bottom, cx), target), args[i + 1], ground=ground and i > 0)
        print(f"{args[i + 1]}: 배율 {scale:.3f}, 잘린 픽셀 {lost}, 가장자리 닿음 {edge_report(out) or '없음'}")

    if idle_dst:
        crop, ox, oy = ref_frames[0]
        top, bottom, cx = body_box(ref_frames[0])
        frames = []
        for k in IDLE_SQUASH:  # 발밑 고정으로 세로만 살짝 눌렀다 편다(가로는 그만큼 조금 넓게)
            h = max(1, round(crop.shape[0] * k))
            w = max(1, round(crop.shape[1] * (1 + (1 - k) * 0.5)))
            squashed = np.array(Image.fromarray(crop).resize((w, h), Image.NEAREST))
            # 발밑(bottom)과 가로 가운데를 그대로 둔다
            nox = cx - (cx - ox) * (w / crop.shape[1])
            noy = bottom - (bottom - oy) * k
            frames.append((squashed, nox, noy))
        # 기준 시트와 같은 배율·발밑(--shrink도 똑같이 — 빼먹으면 대기만 커서 걷기 시작할 때 몸이 줄어든다)
        out, lost = bake(frames, (ref_scale * shrink, (bottom, cx), target), idle_dst)
        print(f"{idle_dst}: 대기 8장, 잘린 픽셀 {lost}, 가장자리 닿음 {edge_report(out) or '없음'}")


if __name__ == "__main__":
    main(sys.argv[1:])
