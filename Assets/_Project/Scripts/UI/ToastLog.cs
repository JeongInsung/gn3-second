using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 화면 왼쪽 아래에 잠깐 떴다 사라지는 알림(파견 출발·귀환, 보상, 회복, 습격, 골드 부족 등).
    /// 최대 4줄을 아래에서 위로 쌓고, 각 줄은 5초 뒤 서서히 사라진다. 클릭을 막지 않는다.
    /// </summary>
    public class ToastLog : MonoBehaviour
    {
        private const int MaxLines = 4;
        private const float LifeSeconds = 5f;
        private const float FadeSeconds = 0.6f;
        private const float Width = 520f;

        private static ToastLog _instance;
        private RectTransform _stack;
        private readonly List<GameObject> _lines = new List<GameObject>();

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_instance == null) _instance = Create();
            _instance.Add(message);
        }

        private static ToastLog Create()
        {
            var go = new GameObject("ToastLog", typeof(Canvas), typeof(CanvasScaler), typeof(ToastLog));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var stack = new GameObject("Stack", typeof(RectTransform), typeof(VerticalLayoutGroup));
            stack.transform.SetParent(go.transform, false);
            var rect = stack.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(16f, 16f);
            rect.sizeDelta = new Vector2(Width, 400f);
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var toast = go.GetComponent<ToastLog>();
            toast._stack = rect;
            return toast;
        }

        private void Add(string message)
        {
            while (_lines.Count >= MaxLines)
            {
                if (_lines[0] != null) Destroy(_lines[0]);
                _lines.RemoveAt(0);
            }

            var line = new GameObject("Toast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
            line.transform.SetParent(_stack, false);
            var background = line.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            var padding = line.GetComponent<VerticalLayoutGroup>();
            padding.padding = new RectOffset(16, 16, 10, 10);
            padding.childControlWidth = true;
            padding.childControlHeight = true;
            line.GetComponent<CanvasGroup>().blocksRaycasts = false;

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Shadow));
            textGO.transform.SetParent(line.transform, false);
            var text = textGO.GetComponent<Text>();
            text.text = message;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.color = UITheme.BodyText;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;
            textGO.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            line.transform.SetAsLastSibling(); // 새 알림이 맨 아래(가장 최근)
            _lines.Add(line);
            StartCoroutine(Expire(line));
        }

        private IEnumerator Expire(GameObject line)
        {
            yield return new WaitForSeconds(LifeSeconds);
            var group = line != null ? line.GetComponent<CanvasGroup>() : null;
            for (float t = 0f; t < FadeSeconds && group != null; t += Time.deltaTime)
            {
                group.alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            _lines.Remove(line);
            if (line != null) Destroy(line);
        }
    }
}
