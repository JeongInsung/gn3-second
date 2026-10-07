using UnityEngine;

namespace GN3.Battle
{
    /// <summary>
    /// 몬스터 그림이 아직 없는 동안 쓰는 기본 도형(원) 스프라이트. 나중에 몬스터 그림이 생기면
    /// BattleUIController에서 이 부분만 교체하면 된다(계층/이름/색은 손댈 필요 없음).
    /// UITheme.Make()와 같은 방식으로 텍스처를 코드로 한 번만 그려 캐싱한다.
    /// </summary>
    public static class BattlePlaceholderSprites
    {
        // 계층(약탈 세력/지형 동물/보스)별 색: 사람 바탕 갈색 -> 짐승 초록 -> 보스 진홍.
        public static readonly Color[] TierColors =
        {
            new Color(0.55f, 0.42f, 0.28f),
            new Color(0.32f, 0.5f, 0.3f),
            new Color(0.55f, 0.18f, 0.18f),
        };

        private static Sprite _circle;
        public static Sprite Circle => _circle != null ? _circle : _circle = BuildCircle();

        public static Color ColorForTier(int tier) => TierColors[Mathf.Clamp(tier, 0, TierColors.Length - 1)];

        /// <summary>보스(계층 2)는 같은 원을 더 크게 그려서 구분한다.</summary>
        public static float SizeForTier(int tier) => tier >= 2 ? 1.35f : 1f;

        private static Sprite BuildCircle()
        {
            const int size = 64;
            const float radius = size / 2f - 1f;
            var center = new Vector2(size / 2f, size / 2f);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                Color c;
                if (dist > radius + 1f) c = new Color(0, 0, 0, 0);
                else if (dist > radius - 1.5f) c = new Color(0.05f, 0.04f, 0.03f, 1f); // 바깥 테두리
                else c = Color.white; // Image.color로 계층별 색을 입힌다
                pixels[y * size + x] = c;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BattlePlaceholderCircle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "BattlePlaceholderCircle";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
