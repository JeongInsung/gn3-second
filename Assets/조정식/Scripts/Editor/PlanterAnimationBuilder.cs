using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// "바람에 살랑이는 나무 꽃화분" 8프레임 시트(4열×2행)를 DesignScene의 꽃화분 전부에 애니메이션으로 붙인다(공통 처리: SheetAnimationBuilder).
    /// 상자 바닥이 땅에 붙어야 하므로 밑동(아래 중앙) 기준으로 맞추고, 프레임마다 위로 떠다니는 꽃잎이 정렬을 흔들지 않게
    /// 아래쪽 몸체(상자+잎)로 가로 중심을 잡는다. 나무 상자는 8프레임 중앙값으로 고정해 잎·꽃·꽃잎만 움직이게 한다.
    /// </summary>
    public static class PlanterAnimationBuilder
    {
        private const float BoxZoneHeight = 0.45f; // 프레임 아래에서 이 비율 안의 나무 상자(갈색)만 고정

        [MenuItem("GN3/Pixel/꽃화분 바람 애니메이션 만들기")]
        public static void Build()
        {
            SheetAnimationBuilder.Build(new SheetAnimationSettings
            {
                LogTag = "PlanterAnimation",
                SheetPath = "Assets/조정식/Animations/Planter/바람에 살랑이는 나무 꽃화분.png",
                AnimationFolder = "Assets/조정식/Animations/Planter",
                OutputName = "꽃화분 바람",
                ClipName = "PlanterSway",
                ControllerName = "Planter",
                TargetKeyword = "꽃이 핀 나무 화분",
                // 참나무처럼 프레임마다 다시 그려진 잎이 반짝여 보이지 않게 느리게.
                FramesPerSecond = 2f,
                Anchor = SheetAnchor.BottomCenter,
                AlignBottomRatio = 0.6f,
                PostProcess = LockBox,
                ApplyToAll = true,
            });
        }

        /// <summary>고정 이미지(중앙값)에서 아래쪽 띠의 갈색(나무 상자) 픽셀만 골라 모든 프레임에 덮어쓴다.</summary>
        private static void LockBox(Color[][] frames, int width, int height)
        {
            var still = SheetAnimationBuilder.Median(frames, width, height);
            int zoneTop = Mathf.RoundToInt(height * BoxZoneHeight);

            for (int y = 0; y < zoneTop; y++)
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                var c = still[p];
                bool wood = c.a >= 0.5f && c.r > c.g * 1.05f && c.r > c.b * 1.3f; // 갈색(빨강 > 초록 > 파랑)
                bool flower = c.r > 0.85f && c.b < 0.1f; // 상자 위로 걸친 주황·노랑 꽃(실측 r 240+, b 0~11)은 흔들리게 둔다
                if (!wood || flower) continue;
                foreach (var frame in frames) frame[p] = c;
            }
        }
    }
}
