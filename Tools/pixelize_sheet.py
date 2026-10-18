"""구운 몬스터 시트를 진짜 픽셀 아트처럼 다시 그린다(같은 크기·같은 칸 배치·같은 발밑).

AI가 그린 "픽셀풍" 시트는 픽셀 크기가 일정한 격자가 아니고, 가장자리가 반투명(안티앨리어싱)이며, 색이 수천 가지라
진짜 픽셀 캐릭터(용병) 옆에서 그림처럼 보인다. 그래서:
  1. 칸(CELL, 544px) 원점부터 B×B 블록마다 한 색 — 격자를 모든 장·모든 시트에 똑같이 고정해 동작할 때 픽셀이 떨리지 않게.
     블록 색은 평균이 아니라 대표색(블록을 밝은/어두운 두 무리로 나눠 큰 무리의 중앙값) — 평균하면 명암이 뭉개진다.
     선·눈은 작아서 대표색에서 빠지므로 비중이 크면 그 색을 쓴다. 블록 평균 알파 50% 이상이면 불투명·아니면 투명.
  2. 넘겨준 시트 전체를 합쳐 공용 팔레트 N색(Lab 공간 k-means, seed 고정)으로 줄인다 — 장마다 색이 흔들리지 않게,
     사람 눈 기준으로 고르게 나눠야 나무껍질·이끼의 명암 단계가 살아난다.
  3. 정리: 실루엣의 1블록 구멍은 메우고 1블록 티끌은 지운다. 상하좌우 넷 중 셋 이상이 같은 색인데 혼자 다른 블록은 그 색으로.
  4. 외곽선(셀아웃): 실루엣 가장자리 블록 중 밝은 것은 제 색을 어둡게(Lab 명도 SELOUT배) 해 팔레트에 맞춘다
     — 검은 테두리로 통일하면 두껍고 납작해 보인다. 가장자리는 상하좌우 기준이라 1블록 두께(모서리 겹침 없음).
  5. 블록을 B배 nearest로 키워 원래 크기로 저장. Unity에선 Bilinear·밉맵 끔으로 쓴다
     — 이미 B배로 키운 그림이라 블록 경계 1텍셀만 부드러워지고, 화면에서 축소돼도 픽셀 크기가 고르다(Point는 들쭉날쭉).
  6. 깜빡임 막기: 시트의 장을 차례로 처리하며, 블록 원래 색이 앞 장 같은 자리 팔레트 색과 ΔE HOLD_DE 안이면 앞 장 색을 그대로 쓴다
     — 장마다 따로 고르면 움직이지 않는 곳도 블록 색이 바뀌어 몸이 꿈틀거려 보인다.
  7. --env 배경.png: 팔레트 색을 이끼(초록)·껍질(갈색)·어두운 선으로 나눠, 배경 그림(숲 참나무)의 같은 무리 색으로
     밝기 순위(분위수)를 맞춰 바꾼다 — 캐릭터 같은 살구색 몸·검은 선 대신 숲 나무와 같은 색. 눈은 그대로.
     --mix 0~1로 원래 색과 섞는 정도, 밝기는 ENV_LIGHT배(앞줄 나무 밝기).
  8. --breathe: 첫 시트(대기)를 첫 장에서 다시 만든다 — 허리 위만 BREATH 블록만큼 내렸다 올린다(발·아랫몸은 고정, 다시 그리기 없음).
     통째로 늘였다 줄인 대기는 장마다 블록이 다시 나뉘어 온몸이 일렁였다.

사용법: python Tools/pixelize_sheet.py --block B --colors N [--env 배경.png --mix M] [--breathe] 입력.png 결과.png [입력.png 결과.png ...]
  한 번에 넘긴 시트들이 팔레트를 함께 쓴다 — 한 몬스터의 시트는 모두 같이 넘긴다.
필요: numpy, scipy, Pillow
"""
import sys
import warnings

import numpy as np
from PIL import Image
from scipy.cluster.vq import kmeans2

