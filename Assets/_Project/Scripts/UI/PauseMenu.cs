using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을에서 열린 창 없이 ESC를 누르면 뜨는 메뉴: 계속하기 · 설정 · 튜토리얼 다시 보기 · 타이틀로.
    /// 뒤를 살짝 어둡게 깔고, 떠 있는 동안 게임을 멈춘다(GamePause). ESC·바깥 어두운 곳·"계속하기"로 닫힌다.
    /// "타이틀로"는 자동 저장한 뒤 타이틀을 다시 띄운다(새로 시작하기는 막힌 채로).
    /// sortingOrder 6: 타이틀(5)·마을 HUD(3) 위, 설정 창(7) 아래(메뉴에서 연 설정 창이 위에 뜬다).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        private const float Width = 420f;
        private const float ButtonWidth = 300f, ButtonHeight = 56f, ButtonSpacing = 14f;
        private const float FirstButtonTop = -84f;

        private static PauseMenu _open;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _open = null;

        public static void Open()
        {
            if (_open != null) return;
            var go = new GameObject("PauseMenu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PauseMenu));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _open = go.GetComponent<PauseMenu>();
            _open.Build();
            EscapeCloser.Register(go, _open.Close, WindowRole.Popup);
            UIThemeApplier.ApplyNow(go.transform);
        }

        private void OnEnable() => GamePause.Push(this);

        private void OnDestroy()
        {
            GamePause.Pop(this);
            if (_open == this) _open = null;
        }

        private void Close() => Destroy(gameObject);

        private void ReplayTutorial()
        {
            Close();
            TutorialGuide.Begin();
        }

        private void ToTitle()
        {
            ToastLog.Show(SaveSystem.Save(auto: true) ? "저장하고 타이틀로 돌아갑니다" : "저장에 실패했습니다", false);
            Close();
            TitleScreen.Create(fromGame: true);
        }

        // ---------- 화면 만들기 ----------

        private void Build()
        {
            // 바깥 어두운 막: 누르면 닫힌다
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(transform, false);
            Stretch((RectTransform)dim.transform);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            dim.GetComponent<Button>().transition = Selectable.Transition.None;
            dim.GetComponent<Button>().onClick.AddListener(Close);

            var actions = new (string label, UnityEngine.Events.UnityAction onClick)[]
            {
                ("계속하기", Close),
                ("설정", SettingsPanel.Open),
                ("튜토리얼 다시 보기", ReplayTutorial),
                ("타이틀로", ToTitle),
            };
            float height = -FirstButtonTop + actions.Length * (ButtonHeight + ButtonSpacing) + 22f;

            // 판(안쪽 클릭이 막으로 올라가 닫히지 않게 아무 일 안 하는 버튼)
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Button));
            panel.transform.SetParent(transform, false);
            ((RectTransform)panel.transform).sizeDelta = new Vector2(Width, height);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;
            panel.GetComponent<Button>().transition = Selectable.Transition.None;

            var title = CreateText(panel.transform, "메뉴", UITheme.TitleSize, UITheme.TitleText);
            title.name = "Title";
            title.fontStyle = FontStyle.Bold;
            PlaceTop(title.rectTransform, -18f, Width - 60f, 40f);

            float y = FirstButtonTop;
            foreach (var (label, onClick) in actions)
            {
                var button = CreateButton(panel.transform, label, onClick);
                PlaceTop((RectTransform)button.transform, y, ButtonWidth, ButtonHeight);
                y -= ButtonHeight + ButtonSpacing;
            }
        }

        private static Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            UITheme.StyleButton(button);
            button.onClick.AddListener(onClick);
            var label = CreateText(go.transform, text, UITheme.HeadingSize, UITheme.BodyText);
            Stretch(label.rectTransform);
            return button;
        }

        private static Text CreateText(Transform parent, string content, int size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = UITheme.Font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>판 위쪽 가운데 기준 배치.</summary>
        private static void PlaceTop(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
