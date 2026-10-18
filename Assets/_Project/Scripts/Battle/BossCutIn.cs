using System.Collections;
using GN3.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 보스 등장 컷인: 화면이 어두워지고 → 보스 그림이 오른쪽에서 크게 미끄러져 들어오고 → 그 아래 살짝 기울어진 붉은 자막 띠가
    /// 왼쪽부터 펼쳐지며 칭호(작게, 금빛)와 이름(크게)이 들어온다 → 잠깐 멈췄다가 흐려지며 빠진다. 그동안 전투는 멈춰 있다(BattleUIController).
    /// 배속·일시정지와 무관한 실제 시간으로 흐르고, 다 나온 뒤 Space·Enter·클릭이면 바로 넘어간다. 좌표는 캔버스 기준(1920×1080).
    /// </summary>
    public static class BossCutIn
    {
        private const float DimAlpha = 0.72f;
        private const float PortraitHeight = 760f;
        private static readonly Vector2 PortraitAt = new Vector2(430f, 60f);   // 화면 가운데 기준(오른쪽 위)
        private const float PortraitFrom = 1350f;                              // 오른쪽 화면 밖에서 들어온다
        private const float BandY = -250f, BandHeight = 150f, BandWidth = 2600f;
        private const float BandTilt = -5f;                                    // 오른쪽이 살짝 내려간 자막 띠
        private const float WordsX = -300f;                                    // 글자는 띠 왼쪽 편(오른쪽 그림과 겹치지 않게)
        private static readonly Color BandColor = new Color(0.5f, 0.07f, 0.05f, 0.93f);
        private static readonly Color GoldLine = new Color(0.95f, 0.75f, 0.35f, 0.95f);
        private static readonly Color TitleColor = new Color(0.98f, 0.8f, 0.42f);

        // 시간표(초)
        private const float DimIn = 0.25f;
        private const float PortraitStart = 0.1f, PortraitDuration = 0.35f;
        private const float BandStart = 0.35f, BandDuration = 0.25f;
        private const float TextStart = 0.5f, TextDuration = 0.3f;
        private const float Hold = 1.6f;
        private const float SkippableAfter = 0.4f;
        private const float OutDuration = 0.3f;

        /// <param name="onImpact">이름이 뜨는 순간(화면 흔들기 등, 없으면 null).</param>
        public static IEnumerator Play(Transform canvasRoot, Sprite portrait, string title, string bossName, System.Action onImpact)
        {
            var root = new GameObject("BossCutIn", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(canvasRoot, false);
            root.transform.SetAsLastSibling();
            Stretch((RectTransform)root.transform);
            var group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = true; // 컷인 동안 판을 누르지 않게

            var dim = NewImage(root.transform, "Dim", Color.black);
            Stretch(dim.rectTransform);

            var picture = NewImage(root.transform, "Portrait", Color.white);
            picture.sprite = portrait;
            picture.preserveAspect = true;
            var pictureRect = picture.rectTransform;
            float aspect = portrait.rect.width / portrait.rect.height;
            pictureRect.sizeDelta = new Vector2(PortraitHeight * aspect, PortraitHeight);

            // 자막 띠: 기울어진 틀 안에 붉은 띠(왼쪽부터 펼쳐짐)와 글자(띠가 펼쳐진 뒤 들어옴)
            var bandRoot = new GameObject("Band", typeof(RectTransform));
            bandRoot.transform.SetParent(root.transform, false);
            var bandRootRect = (RectTransform)bandRoot.transform;
            bandRootRect.anchoredPosition = new Vector2(0f, BandY);
            bandRootRect.localRotation = Quaternion.Euler(0f, 0f, BandTilt);

            var band = NewImage(bandRoot.transform, "Strip", BandColor);
            var bandRect = band.rectTransform;
            bandRect.pivot = new Vector2(0f, 0.5f);
            bandRect.anchoredPosition = new Vector2(-BandWidth / 2f, 0f);
            bandRect.sizeDelta = new Vector2(BandWidth, BandHeight);
            foreach (float edge in new[] { 1f, 0f }) // 위아래 금빛 가는 선(띠와 함께 펼쳐진다)
            {
                var line = NewImage(band.transform, "Line", GoldLine);
                var lineRect = line.rectTransform;
                lineRect.anchorMin = new Vector2(0f, edge);
                lineRect.anchorMax = new Vector2(1f, edge);
                lineRect.pivot = new Vector2(0.5f, edge);
                lineRect.sizeDelta = new Vector2(0f, 4f);
                lineRect.anchoredPosition = Vector2.zero;
            }

            var words = new GameObject("Words", typeof(RectTransform), typeof(CanvasGroup));
            words.transform.SetParent(bandRoot.transform, false);
            var wordsRect = (RectTransform)words.transform;
            var wordsGroup = words.GetComponent<CanvasGroup>();
            if (!string.IsNullOrEmpty(title))
                NewText(words.transform, title, 30, UITheme.Font, TitleColor, new Vector2(0f, 50f));
            NewText(words.transform, bossName, 76, UITheme.BoldFont, Color.white, new Vector2(0f, -14f));

            float t = 0f;
            bool shaken = false;
            float outAt = TextStart + TextDuration + Hold;
            while (t < outAt + OutDuration)
            {
                // 다 나온 뒤엔 Space·Enter·클릭으로 바로 빠진다
                if (t >= SkippableAfter && t < outAt && SkipPressed()) outAt = t;

                dim.color = new Color(0f, 0f, 0f, DimAlpha * Mathf.Clamp01(t / DimIn));

                float p = EaseOut(Mathf.Clamp01((t - PortraitStart) / PortraitDuration));
                float leave = Mathf.Clamp01((t - outAt) / OutDuration);
                pictureRect.anchoredPosition = new Vector2(Mathf.Lerp(PortraitFrom, PortraitAt.x, p) - 80f * leave, PortraitAt.y);
                float zoom = Mathf.Lerp(1.08f, 1f, p);
                pictureRect.localScale = new Vector3(-zoom, zoom, 1f); // 좌우 반전: 오른쪽에 선 그림이 왼쪽(자막·전장)을 노려본다
                picture.color = new Color(1f, 1f, 1f, Mathf.Clamp01(p * 3f));

                bandRect.localScale = new Vector3(EaseOut(Mathf.Clamp01((t - BandStart) / BandDuration)), 1f, 1f);

                float w = EaseOut(Mathf.Clamp01((t - TextStart) / TextDuration));
                wordsGroup.alpha = w;
                wordsRect.anchoredPosition = new Vector2(WordsX - 60f * (1f - w), 0f);
                if (!shaken && t >= TextStart)
                {
                    shaken = true;
                    onImpact?.Invoke();
                }

                group.alpha = 1f - leave;
                yield return null;
                t += Time.unscaledDeltaTime; // 전투 배속·일시정지와 무관
            }
            Object.Destroy(root);
        }

        private static bool SkipPressed()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = name == "Dim"; // 막만 클릭을 받는다(판으로 새지 않게)
            return image;
        }

        private static void NewText(Transform parent, string content, int size, Font font, Color color, Vector2 position)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(1600f, size * 1.4f);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
