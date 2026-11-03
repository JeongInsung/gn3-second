using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 중 땅을 끌 때 생기는 점선 네모(바탕화면 드래그 선택처럼). 옅은 바탕 + 네 변의 점선이 테두리를 따라 흐른다.
    /// 필드 좌표 두 점(끌기 시작·지금)으로 SetCorners. 클릭은 막지 않는다. 일시정지(timeScale 0) 중에도 흐른다.
    /// </summary>
    public class BattleSelectionBox : MonoBehaviour
    {
        private const float DashLength = 16f;    // 점선 한 칸(칠한 곳 + 빈 곳)
        private const float LineWidth = 3f;
        private const float ScrollSpeed = 1.5f;  // 칸/초
        private static readonly Color LineColor = new Color(0.6f, 1f, 0.7f, 0.95f);
        private static readonly Color FillColor = new Color(0.5f, 1f, 0.6f, 0.12f);

        private static Texture2D _dashH, _dashV;

        private RectTransform _rect;
        private RawImage _top, _bottom, _left, _right;

        public static BattleSelectionBox Create(RectTransform parent)
        {
            var go = new GameObject("SelectionBox", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            var fill = go.GetComponent<Image>();
            fill.color = FillColor;
            fill.raycastTarget = false;

            var box = go.AddComponent<BattleSelectionBox>();
            box._rect = rect;
            box._top = box.Edge("Top", new Vector2(0f, 1f), Vector2.one, horizontal: true);
            box._bottom = box.Edge("Bottom", Vector2.zero, new Vector2(1f, 0f), horizontal: true);
            box._left = box.Edge("Left", Vector2.zero, new Vector2(0f, 1f), horizontal: false);
            box._right = box.Edge("Right", new Vector2(1f, 0f), Vector2.one, horizontal: false);
            go.SetActive(false);
            return box;
        }

        private RawImage Edge(string name, Vector2 anchorMin, Vector2 anchorMax, bool horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = horizontal ? new Vector2(0f, LineWidth) : new Vector2(LineWidth, 0f);
            var image = go.GetComponent<RawImage>();
            image.texture = horizontal ? DashH : DashV;
            image.color = LineColor;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>두 모서리(필드 좌표, 순서 무관)로 네모를 맞춘다.</summary>
        public void SetCorners(Vector2 a, Vector2 b)
        {
            var min = Vector2.Min(a, b);
            _rect.anchoredPosition = min;
            _rect.sizeDelta = Vector2.Max(a, b) - min;
            Scroll();
        }

        private void Update() => Scroll();

        /// <summary>점선이 네모 둘레를 한 방향(시계 방향)으로 돌게 uv를 민다.</summary>
        private void Scroll()
        {
            if (_rect == null) return;
            var size = _rect.sizeDelta;
            float t = Time.unscaledTime * ScrollSpeed;
            float w = Mathf.Max(1f, size.x) / DashLength, h = Mathf.Max(1f, size.y) / DashLength;
            _top.uvRect = new Rect(-t, 0f, w, 1f);
            _bottom.uvRect = new Rect(t, 0f, w, 1f);
            _left.uvRect = new Rect(0f, -t, 1f, h);
            _right.uvRect = new Rect(0f, t, 1f, h);
        }

        private static Texture2D DashH => _dashH != null ? _dashH : _dashH = MakeDash(16, 2, horizontal: true);
        private static Texture2D DashV => _dashV != null ? _dashV : _dashV = MakeDash(2, 16, horizontal: false);

        private static Texture2D MakeDash(int w, int h, bool horizontal)
        {
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = (horizontal ? x < w / 2 : y < h / 2) ? Color.white : Color.clear;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = horizontal ? "SelectionDashH" : "SelectionDashV",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
