using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 꽃잎·새·나비처럼 아주 작은 그림을 외부 이미지 없이 글자 도트맵으로 만든다.
    /// 마을 그림은 1080p 화면 기준으로 구웠으므로(PixelBaker) 도트 크기도 같은 기준의 화면 px 배수로 맞춘다.
    /// </summary>
    public static class PixelSprites
    {
        private const float ReferenceScreenHeight = 1080f;

        /// <summary>1080p 화면 1px의 월드 크기.</summary>
        public static float ScreenPixel(Camera cam) => cam.orthographicSize * 2f / ReferenceScreenHeight;

        /// <summary>마을 그림(AI 픽셀아트를 구운 것)의 도트 하나 ≈ 화면 2px. 3px은 꽃잎·새·나비가 너무 커 보였다(나비는 어두운 날개 테두리로 2px에서도 보이게 함).</summary>
        public static float ArtDot(Camera cam) => ScreenPixel(cam) * 2f;

        /// <summary>카메라에 지금 보이는 월드 사각형.</summary>
        public static Rect ViewRect(Camera cam)
        {
            float halfHeight = cam.orthographicSize, halfWidth = halfHeight * cam.aspect;
            var center = cam.transform.position;
            return new Rect(center.x - halfWidth, center.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        }

        /// <summary>
        /// 도트맵 → 텍스처. 행은 위에서 아래 순서, '.'은 투명, 그 밖의 글자는 keys에서 찾아 같은 자리의 colors를 칠한다.
        /// </summary>
        public static Texture2D Texture(string[] rows, string keys, Color[] colors)
        {
            int height = rows.Length, width = rows[0].Length;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int key = keys.IndexOf(rows[height - 1 - y][x]);
                pixels[y * width + x] = key < 0 ? Color.clear : colors[key];
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>프레임마다 도트맵 하나씩. 도트 하나 = worldPerDot 유닛, 피벗은 가운데.</summary>
        public static Sprite[] Frames(string name, string[][] frames, string keys, Color[] colors, float worldPerDot)
        {
            var sprites = new Sprite[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                var texture = Texture(frames[i], keys, colors);
                sprites[i] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f / worldPerDot);
                sprites[i].name = $"{name}_{i}";
            }
            return sprites;
        }
    }
}