CELL = 544
OPAQUE = 0.5      # 블록 평균 알파가 이 이상이면 불투명
DARK_LUMA = 70    # 가장자리 블록이 이보다 밝으면 셀아웃
SELOUT = 0.5      # 셀아웃 명도 배율(Lab L)
OUTLINE = np.array([28.0, 16.0, 10.0])  # 팔레트에 꼭 남기는 외곽선 색(거의 검은 갈색)
EYES = np.array([[250.0, 215.0, 50.0], [230.0, 40.0, 30.0]])  # 노란 눈·붉은 눈 — 드물어서 k-means가 다른 색에 묻는다
LINE_LUMA = 75    # 이보다 어두운 픽셀은 선(외곽선·주름)
LINE_SHARE = 0.2  # 블록의 불투명 픽셀 중 선이 이만큼이면 블록을 선 색으로(안쪽 윤곽선이 끊기지 않게)
EYE_SHARE = 0.12  # 눈(밝은 노랑·빨강) 픽셀이 이만큼이면 눈 색으로
SPLIT = 30        # 블록 안 밝기 차가 이보다 작으면 한 무리로 본다
SAMPLES = 250000  # 팔레트를 고를 때 쓰는 블록 수(넘으면 고정 시드로 뽑는다)
HOLD_DE = 10.0    # 블록 원래 색이 앞 장 팔레트 색과 이만큼(Lab ΔE) 안이면 앞 장 색 유지
ENV_LIGHT = 0.94  # --env로 바꾼 색 밝기(앞줄 나무 밝기 TreeMinLight 0.88~1의 가운데)
ENV_DARK = 45     # 이보다 어두운 색은 선 무리
BREATH = [0, 0, 1, 2, 2, 2, 1, 0]  # 대기 장마다 허리 위를 내리는 블록 수
WAIST = 0.55      # 발에서 몸 키의 이 비율 위가 허리(그 위만 숨 쉰다)
LUMA = np.array([0.299, 0.587, 0.114])


def lab(rgb):
    """sRGB(0~255) → CIE Lab(D65)."""
    c = np.asarray(rgb, np.float64) / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    xyz = c @ np.array([[0.4124, 0.2126, 0.0193], [0.3576, 0.7152, 0.1192], [0.1805, 0.0722, 0.9505]])
    xyz = xyz / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16.0 / 116.0)
    return np.stack([116.0 * f[..., 1] - 16.0, 500.0 * (f[..., 0] - f[..., 1]), 200.0 * (f[..., 1] - f[..., 2])], -1)


