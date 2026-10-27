"""전투씬 바닥 꾸미기 그림 굽기: 마을 그림(Assets/조정식/Decorations)을 전투 화면 크기에 맞게 줄여 Resources로 옮긴다.

원본은 512px 정도로 크고 사방에 투명 여백이 있어서, 여백을 잘라낸 뒤 표시 높이의 2배로 줄인다(BOX 리샘플:
픽셀 그림을 줄일 때 색이 번지지 않고 고르게 섞인다). Unity 쪽(BattleDecor)은 이 그림을 절반 크기로 그린다.

사용법: python Tools/bake_battle_decor.py   (프로젝트 루트 어디서 실행해도 된다)
필요: numpy, Pillow
"""
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Assets" / "조정식" / "Decorations"
DST = ROOT / "Assets" / "_Project" / "Resources" / "Battle" / "Decor"
ALPHA_MIN = 16  # 이보다 투명한 픽셀은 여백으로 본다

# 결과 이름: (원본 파일, 굽는 높이 px)
ITEMS = {f"flower_{i:02d}": (f"꽃무더기_{i:02d}.png", 72) for i in range(1, 13)}
ITEMS.update({
    "wildflowers": ("흰 데이지와 주황 야생화 묶음-8.png", 88),
    "stone": ("이끼 낀 회색 돌 스프라이트-7.png", 72),
    "grass": ("픽셀 아트 야생 풀 한 포기-6.png", 72),
    # 판 둘레 숲(BattleGroundTiles): 마을 숲 테두리와 같은 나무·덤불·바위
    "pine": ("중세 판타지 소나무 스프라이트-2.png", 480),
    "autumn": ("황금빛 가을나무 픽셀 스프라이트-3.png", 420),
    "bush": ("둥근 녹색 관목 스프라이트-5.png", 180),
    "thicket": ("모스빛 숲덤불 세 덩이-4.png", 240),
    "rocks": ("이끼 낀 세 바위 무리-5.png", 180),
})


def bake(name, src_name, height):
    image = Image.open(SRC / src_name).convert("RGBA")
    alpha = np.array(image)[:, :, 3]
    ys, xs = np.nonzero(alpha > ALPHA_MIN)
    image = image.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))
    width = max(1, round(image.width * height / image.height))
    image = image.resize((width, height), Image.BOX)
    image.save(DST / f"{name}.png")
    print(f"{name}: {src_name} → {width}x{height}")


if __name__ == "__main__":
    DST.mkdir(parents=True, exist_ok=True)
    for name, (src_name, height) in ITEMS.items():
        bake(name, src_name, height)
