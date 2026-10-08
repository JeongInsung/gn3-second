using System.Collections;
using System.Collections.Generic;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 화면 왼쪽 아래에 잠깐 떴다 사라지는 알림(파견 출발·귀환, 보상, 회복, 습격, 골드 부족 등).
    /// 최대 4줄을 아래에서 위로 쌓고, 각 줄은 3초 뒤 서서히 사라진다. 클릭을 막지 않는다.
    /// 띄운 알림은 우편함(Mailbox)에 일반 알림 편지로 남아 다시 볼 수 있다(한 판 동안, 저장 안 함).
    /// </summary>
    public class ToastLog : MonoBehaviour
    {
        private const int MaxLines = 4;
        private const float LifeSeconds = 3f;
        private const int MaxHistory = 200;

        public struct Entry
        {
            public int Day;
            public float Hour;
            public string Message;
        }

        private static readonly List<Entry> _history = new List<Entry>();
        /// <summary>지금까지 띄운 알림(오래된 것부터, 최근 200개).</summary>
        public static IReadOnlyList<Entry> History => _history;
        public static event System.Action Added;

        // Domain Reload가 꺼져 있어도 Play마다 기록을 비운다(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _history.Clear();
            Added = null;
        }
        private const float FadeSeconds = 0.6f;
        private const float Width = 520f;

        private static ToastLog _instance;
        private RectTransform _stack;
        private readonly List<GameObject> _lines = new List<GameObject>();

        /// <summary>
        /// 왼쪽 아래에 잠깐 띄운다. mail이면 우편함에 일반 알림(Notice) 편지로도 남긴다.
        /// 퀘스트 결과·중요 소식처럼 따로 편지를 만드는 곳은 mail:false로 불러 같은 내용이 두 번 쌓이지 않게 한다.
        /// </summary>
        public static void Show(string message) => Show(message, true);

        public static void Show(string message, bool mail)
        {
            if (string.IsNullOrEmpty(message)) return;
            _history.Add(new Entry { Day = GameClock.CurrentDay, Hour = GameClock.CurrentHour, Message = message });
            if (_history.Count > MaxHistory) _history.RemoveAt(0);
            if (mail) Mailbox.Post(MailKind.Notice, message);
            if (_instance == null) _instance = Create();
            _instance.Add(message);
            Added?.Invoke();
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
            yield return new WaitForSecondsRealtime(LifeSeconds); // 2배속(timeScale)이어도 알림은 같은 시간 떠 있게
            var group = line != null ? line.GetComponent<CanvasGroup>() : null;
            for (float t = 0f; t < FadeSeconds && group != null; t += Time.unscaledDeltaTime)
            {
                group.alpha = 1f - t / FadeSeconds;
                yield return null;
            }
            _lines.Remove(line);
            if (line != null) Destroy(line);
        }
    }
}