def blocks_of(sheet, b):
    """시트 → (블록 색 RGB, 블록 불투명 여부, 블록 역할 0/1선/2눈) 격자. 칸마다 원점부터 b 간격(칸 끝 남는 부분은 작은 블록)."""
    h, w = sheet.shape[:2]
    rows, cols = h // CELL, w // CELL
    n = -(-CELL // b)  # 칸 하나의 블록 수(올림)
    rgb = np.zeros((rows * n, cols * n, 3), np.float64)
    solid = np.zeros((rows * n, cols * n), bool)
    role = np.zeros((rows * n, cols * n), np.uint8)
    area = np.full((n, n), float(b * b))
    area[-1, :] = area[-1, :] / b * (CELL - (n - 1) * b)  # 칸 끝의 작은 블록은 실제 넓이로
    area[:, -1] = area[:, -1] / b * (CELL - (n - 1) * b)
    for r in range(rows):
        for c in range(cols):
            pad = np.zeros((n * b, n * b, 4))
            pad[:CELL, :CELL] = sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
            px = pad.reshape(n, b, n, b, 4).transpose(0, 2, 1, 3, 4).reshape(n, n, b * b, 4)  # 블록별 픽셀
            col = px[..., :3]
            opaque = px[..., 3] > 127
            luma = col @ LUMA
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", RuntimeWarning)  # 빈 블록의 nan 경고
                lum = np.where(opaque, luma, np.nan)
                lo, hi = np.nanmin(lum, axis=2), np.nanmax(lum, axis=2)
                light = opaque & (luma >= ((lo + hi) / 2)[..., None])
                dark = opaque & ~light
                pick = np.where((light.sum(2) >= dark.sum(2))[..., None], light, dark)
                pick = np.where(((hi - lo) < SPLIT)[..., None], opaque, pick)
                mean = np.nanmedian(np.where(pick[..., None], col, np.nan), axis=2)
                # 선·눈은 작아서 대표색에서 빠진다: 블록에서 그 비중이 크면 그 픽셀들의 색을 쓴다(눈이 선보다 우선)
                line = opaque & (luma < LINE_LUMA)
                eye = opaque & (col[..., 0] > 200) & (col[..., 2] < 90) & ((col[..., 1] > 170) | (col[..., 1] < 70))
                cnt = np.maximum(opaque.sum(2), 1)
                rl = np.zeros((n, n), np.uint8)
                for k, (mask, share) in enumerate(((line, LINE_SHARE), (eye, EYE_SHARE)), start=1):
                    use = mask.sum(2) / cnt >= share
                    mean[use] = np.nanmedian(np.where(mask[..., None], col, np.nan), axis=2)[use]
                    rl[use] = k
            asum = px[..., 3].sum(2) / 255.0
            s = asum / area >= OPAQUE
            rgb[r * n:(r + 1) * n, c * n:(c + 1) * n] = np.nan_to_num(mean)
            solid[r * n:(r + 1) * n, c * n:(c + 1) * n] = s
            role[r * n:(r + 1) * n, c * n:(c + 1) * n] = np.where(s, rl, 0)
    return rgb, solid, role, n


def palette(samples, colors):
    """공용 팔레트(RGB, Lab): Lab k-means N색 + 꼭 남겨야 할 색. 색은 각 무리 RGB 평균."""
    rng = np.random.default_rng(7)
    if len(samples) > SAMPLES:
        samples = samples[rng.choice(len(samples), SAMPLES, replace=False)]
    sl = lab(samples)
    centers, labels = kmeans2(sl, colors, minit="++", seed=7)
    pal = [samples[labels == k].mean(axis=0) for k in range(colors) if (labels == k).any()]
    pal = np.vstack([np.array(pal), OUTLINE, EYES])
    return pal, lab(pal)


def groups(rgb):
    """색 → 무리(0 어두운 선, 1 이끼·잎 초록, 2 껍질·줄기)."""
    rgb = np.asarray(rgb, np.float64)
    mx, mn = rgb.max(-1), rgb.min(-1)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    hue = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    sat = (mx - mn) / np.maximum(mx, 1e-6)
    green = (hue >= 55) & (hue <= 170) & (sat > 0.2)
    return np.where(rgb @ LUMA < ENV_DARK, 0, np.where(green, 1, 2))


def env_colors(pal, samples, env_path, mix):
    """팔레트 → 배경 그림 색. 무리마다 팔레트 색의 밝기 분위수(쓰인 블록 수로 가중)를 배경 같은 무리의 같은 분위수 색으로."""
    env = np.array(Image.open(env_path).convert("RGBA")).reshape(-1, 4)
    env = env[env[:, 3] > 127, :3].astype(np.float64)
    env_g = groups(env)
    used = np.bincount(nearest(lab(samples), lab(pal)), minlength=len(pal)).astype(np.float64)
    pal_g = groups(pal)
    out = pal.copy()
    keep = len(pal) - len(EYES)  # 눈 색은 그대로
    for k in range(3):
        src = np.nonzero((pal_g[:keep] == k))[0]
        ref = env[env_g == k]
        if len(src) == 0 or len(ref) == 0:
            continue
        ref = ref[np.argsort(ref @ LUMA)]
        order = src[np.argsort(pal[src] @ LUMA)]
        w = used[order] + 1.0
        q = (np.cumsum(w) - w / 2) / w.sum()  # 무리 안 밝기 분위수
        for i, qi in zip(order, q):
            lo, hi = int(max(qi - 0.03, 0) * len(ref)), int(min(qi + 0.03, 1) * len(ref))
            out[i] = np.median(ref[lo:max(hi, lo + 1)], axis=0)
    out[:keep] = ((1 - mix) * pal[:keep] + mix * out[:keep]) * ENV_LIGHT
    return out


def nearest(colors_lab, pal_lab):
    """Lab 색들 → 가장 가까운 팔레트 번호."""
    flat = colors_lab.reshape(-1, 3)
    out = np.empty(len(flat), np.int64)
    for i in range(0, len(flat), 200000):
        part = flat[i:i + 200000]
        out[i:i + 200000] = np.argmin(((part[:, None, :] - pal_lab[None, :, :]) ** 2).sum(2), axis=1)
    return out.reshape(colors_lab.shape[:-1])


def neighbours(a, fill):
    """상하좌우 이웃 4장(가장자리 밖은 fill)."""
    p = np.pad(a, 1, constant_values=fill)
    return np.stack([p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]])


