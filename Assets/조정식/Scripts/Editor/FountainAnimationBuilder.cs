using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// 4열×2행(8프레임) 분수 루프 시트를 DesignScene 광장 분수에 애니메이션으로 붙인다(공통 처리: SheetAnimationBuilder).
    /// 분수는 제자리에서 움직이므로 프레임을 불투명 영역 중심으로 맞추고, 올라오는 물만 움직이게 몸통을 고정한다.
    /// </summary>
    public static class FountainAnimationBuilder
    {
        private const float WaterAreaRadius = 0.8f; // 프레임 반지름 대비 물보라를 허용할 안쪽 타원(돌 테두리 제외)

        [MenuItem("GN3/Pixel/분수 애니메이션 만들기")]
        public static void Build()
        {
            SheetAnimationBuilder.Build(new SheetAnimationSettings
            {
                LogTag = "FountainAnimation",
                SheetPath = "Assets/조정식/Animations/Fountain/흐르는 물의 픽셀 돌분수 루프.png",
                AnimationFolder = "Assets/조정식/Animations/Fountain",
                OutputName = "분수 루프",
                ClipName = "FountainLoop",
                ControllerName = "Fountain",
                TargetKeyword = "분수",
                FramesPerSecond = 8f,
                Anchor = SheetAnchor.Center,
                PostProcess = KeepOnlyWaterMoving,
            });
        }

        /// <summary>
        /// AI 시트는 프레임마다 분수를 통째로 다시 그려서 돌 테두리·기둥·물결 도트까지 꿈틀거렸다.
        /// 8프레임의 픽셀별 중앙값으로 고정 몸통을 만들고, 각 프레임에서는 분수 안쪽의 밝은 물보라(하늘색·흰색)
        /// 중 몸통과 확실히 다른 픽셀만 덮어써서 올라오는 물만 움직이게 한다.
        /// </summary>
        private static void KeepOnlyWaterMoving(Color[][] frames, int width, int height)
        {
            var body = SheetAnimationBuilder.Median(frames, width, height);
            float centerX = width / 2f, centerY = height / 2f;
            float radiusX = width * 0.5f * WaterAreaRadius, radiusY = height * 0.5f * WaterAreaRadius;
            for (int i = 0; i < frames.Length; i++)
            {
                var result = (Color[])body.Clone();
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float ex = (x - centerX) / radiusX, ey = (y - centerY) / radiusY;
                    if (ex * ex + ey * ey >= 1f) continue; // 돌 테두리 바깥쪽은 항상 몸통

                    int p = y * width + x;
                    var pixel = frames[i][p];
                    bool spray = pixel.a >= 0.5f && pixel.r > 0.5f && pixel.g > 0.75f && pixel.b > 0.85f;
                    float diff = Mathf.Abs(pixel.r - body[p].r) + Mathf.Abs(pixel.g - body[p].g) + Mathf.Abs(pixel.b - body[p].b);
                    if (spray && diff > 0.2f) result[p] = pixel;
                }
                frames[i] = result;
            }
        }
    }
}
