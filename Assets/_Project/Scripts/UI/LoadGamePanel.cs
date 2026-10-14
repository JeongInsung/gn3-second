using System.Collections.Generic;
using GN3.Save;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 불러오기 창: 저장 기록(SaveSystem.ListSaves, 최신순)을 한 줄씩 보여 주고 골라 불러온다.
    /// 줄: "650년 3월 10일 · 견습 길드 · 용병 3명 · 368G" / "2026-10-14 20:35 · 자동|직접" + [불러오기].
    /// 지금 진행 중 저장하지 않은 부분이 사라지므로 버튼을 한 번 누르면 "정말 불러오기?"로 바뀌고 한 번 더 눌러야 불러온다.
    /// 오른쪽 위 "불러오기" 버튼과 시작 창(ContinuePrompt)의 "다른 저장 불러오기"가 연다. ESC·X·바깥 어두운 곳으로 닫힌다.
    /// </summary>
    public class LoadGamePanel : MonoBehaviour
    {
        private const float Width = 620f;
        private const float Height = 500f;
        private const float RowHeight = 62f;
        private const string LoadLabel = "불러오기";
        private const string ConfirmLabel = "정말 불러오기?";

        private static LoadGamePanel _open;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _open = null;

        private Font _font;
        private string _armedPath; // "정말 불러오기?" 상태인 줄
        private readonly Dictionary<string, Text> _buttonLabels = new Dictionary<string, Text>();

        public static void Open()
        {
            if (_open != null) return;
            var go = new GameObject("LoadGamePanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(LoadGamePanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // 시작 창(4)·도착 알림창(3) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _open = go.GetComponent<LoadGamePanel>();
            _open.Build();
            EscapeCloser.Register(go, _open.Close, WindowRole.Popup);
            UIThemeApplier.ApplyNow(go.transform);
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Close() => Destroy(gameObject);

        private void Press(SaveSystem.SaveEntry entry)
        {
            if (TimeAdvanceController.IsAdvancing)
            {
                ToastLog.Show("시간을 넘기는 중에는 불러올 수 없습니다", false);
                return;
            }
            if (_armedPath != entry.Path)
            {
                Arm(entry.Path);
                return;
            }
            bool ok = SaveSystem.Load(entry.Path);
            ToastLog.Show(ok ? $"저장을 불러왔습니다 ({FirstLine(entry.Description)})" : "저장 파일을 읽지 못했습니다", false);
            if (ok) Close();
            else Arm(null);
        }

        /// <summary>path 줄만 "정말 불러오기?"로, 나머지는 원래대로.</summary>
        private void Arm(string path)
        {
            _armedPath = path;
            foreach (var pair in _buttonLabels)
                if (pair.Value != null) pair.Value.text = pair.Key == path ? ConfirmLabel : LoadLabel;
        }

        private static string FirstLine(string text)
        {
            int newline = text.IndexOf('\n');
            return newline < 0 ? text : text.Substring(0, newline);
        }

        // ---------- 화면 만들기 ----------

        private void Build()
        {
            _font = UITheme.Font;

            // 바깥 어두운 막: 누르면 닫힌다(테마 자동 적용에서 빠지는 전체 화면 막).
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(transform, false);
            Stretch(dim.GetComponent<RectTransform>());
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            dim.GetComponent<Button>().transition = Selectable.Transition.None;
            dim.GetComponent<Button>().onClick.AddListener(Close);

            // 판 안 클릭이 막으로 올라가 닫히지 않게 판에도 아무 일 안 하는 버튼(BuildingPanel과 같은 방식).
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Button));
            panel.transform.SetParent(transform, false);
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(Width, Height);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.96f);
            panel.GetComponent<Button>().transition = Selectable.Transition.None;

            var title = CreateText(panel.transform, "불러오기", 24, TextAnchor.MiddleCenter, UITheme.TitleText);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(0f, -16f), new Vector2(Width - 40f, 36f));

            var hint = CreateText(panel.transform, "저장할 때마다 기록이 남습니다(자동·직접 각각 최근 10개). 불러오면 지금 저장하지 않은 진행은 사라집니다.",
                14, TextAnchor.MiddleCenter, UITheme.MutedText);
            Place(hint.rectTransform, new Vector2(0f, -54f), new Vector2(Width - 40f, 36f));

            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-12f, -12f);
            closeRect.sizeDelta = new Vector2(36f, 36f);
            close.GetComponent<Image>().color = new Color(0.3f, 0.28f, 0.33f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Close);
            Stretch(CreateText(close.transform, "X", 20, TextAnchor.MiddleCenter, UITheme.BodyText).rectTransform);

            // 목록(스크롤)
            var list = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            list.transform.SetParent(panel.transform, false);
            Place(list.GetComponent<RectTransform>(), new Vector2(0f, -98f), new Vector2(Width - 40f, Height - 118f));
            list.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(list.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(6, 6, 6, 6);
            v.spacing = 6f;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var scroll = list.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            SmoothWheelScroll.Attach(scroll);

            var saves = SaveSystem.ListSaves();
            if (saves.Count == 0)
            {
                var empty = CreateText(list.transform, "저장 기록이 없습니다", 18, TextAnchor.MiddleCenter, UITheme.MutedText);
                Stretch(empty.rectTransform);
                return;
            }
            foreach (var entry in saves) CreateRow(content.transform, entry);
        }

        private void CreateRow(Transform parent, SaveSystem.SaveEntry entry)
        {
            var row = new GameObject("Save_" + System.IO.Path.GetFileNameWithoutExtension(entry.Path),
                typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f); // UIThemeApplier가 어두운 칸으로 바꾼다
            var rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = RowHeight;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(12, 10, 8, 8);
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var column = new GameObject("Label", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            column.transform.SetParent(row.transform, false);
            column.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var cv = column.GetComponent<VerticalLayoutGroup>();
            cv.childAlignment = TextAnchor.MiddleLeft;
            cv.childControlWidth = cv.childControlHeight = true;
            cv.childForceExpandWidth = true;
            cv.childForceExpandHeight = false;
            cv.spacing = 2f;
            var main = CreateText(column.transform, FirstLine(entry.Description), 17, TextAnchor.MiddleLeft, UITheme.BodyText);
            main.fontStyle = FontStyle.Bold;
            CreateText(column.transform, $"{entry.SavedAt:yyyy-MM-dd HH:mm} · {(entry.IsAuto ? "자동" : "직접")} 저장", 13, TextAnchor.MiddleLeft, UITheme.MutedText);

            var button = new GameObject("LoadButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            button.transform.SetParent(row.transform, false);
            button.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // 테마 진홍 버튼
            var buttonLayout = button.GetComponent<LayoutElement>();
            buttonLayout.minWidth = buttonLayout.preferredWidth = 130f;
            buttonLayout.minHeight = buttonLayout.preferredHeight = 40f;
            var label = CreateText(button.transform, LoadLabel, 16, TextAnchor.MiddleCenter, UITheme.BodyText);
            Stretch(label.rectTransform);
            _buttonLabels[entry.Path] = label;
            button.GetComponent<Button>().onClick.AddListener(() => Press(entry));
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 topCenter, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = topCenter;
            rect.sizeDelta = size;
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