def clean_cell(idx, solid, role, pal, pal_lab):
    """칸 하나의 블록 격자 정리: 구멍·티끌, 외톨이 색, 셀아웃. idx·solid를 고쳐 돌려준다."""
    ns = neighbours(solid, False)
    ni = neighbours(np.where(solid, idx, -1), -1)  # 투명 이웃은 -1
    # 1블록 구멍은 이웃 색으로 메우고 1블록 티끌은 지운다
    hole = ~solid & ns.all(0)
    speck = solid & ~ns.any(0) & (role != 2)
    solid = (solid | hole) & ~speck
    # 외톨이: 상하좌우 넷 중 셋 이상이 같은 색인데 혼자 다른 블록(선·눈은 그대로)
    best = np.full(idx.shape, -1)
    for k in range(4):
        same = (ni == ni[k][None]).sum(0)
        best = np.where((same >= 3) & (ni[k] >= 0) & (best < 0), ni[k], best)
    fix = (best >= 0) & (best != idx) & (role == 0) & solid
    fix |= hole & (best >= 0)
    idx = np.where(fix, best, idx)
    hole_left = hole & (best < 0)  # 이웃 색이 갈려 못 정한 구멍은 첫 이웃 색
    idx = np.where(hole_left, ni[0], idx)
    # 셀아웃: 가장자리 블록 중 밝은 것은 제 색을 어둡게 해 팔레트에 맞춘다
    ns = neighbours(solid, False)
    edge = solid & ~ns.all(0) & (role == 0)
    luma = pal[idx] @ LUMA
    dim = edge & (luma > DARK_LUMA)
    if dim.any():
        dark = pal_lab[idx[dim]] * np.array([SELOUT, 1.0, 1.0])
        idx[dim] = nearest(dark, pal_lab)
    return idx, solid


