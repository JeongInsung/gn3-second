using GN3.Save;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 시작할 때 저장 파일이 있으면 화면 가운데에 "이어하기 / 새로 시작"을 묻는 창.
    /// 이어하기: SaveSystem.Load. 새로 시작: 파일은 그대로 두고 지금 상태로 시작(다음 자동 저장이 덮어쓴다).
    /// 고를 때까지 뒤 화면 클릭을 막는다.
    /// </summary>
    public class ContinuePrompt : MonoBehaviour
    {
        private const float Width = 460f;
        private const float Height = 240f;

        private Font _font;

        public static ContinuePrompt Create()
        {
            var go = new GameObject("ContinuePrompt", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ContinuePrompt));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4; // 도착 알림창(3) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var prompt = go.GetComponent<ContinuePrompt>();
            prompt.Build();
            EscapeCloser.Register(go, () => false, WindowRole.Popup); // 반드시 고른다: ESC는 먹기만 해 뒤 창이 닫히지 않게
            return prompt;
        }

        private void Continue()
        {
            bool ok = SaveSystem.Load();
            ToastLog.Show(ok ? "저장된 게임을 불러왔습니다" : "저장 파일을 읽지 못해 새로 시작합니다");
            Destroy(gameObject);
        }

        private void StartNew()
        {
            ToastLog.Show("새로 시작합니다 (다음 자동 저장 때 이전 저장을 덮어씁니다)");
            Destroy(gameObject);
        }

        private void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 뒤 화면 클릭 막기(화면 전체 어둡게 — 테마 자동 적용에서 빠지는 전체 화면 막)
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(transform, false);
            var dimRect = dim.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(Width, Height);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.96f);

            var title = CreateText(panel.transform, "저장된 게임이 있습니다", 22, TextAnchor.MiddleCenter, UITheme.TitleText);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(0f, -18f), new Vector2(Width - 40f, 34f));

            var body = CreateText(panel.transform, SaveSystem.Describe(), 16, TextAnchor.UpperCenter, UITheme.BodyText);
            Place(body.rectTransform, new Vector2(0f, -64f), new Vector2(Width - 40f, 80f));

            var row = new GameObject("Choices", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, 22f);
            rowRect.sizeDelta = new Vector2(Width - 60f, 46f);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            CreateButton(row.transform, "이어하기", Continue, new Color(0.25f, 0.55f, 0.35f, 1f)); // 초록 → 테마 초록 버튼
            CreateButton(row.transform, "새로 시작", StartNew, new Color(0.6f, 0.2f, 0.2f, 1f));
        }

        private static void Place(RectTransform rect, Vector2 topCenter, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = topCenter;
            rect.sizeDelta = size;
        }

        private void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<LayoutElement>().minHeight = 46f;
            var text = CreateText(go.transform, label, 18, TextAnchor.MiddleCenter, UITheme.BodyText);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            go.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
