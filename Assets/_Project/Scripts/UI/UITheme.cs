using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// MainScene UI의 다크 판타지 테마: 숯빛 판 + 청동 베벨 테두리 + 숯빛 버튼(알림창 판과 같은 색) + 호박색 제목.
    /// 그림 파일 없이 코드로 9-slice 스프라이트를 그려(처음 한 번, 캐시) 해상도와 상관없이 테두리가 늘어지지 않는다.
    /// 어떤 UI에 무엇을 입힐지는 UIThemeApplier가 정한다.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color PanelFill = Hex(0x1c1916);
        public static readonly Color InsetFill = Hex(0x120f0d);
        public static readonly Color BronzeLight = Hex(0xb0834f);
        public static readonly Color BronzeDark = Hex(0x4a3220);
        public static readonly Color TitleText = Hex(0xf0a54a);
        public static readonly Color ButtonTop = Hex(0x2e2924);         // 버튼 바탕 = 알림창 판(PanelFill) 숯빛, 위만 살짝 밝게
        public static readonly Color ButtonBottom = PanelFill;
        public static readonly Color BodyText = Hex(0xe6d8bd);
        public static readonly Color MutedText = Hex(0xa3937c);
        private static readonly Color Outline = new Color(0.03f, 0.02f, 0.02f, 1f);

        private static Sprite _panel, _button, _inset;

        public static Sprite Panel => _panel != null ? _panel : _panel = BuildPanel();
        public static Sprite Button => _button != null ? _button : _button = BuildButton(ButtonTop, ButtonBottom, "ThemeButton");
        public static Sprite Inset => _inset != null ? _inset : _inset = BuildInset();

        /// <summary>판: 검은 외곽선 1px → 청동 베벨 5px(위·왼쪽 밝게) → 어두운 선 1px → 비네트 숯빛 안쪽. 모서리에 사선 장식.</summary>
        private static Sprite BuildPanel()
        {
            const int size = 64, bevel = 5;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                Color c;
                if (edge == 0) c = Outline;
                else if (edge <= bevel) c = Bevel(x, y, size, size, edge, bevel);
                else if (edge == bevel + 1) c = Outline;
                else
                {
                    // 가장자리 쪽이 살짝 어두운 비네트(안쪽 9-slice 가운데는 균일하게 늘어난다)
                    float t = Mathf.Clamp01((edge - bevel - 2) / 9f);
                    c = Color.Lerp(PanelFill * 0.75f, PanelFill, t);
                    c.a = 1f;
                }
                pixels[y * size + x] = c;
            }
            // 네 모서리 사선 장식(청동 밝은 점 3개)
            for (int i = 0; i < 3; i++)
            {
                int o = bevel + 3 + i;
                Put(pixels, size, o, o, BronzeLight);
                Put(pixels, size, size - 1 - o, o, BronzeLight);
                Put(pixels, size, o, size - 1 - o, BronzeLight);
                Put(pixels, size, size - 1 - o, size - 1 - o, BronzeLight);
            }
            return Make(pixels, size, size, 16, "ThemePanel");
        }

        /// <summary>
        /// 버튼: 검은 외곽선 1px → 청동 베벨 3px → 어두운 선 1px → 세로 그라데이션 안쪽.
        /// 안쪽 위 40%에 광택 띠, 맨 위 1px 하이라이트, 맨 아래 1px 안 그림자. 네 모서리 베벨에 청동 리벳.
        /// 장식은 모두 9-slice 테두리(8px) 안이라 버튼 크기가 바뀌어도 늘어나지 않는다.
        /// </summary>
        private static Sprite BuildButton(Color top, Color bottom, string name)
        {
            const int w = 64, h = 40, rim = 3, border = 8;
            const int inner = rim + 2; // 안쪽이 시작하는 edge
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(w - 1 - x, h - 1 - y));
                Color c;
                if (edge == 0) c = Outline;
                else if (edge <= rim) c = Bevel(x, y, w, h, edge, rim);
                else if (edge == rim + 1) c = Outline;
                else
                {
                    float t = (float)(y - inner) / (h - 2 * inner - 1); // 0 아래 → 1 위
                    c = Color.Lerp(bottom, top, Mathf.Clamp01(t));
                    if (t > 0.6f) c = Color.Lerp(c, Color.white, 0.08f * Mathf.InverseLerp(0.6f, 1f, t)); // 광택 띠
                    if (y == h - 1 - inner) c = Color.Lerp(c, Color.white, 0.25f);                    // 위쪽 하이라이트
                    if (y == inner) c = bottom * 0.6f;                                                   // 아래 안 그림자
                    c.a = 1f;
                }
                pixels[y * w + x] = c;
            }
            // 네 모서리 리벳: 2x2 밝은 점 + 오른쪽 아래 그림자 1px
            Color rivet = Color.Lerp(BronzeLight, Color.white, 0.3f);
            foreach (int rx in new[] { 1, w - 4 })
            foreach (int ry in new[] { 1, h - 4 })
            {
                Put(pixels, w, rx, ry + 1, rivet);
                Put(pixels, w, rx + 1, ry + 1, rivet);
                Put(pixels, w, rx, ry + 2, rivet);
                Put(pixels, w, rx + 1, ry + 2, rivet);
                Put(pixels, w, rx + 2, ry + 1, BronzeDark);
                Put(pixels, w, rx + 1, ry, BronzeDark);
                Put(pixels, w, rx + 2, ry, BronzeDark);
            }
            return Make(pixels, w, h, border, name);
        }

        /// <summary>안쪽 칸: 어두운 청동 테두리 1px + 아주 어두운 안쪽(파인 느낌으로 위쪽이 조금 더 어둡다).</summary>
        private static Sprite BuildInset()
        {
            const int size = 24;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                Color c = edge == 0 ? BronzeDark : InsetFill;
                if (edge == 1 && y >= size - 2) c = InsetFill * 0.6f; // 위쪽 안 그림자
                c.a = 1f;
                pixels[y * size + x] = c;
            }
            return Make(pixels, size, size, 6, "ThemeInset");
        }

        /// <summary>베벨: 위·왼쪽 변은 밝은 청동, 아래·오른쪽 변은 어두운 청동, 바깥에서 안쪽으로 조금씩 어두워진다.</summary>
        private static Color Bevel(int x, int y, int w, int h, int edge, int width)
        {
            bool lit = (y >= h - 1 - edge) || (x <= edge); // 위 또는 왼쪽 변
            if ((y >= h - 1 - edge) && (x >= w - 1 - edge)) lit = x - (w - 1 - edge) < y - (h - 1 - edge); // 오른쪽 위 모서리 대각선
            if ((x <= edge) && (y <= edge)) lit = x < y;                                                  // 왼쪽 아래 모서리 대각선
            Color c = lit ? BronzeLight : BronzeDark;
            float depth = (edge - 1f) / Mathf.Max(1f, width - 1f);
            c = Color.Lerp(c, BronzeDark * 0.8f, depth * 0.45f);
            c.a = 1f;
            return c;
        }

        private static void Put(Color[] pixels, int width, int x, int y, Color c) => pixels[y * width + x] = c;

        private static Sprite Make(Color[] pixels, int w, int h, int border, string name)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
