using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// "미풍에 살짝 흔들리는 참나무" 8프레임 시트(4열×2행)를 DesignScene의 초록 참나무에 애니메이션으로 붙인다(공통 처리: SheetAnimationBuilder).
    /// 흔들리면 잎 쪽 폭이 프레임마다 바뀌므로 밑동(아래 중앙)으로 맞춰 뿌리가 땅에서 미끄러지지 않게 하고,
    /// 아래쪽 줄기·밑동 띠는 8프레임 중앙값으로 고정해 잎만 흔들리게 한다.
    /// </summary>
    public static class TreeAnimationBuilder
    {
        private const float TrunkZoneHeight = 0.32f; // 프레임 아래에서 이 비율 안의 줄기(갈색)만 고정
        private const float TrunkZoneWidth = 0.4f;   // 가운데 이 폭 안만(양옆 잎 끝은 건드리지 않는다)

        [MenuItem("GN3/Pixel/참나무 바람 애니메이션 만들기")]
        public static void Build()
        {
            SheetAnimationBuilder.Build(new SheetAnimationSettings
            {
                LogTag = "TreeAnimation",
                SheetPath = "Assets/조정식/Animations/OakTree/미풍에 살짝 흔들리는 참나무 픽셀 스프라이트.png",
                AnimationFolder = "Assets/조정식/Animations/OakTree",
                OutputName = "참나무 바람",
                ClipName = "OakTreeSway",
                ControllerName = "OakTree",
                TargetKeyword = "풍성한 오크 나무",
                // 프레임당 약 0.67초(한 바퀴 5.3초). 6fps·3fps는 너무 빨리 넘어가 잎(프레임마다 다시 그려진 무늬)이 반짝여 보였다.
                FramesPerSecond = 1.5f,
                Anchor = SheetAnchor.BottomCenter,
                PostProcess = LockTrunk,
            });
        }

        /// <summary>
        /// 아래 띠를 통째로 고정하면 띠 경계에서 잎 아랫자락이 잘려 줄이 보였다. 그래서 고정 이미지(중앙값)에서
        /// 밑동 근처의 갈색(줄기) 픽셀과, 줄기 아래 땅에 닿는 부분만 골라 모든 프레임에 덮어쓴다.
        /// </summary>
        private static void LockTrunk(Color[][] frames, int width, int height)
        {
            var still = SheetAnimationBuilder.Median(frames, width, height);
            int zoneTop = Mathf.RoundToInt(height * TrunkZoneHeight);
            int zoneLeft = Mathf.RoundToInt(width * (0.5f - TrunkZoneWidth / 2f));
            int zoneRight = Mathf.RoundToInt(width * (0.5f + TrunkZoneWidth / 2f));

            for (int y = 0; y < zoneTop; y++)
            for (int x = zoneLeft; x < zoneRight; x++)
            {
                int p = y * width + x;
                var c = still[p];
                bool trunk = c.a >= 0.5f && c.r > c.g * 1.05f && c.r > c.b * 1.3f; // 갈색(빨강 > 초록 > 파랑)
                if (!trunk) continue;
                foreach (var frame in frames) frame[p] = c;
            }
        }
    }
}
