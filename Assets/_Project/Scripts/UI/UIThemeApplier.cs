using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 씬의 모든 Canvas UI에 UITheme(숯빛 판·청동 테두리·진홍 버튼·호박색 제목)을 자동으로 입힌다.
    /// 목록 줄·버튼은 UI 코드가 수시로 다시 만들므로 0.2초마다 훑어 아직 안 입힌 것만 입힌다(인스턴스 ID로 기억).
    /// 자기 그림이 있는 Image(초상·상인 그림·체력 채움·아이콘), 마스크, 화면 전체 어두운 막은 건드리지 않는다.
    /// MainMenuBootstrapper가 MainScene에서 만든다.
    /// </summary>
    public class UIThemeApplier : MonoBehaviour
    {
        private const float ScanInterval = 0.2f;
        private static readonly Vector2 PanelMinSize = new Vector2(300f, 200f);
        private const int TitleFontSize = 20;
        private const float RowAlpha = 0.9f;

        private static readonly HashSet<string> DefaultSpriteNames = new HashSet<string> { "UISprite", "Background", "InputFieldBackground" };

        private readonly HashSet<int> _done = new HashSet<int>();
        private float _timer;
        private static UIThemeApplier _instance;

        private void Awake() => _instance = this;

        /// <summary>
        /// root 아래 UI에 지금 바로 테마를 입힌다. 막 만든 목록·처음 연 창이 다음 스캔(최대 0.2초)까지 기본색으로 보여
        /// 한 번 반짝이던 것을 막는다. 레이아웃을 계산한 뒤에 부른다(크기 0인 칸은 다음 스캔으로 넘어간다).
        /// </summary>
        public static void ApplyNow(Transform root)
        {
            if (_instance == null || root == null) return;
            _instance.ApplyUnder(root);
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;

            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                ApplyUnder(canvas.transform);
            }
        }

        private void ApplyUnder(Transform root)
        {
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                int id = graphic.GetInstanceID();
                if (_done.Contains(id)) continue;
                bool finished = graphic is Image image ? ApplyImage(image)
                    : graphic is Text text ? ApplyText(text)
                    : true;
                if (finished) _done.Add(id);
            }
        }

        /// <summary>Image에 테마를 입힌다. 아직 레이아웃 전이라 크기가 0이면 false(다음에 다시).</summary>
        private static bool ApplyImage(Image image)
        {
            if (image.sprite != null && !DefaultSpriteNames.Contains(image.sprite.name)) return true; // 자기 그림이 있다
            if (image.type == Image.Type.Filled) return true;                                       // 채움 막대
            if (image.GetComponent<Mask>() != null || image.GetComponent<RectMask2D>() != null) return true; // 마스크·초상 칸
            if (image.gameObject.name.Contains("Portrait")) return true; // 그림이 나중에 들어오는 초상 자리(건물 상인 등)

            var rect = image.rectTransform.rect.size;
            if (rect.x <= 0.01f && rect.y <= 0.01f) return false;
            if (IsScreenOverlay(image)) return true;

            Color original = image.color;
            bool isButton = image.GetComponent<Button>() != null && (rect.x < PanelMinSize.x || rect.y < PanelMinSize.y);

            if (image.GetComponentInParent<Scrollbar>() != null)
            {
                bool handle = image.GetComponent<Scrollbar>() == null;
                SetSprite(image, handle ? UITheme.Button : UITheme.Inset, Color.white);
                return true;
            }

            if (isButton)
            {
                bool green = original.g > original.r + 0.05f && original.g > original.b;
                SetSprite(image, green ? UITheme.ButtonGreen : UITheme.Button, Color.white);
                var button = image.GetComponent<Button>();
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.15f, 1.1f, 1.05f, 1f);
                colors.pressedColor = new Color(0.75f, 0.7f, 0.7f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(0.55f, 0.5f, 0.5f, 0.6f);
                colors.colorMultiplier = 1f;
                button.colors = colors;
                return true;
            }

            string name = image.gameObject.name;
            if (name.Contains("Panel") || name.Contains("Box") || name.Contains("Tooltip")
                || (rect.x >= PanelMinSize.x && rect.y >= PanelMinSize.y))
            {
                SetSprite(image, UITheme.Panel, Color.white);
                return true;
            }

            SetSprite(image, UITheme.Inset, RowTint(original));
            return true;
        }

        /// <summary>
        /// 목록 줄 색: 원래 색에 뜻이 있던 줄(선택=초록, 경고=빨강)은 그 색을 섞어 남기고,
        /// 원래 어두웠던 줄(파견 중 등)은 더 어둡게, 나머지는 그대로 어두운 칸.
        /// </summary>
        private static Color RowTint(Color original)
        {
            float max = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
            float min = Mathf.Min(original.r, Mathf.Min(original.g, original.b));
            Color tint = Color.white;
            if (max - min > 0.1f)
            {
                var vivid = new Color(Mathf.Clamp01(original.r * 1.8f), Mathf.Clamp01(original.g * 1.8f), Mathf.Clamp01(original.b * 1.8f));
                tint = Color.Lerp(Color.white, vivid, 0.6f);
            }
            else if (max < 0.15f && original.a >= 0.2f)
            {
                tint = new Color(0.6f, 0.6f, 0.6f);
            }
            tint.a = RowAlpha;
            return tint;
        }

        /// <summary>화면 전체를 덮는 반투명 어두운 막(건물 패널 뒤 배경 등).</summary>
        private static bool IsScreenOverlay(Image image)
        {
            var rt = image.rectTransform;
            bool stretched = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
                             && rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
            bool parentIsCanvas = rt.parent != null && rt.parent.GetComponent<Canvas>() != null;
            var c = image.color;
            return stretched && parentIsCanvas && c.a < 0.8f && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.2f;
        }

        private static void SetSprite(Image image, Sprite sprite, Color color)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = color;
        }

        private static bool ApplyText(Text text)
        {
            string name = text.gameObject.name;
            bool inButton = text.GetComponentInParent<Button>() != null
                            && text.GetComponentInParent<Button>().GetComponent<RectTransform>().rect.height < PanelMinSize.y;
            Color c = text.color;
            bool whiteish = c.r > 0.85f && c.g > 0.85f && c.b > 0.85f;
            bool greyish = Mathf.Abs(c.r - c.g) < 0.05f && Mathf.Abs(c.g - c.b) < 0.05f && !whiteish;

            if (name.StartsWith("NameTag_"))
            {
                text.color = UITheme.BodyText;
                return true;
            }

            if (!inButton && (text.fontSize >= TitleFontSize || name.Contains("Title")))
            {
                text.color = WithAlpha(UITheme.TitleText, c.a);
                text.fontStyle = FontStyle.Bold;
                AddShadow(text);
                return true;
            }

            if (inButton)
            {
                text.color = WithAlpha(UITheme.BodyText, c.a);
                AddShadow(text);
                return true;
            }

            if (whiteish) text.color = WithAlpha(c.a < 0.7f ? UITheme.MutedText : UITheme.BodyText, Mathf.Max(c.a, 0.6f));
            else if (greyish || c.a < 0.7f) text.color = WithAlpha(UITheme.MutedText, Mathf.Max(c.a, 0.6f));
            // 그 밖의 색(경고 빨강, 초록 등)은 뜻이 있으니 그대로 둔다.
            return true;
        }

        private static void AddShadow(Text text)
        {
            if (text.GetComponent<Shadow>() != null) return; // Outline도 Shadow를 상속한다
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