def render(rgb, solid, role, pal, pal_lab, out_pal, n, b, shape):
    """블록 격자 → 팔레트 색으로 바꾸고(앞 장과 비슷하면 앞 장 색) 정리 → 원래 크기 시트(색은 out_pal)."""
    src_lab = lab(rgb)
    idx_all = nearest(src_lab, pal_lab)
    # 눈 블록은 눈 색 중에서만 고른다 — 눈 가까운 k-means 색은 --env에서 갈색이 되어 눈이 사라진다
    eyes = role == 2
    first_eye = len(pal) - len(EYES)
    idx_all[eyes] = first_eye + nearest(src_lab[eyes], pal_lab[first_eye:])
    h, w = shape
    rows, cols = h // CELL, w // CELL
    sheet = np.zeros((h, w, 4), np.uint8)
    prev = None
    for r in range(rows):
        for c in range(cols):
            sl = (slice(r * n, (r + 1) * n), slice(c * n, (c + 1) * n))
            idx = idx_all[sl].copy()
            if prev is not None:
                p_idx, p_s = prev
                close = ((src_lab[sl] - pal_lab[p_idx]) ** 2).sum(-1) < HOLD_DE ** 2
                hold = close & p_s & solid[sl] & (role[sl] == 0)
                idx[hold] = p_idx[hold]
            idx, s = clean_cell(idx, solid[sl], role[sl], pal, pal_lab)
            if s.any():
                prev = (idx, s)
            big_i = np.repeat(np.repeat(idx, b, axis=0), b, axis=1)[:CELL, :CELL]
            big_s = np.repeat(np.repeat(s, b, axis=0), b, axis=1)[:CELL, :CELL]
            cell = np.zeros((CELL, CELL, 4), np.uint8)
            cell[..., :3] = np.clip(np.round(out_pal[big_i]), 0, 255).astype(np.uint8)
            cell[..., 3] = np.where(big_s, 255, 0)
            cell[~big_s, :3] = 0
            sheet[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = cell
    return sheet


def breathe(sheet, b):
    """대기 시트를 첫 장에서 다시 만든다: 장마다 허리 위만 BREATH 블록만큼 내린다(아래와 겹치는 곳은 윗몸이 덮는다)."""
    first = sheet[:CELL, :CELL].copy()
    ys = np.nonzero((first[..., 3] > 0).any(axis=1))[0]
    waist = ys.max() - int((ys.max() - ys.min()) * WAIST)
    waist -= waist % b  # 블록 경계에서 자른다
    out = np.zeros_like(sheet)
    rows, cols = sheet.shape[0] // CELL, sheet.shape[1] // CELL
    for k in range(rows * cols):
        d = BREATH[k % len(BREATH)] * b
        cell = np.zeros_like(first)
        cell[waist:] = first[waist:]
        upper = first[:waist]
        dst = cell[d:waist + d]
        top = upper[..., 3] > 0
        dst[top] = upper[top]
        r, c = divmod(k, cols)
        out[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL] = cell
    return out


def pixelize(pairs, b, colors, env=None, mix=1.0, breathe_first=False):
    """pairs = [(입력, 결과)]. 공용 팔레트로 모두 변환해 저장(결과가 None이면 저장 안 함)하고, 결과 배열들을 돌려준다."""
    data = []
    for src, _ in pairs:
        sheet = np.array(Image.open(src).convert("RGBA"))
        data.append((sheet.shape[:2],) + blocks_of(sheet, b))
    samples = np.concatenate([rgb[solid] for _, rgb, solid, _, _ in data])
    pal, pal_lab = palette(samples, colors)
    out_pal = env_colors(pal, samples, env, mix) if env else pal
    outs = []
    for k, ((shape, rgb, solid, role, n), (_, dst)) in enumerate(zip(data, pairs)):
        out = render(rgb, solid, role, pal, pal_lab, out_pal, n, b, shape)
        if k == 0 and breathe_first:
            out = breathe(out, b)
        if dst:
            Image.fromarray(out).save(dst)
        outs.append(out)
    return outs, pal


def main(args):
    b, colors = 4, 64
    if "--block" in args:
        i = args.index("--block")
        b = int(args[i + 1])
        args = args[:i] + args[i + 2:]
    if "--colors" in args:
        i = args.index("--colors")
        colors = int(args[i + 1])
        args = args[:i] + args[i + 2:]
    env, mix = None, 1.0
    if "--env" in args:
        i = args.index("--env")
        env = args[i + 1]
        args = args[:i] + args[i + 2:]
    if "--mix" in args:
        i = args.index("--mix")
        mix = float(args[i + 1])
        args = args[:i] + args[i + 2:]
    breathe_first = "--breathe" in args
    if breathe_first:
        args.remove("--breathe")
    if len(args) < 2 or len(args) % 2:
        sys.exit(__doc__)
    pairs = [(args[i], args[i + 1]) for i in range(0, len(args), 2)]
    _, pal = pixelize(pairs, b, colors, env, mix, breathe_first)
    print(f"블록 {b}px · 팔레트 {len(pal)}색 → {len(pairs)}장")


if __name__ == "__main__":
    main(sys.argv[1:])
