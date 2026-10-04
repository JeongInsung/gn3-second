using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// "연기 피어오르는 픽셀 대장간" 8프레임 시트(4열×2행)를 씬의 대장간에 애니메이션으로 붙인다(공통 처리: SheetAnimationBuilder).
    /// 연기는 프레임마다 위로 다르게 뻗어 전체 bbox로 맞추면 건물이 좌우로 흔들려서, 아래쪽 몸체만으로 정렬한다.
    /// AI 시트라 프레임마다 건물 도트도 다시 그려져 있어, 건물은 8프레임 중앙값으로 고정하고 굴뚝 위 연기만 움직인다.
    /// </summary>
    public static class BlacksmithAnimationBuilder
    {
        private const float SmokeZoneLeft = 0.55f;   // 굴뚝은 그림 오른쪽에 있다

        [MenuItem("GN3/Pixel/대장간 연기 애니메이션 만들기")]
        public static void Build()
        {
            SheetAnimationBuilder.Build(new SheetAnimationSettings
            {
                LogTag = "BlacksmithAnimation",
                SheetPath = "Assets/조정식/Animations/Blacksmith/연기 피어오르는 픽셀 대장간.png",
                AnimationFolder = "Assets/조정식/Animations/Blacksmith",
                OutputName = "대장간 연기",
                ClipName = "BlacksmithSmoke",
                ControllerName = "Blacksmith",
                TargetKeyword = "대장간",
                FramesPerSecond = 6f, // 연기가 천천히 피어오르게
                Anchor = SheetAnchor.BottomCenter,
                AlignBottomRatio = 0.6f,
                PostProcess = KeepOnlySmokeMoving,
            });
        }

        /// <summary>
        /// 연기만 움직인다. 처음엔 굴뚝 근처 회색·화덕 불 픽셀도 움직이게 했더니 굴뚝 돌과 불 주변 도트까지 섞여
        /// 프레임마다 약 300px씩 건물이 일렁였다. 그래서 굴뚝 꼭대기 선을 기준으로 아래는 고정 건물(중앙값)만,
        /// 위(연기가 피어오르는 하늘)는 각 프레임 그대로 쓴다.
        /// </summary>
        private static void KeepOnlySmokeMoving(Color[][] frames, int width, int height)
        {
            var body = SheetAnimationBuilder.Median(frames, width, height);
            int chimneyTop = ChimneyTopRow(frames, body, width, height);
            for (int i = 0; i < frames.Length; i++)
            {
                var result = (Color[])body.Clone();
                for (int y = chimneyTop + 1; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int p = y * width + x;
                    result[p] = frames[i][p];
                }
                frames[i] = result;
            }
        }

        /// <summary>
        /// 굴뚝 꼭대기 줄(텍스처 0행 = 맨 아래). 색으로 고르면 굴뚝 바로 위 연기(살짝 색이 섞인 회색)를 건물로 착각해
        /// 선이 너무 높게 잡혔다. 그래서 "8프레임 모두 불투명하고 색이 거의 같은 따뜻한 색" 픽셀(= 연통)이
        /// 굴뚝 구역에 몇 개 이상 있는 가장 높은 줄을 쓴다.
        /// </summary>
        internal static int ChimneyTopRow(Color[][] frames, Color[] body, int width, int height)
        {
            const int MinStablePixels = 3;
            for (int y = height - 1; y >= 0; y--)
            {
                int stable = 0;
                for (int x = Mathf.RoundToInt(width * SmokeZoneLeft); x < width; x++)
                {
                    int p = y * width + x;
                    if (IsStable(frames, body, p) && IsWarm(body[p])) stable++;
                }
                if (stable >= MinStablePixels) return y;
            }
            return height - 1;
        }

        /// <summary>
        /// 굴뚝 꼭대기의 붉은 갈색 연통. 굴뚝 바로 위 연기는 8프레임 모두에 비슷하게 있어 "안정적"으로 걸렸으므로,
        /// 회색 연기에는 절대 없는 따뜻한 색(빨강이 파랑보다 확실히 큼)까지 요구한다.
        /// </summary>
        private static bool IsWarm(Color c) => c.r > c.b + 0.08f && c.r > 0.2f;

        private static bool IsStable(Color[][] frames, Color[] body, int p)
        {
            if (body[p].a < 0.5f) return false;
            foreach (var frame in frames)
            {
                var c = frame[p];
                if (c.a < 0.5f) return false;
                if (Mathf.Abs(c.r - body[p].r) + Mathf.Abs(c.g - body[p].g) + Mathf.Abs(c.b - body[p].b) > 0.35f) return false;
            }
            return true;
        }
    }
}
