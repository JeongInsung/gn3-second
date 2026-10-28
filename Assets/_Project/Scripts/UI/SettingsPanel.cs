using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 설정 창: 음악 볼륨 · 효과음 볼륨(GameAudio) · 화면 모드(전체 화면/창) · 해상도(1280×720/1600×900/1920×1080).
    /// 바꾸는 즉시 적용하고 PlayerPrefs에 저장한다(게임 저장 파일과 따로). 켤 때 ApplySaved가 다시 적용한다.
    /// 효과음 슬라이더를 움직이면 버튼 소리로 크기를 미리 들려준다.
    /// 화면 모드·해상도는 에디터에서 바뀌지 않아(Game 창 크기를 따름) 에디터에서는 안내문을 띄운다.
    /// 타이틀 화면의 "설정"이 연다. ESC·X·바깥 어두운 곳으로 닫힌다. sortingOrder 7(불러오기 창·일시정지 메뉴 6 위).
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        private const string FullscreenKey = "gn3.fullscreen";
        private const string WidthKey = "gn3.resW";
        private const string HeightKey = "gn3.resH";
        private static readonly Vector2Int[] Resolutions = { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) };

        private const float Width = 600f;
        private const float Height = 750f; // 볼륨·화면·해상도 + 안내 다시 보기 + 단축키 목록(마을 두 줄)
        private const float LabelWidth = 130f;
        private const float RowLeft = 40f;
        private static readonly Color SelectedTint = new Color(1f, 0.82f, 0.5f); // 고른 버튼: 숯빛 버튼을 따뜻한 청동빛으로

        private static SettingsPanel _open;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _open = null;

        /// <summary>게임을 켤 때 저장된 설정을 적용한다(화면 모드·해상도는 빌드에서만).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySaved()
        {
            AudioListener.volume = 1f; // 음량은 음악·효과음 볼륨(GameAudio)이 따로 맡는다
            if (Application.isEditor || !PlayerPrefs.HasKey(WidthKey)) return;
            Screen.SetResolution(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey), SavedMode);
        }

        private static bool SavedFullscreen => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        private static FullScreenMode SavedMode => SavedFullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        private static Vector2Int SavedResolution => new Vector2Int(
            PlayerPrefs.GetInt(WidthKey, Screen.width), PlayerPrefs.GetInt(HeightKey, Screen.height));

        private const float SfxPreviewGap = 0.15f; // 효과음 슬라이더를 움직일 때 미리듣기 간격
        private float _lastPreview = -1f;
        private readonly List<(Button button, Text label, System.Func<bool> selected)> _choices = new List<(Button, Text, System.Func<bool>)>();

        public static void Open()
        {
            if (_open != null) return;
            var go = new GameObject("SettingsPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(SettingsPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _open = go.GetComponent<SettingsPanel>();
            _open.Build();
            EscapeCloser.Register(go, _open.Close, WindowRole.Popup);
            UIThemeApplier.ApplyNow(go.transform); // 글씨 테마를 먼저 입힌 뒤
            _open.RefreshChoices();                 // 고른 버튼 표시(테마가 버튼 글씨를 흰색으로 덮지 않게 나중에)
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Close() => Destroy(gameObject);

        // ---------- 바꾸기 ----------

        private void SetFullscreen(bool fullscreen)
        {
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            var resolution = SavedResolution;
            Screen.SetResolution(resolution.x, resolution.y, SavedMode);
            RefreshChoices();
        }

        private void SetResolution(Vector2Int resolution)
        {
            PlayerPrefs.SetInt(WidthKey, resolution.x);
            PlayerPrefs.SetInt(HeightKey, resolution.y);
            PlayerPrefs.Save();
            Screen.SetResolution(resolution.x, resolution.y, SavedMode);
            RefreshChoices();
        }

        /// <summary>고른 버튼만 청동빛 + 호박색 글씨, 나머지는 기본 버튼.</summary>
        private void RefreshChoices()
        {
            foreach (var (button, label, selected) in _choices)
            {
                bool on = selected();
                button.GetComponent<Image>().color = on ? SelectedTint : Color.white;
                label.color = on ? UITheme.TitleText : UITheme.BodyText;
            }
        }

        // ---------- 화면 만들기 ----------

        private void Build()
        {
            // 바깥 어두운 막: 누르면 닫힌다(테마 자동 적용에서 빠지는 전체 화면 막)
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(transform, false);
            Stretch((RectTransform)dim.transform);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            dim.GetComponent<Button>().transition = Selectable.Transition.None;
            dim.GetComponent<Button>().onClick.AddListener(Close);

            // 판(안쪽 클릭이 막으로 올라가 닫히지 않게 아무 일 안 하는 버튼)
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Button));
            panel.transform.SetParent(transform, false);
            ((RectTransform)panel.transform).sizeDelta = new Vector2(Width, Height);
            DraggablePanel.Attach((RectTransform)panel.transform); // 잡고 끌어 옮길 수 있다(뒤 어두운 막은 고정)
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UITheme.Panel;
            panelImage.type = Image.Type.Sliced;
            panel.GetComponent<Button>().transition = Selectable.Transition.None;

            var title = CreateText(panel.transform, "설정", UITheme.TitleSize, TextAnchor.MiddleCenter, UITheme.TitleText);
            title.name = "Title";
            title.fontStyle = FontStyle.Bold;
            PlaceTop(title.rectTransform, 0f, -18f, Width - 120f, 40f, center: true);

            UITheme.CreateCloseX(panel.transform, Close); // 공통 닫기 X

            // 음악 · 효과음 볼륨(따로)
            float y = -96f;
            VolumeRow(panel.transform, "음악", y, GameAudio.MusicVolume, v => GameAudio.MusicVolume = v);
            y -= 76f;
            VolumeRow(panel.transform, "효과음", y, GameAudio.SfxVolume, v =>
            {
                GameAudio.SfxVolume = v;
                // 움직이는 동안 바뀐 크기를 들려준다(너무 자주 울리지 않게 간격을 둔다)
                if (Time.unscaledTime - _lastPreview >= SfxPreviewGap)
                {
                    _lastPreview = Time.unscaledTime;
                    GameAudio.PlaySfx(ButtonSounds.ClickSound);
                }
            });

            // 화면 모드
            y -= 76f;
            RowLabel(panel.transform, "화면 모드", y);
            ChoiceRow(panel.transform, y, 190f, new (string, UnityEngine.Events.UnityAction, System.Func<bool>)[]
            {
                ("전체 화면", () => SetFullscreen(true), () => SavedFullscreen),
                ("창 모드", () => SetFullscreen(false), () => !SavedFullscreen),
            });

            // 해상도
            y -= 76f;
            RowLabel(panel.transform, "해상도", y);
            var options = new List<(string, UnityEngine.Events.UnityAction, System.Func<bool>)>();
            foreach (var r in Resolutions)
            {
                var resolution = r;
                options.Add(($"{r.x}×{r.y}", () => SetResolution(resolution), () => SavedResolution == resolution));
            }
            ChoiceRow(panel.transform, y, 124f, options.ToArray());

            // 처음 여는 창 안내(FirstTimeTips)를 다시 켠다
            y -= 76f;
            RowLabel(panel.transform, "안내", y);
            Text resetLabel = null;
            var reset = CreateButton(panel.transform, "안내 다시 보기", () =>
            {
                FirstTimeTips.ResetAll();
                if (resetLabel != null) resetLabel.text = "다시 보기 켬";
            }, out resetLabel);
            PlaceTop((RectTransform)reset.transform, RowLeft + LabelWidth, y, 190f, 44f, center: false);
            reset.gameObject.AddComponent<TooltipTrigger>().Text = "대장간·창고·건강 창 등을 처음 열 때 뜨던 안내를 다음에 열 때 다시 보여 준다";

            // 끌어 옮겨 저장된 창 자리(DraggablePanel)를 모두 지운다
            Text windowsLabel = null;
            var windows = CreateButton(panel.transform, "창 위치 초기화", () =>
            {
                DraggablePanel.ResetAll();
                if (windowsLabel != null) windowsLabel.text = "초기화함";
            }, out windowsLabel);
            PlaceTop((RectTransform)windows.transform, RowLeft + LabelWidth + 200f, y, 190f, 44f, center: false);
            windows.gameObject.AddComponent<TooltipTrigger>().Text = "끌어 옮겨 둔 창들을 다음에 열 때부터 원래 자리에 띄운다";

            // 단축키 목록(게임 안에서 알 수 있게)
            y -= 64f;
            RowLabel(panel.transform, "단축키", y);
            var keys = CreateText(panel.transform,
                "마을  M 용병시장 · T 파티 · Q 퀘스트 · L 우편함 · B 건설 · G 장부 · H 의뢰 기록\n" +
                "      C 일정 · Y 업적 · U 길드 · K 건강 · R 관계 · N 근처 길드 · J 도감\n" +
                "      Space 시간 진행 · F 배속(1/2/4) · P 일시정지 · ESC 창 닫기/메뉴\n" +
                "전투  Space 일시정지 · F 배속 · Q 스킬 · Tab 분대 바꾸기 · B 가방 · D 통계\n" +
                "      분대 명령 1 집결 · 2 후퇴 · 3 전진 · 4 집중 · 결과 화면 Enter 마을로\n" +
                "      수색 창 E 가져가기 · R 모두 열기 · X 두고 가기 · 라운드 정리 Enter\n" +
                "      가방 창 X 버리기 · S 값 순 정리 · Esc 닫기 · Shift+클릭 빈 칸 많은 대원에게",
                UITheme.SmallSize, TextAnchor.UpperLeft, UITheme.BodyText);
            PlaceTop(keys.rectTransform, RowLeft + LabelWidth, y - 10f, Width - RowLeft * 2f - LabelWidth, 170f, center: false);

            if (Application.isEditor)
            {
                var note = CreateText(panel.transform, "화면 모드·해상도는 빌드한 게임에서 적용됩니다(에디터는 Game 창 크기를 따름)",
                    UITheme.SmallSize, TextAnchor.MiddleCenter, UITheme.MutedText);
                note.name = "EditorNote";
                PlaceTop(note.rectTransform, 0f, -(Height - 52f), Width - 60f, 30f, center: true);
            }
        }

        /// <summary>이름 · 슬라이더 · % 한 줄. 값이 바뀌면 set을 부르고 %를 고친다.</summary>
        private void VolumeRow(Transform parent, string label, float y, float value, System.Action<float> set)
        {
            RowLabel(parent, label, y);
            var slider = CreateSlider(parent, new Vector2(RowLeft + LabelWidth, y), 300f);
            var percent = CreateText(parent, "", UITheme.BodySize, TextAnchor.MiddleLeft, UITheme.BodyText);
            PlaceTop(percent.rectTransform, RowLeft + LabelWidth + 316f, y, 80f, 44f, center: false);
            slider.value = value;
            percent.text = $"{Mathf.RoundToInt(value * 100f)}%";
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                percent.text = $"{Mathf.RoundToInt(v * 100f)}%";
            });
        }

        private void RowLabel(Transform parent, string text, float y)
        {
            var label = CreateText(parent, text, UITheme.BodySize, TextAnchor.MiddleLeft, UITheme.BodyText);
            PlaceTop(label.rectTransform, RowLeft, y, LabelWidth, 44f, center: false);
        }

        private void ChoiceRow(Transform parent, float y, float buttonWidth, (string label, UnityEngine.Events.UnityAction onClick, System.Func<bool> selected)[] options)
        {
            float x = RowLeft + LabelWidth;
            foreach (var option in options)
            {
                var button = CreateButton(parent, option.label, option.onClick, out var label);
                PlaceTop((RectTransform)button.transform, x, y, buttonWidth, 44f, center: false);
                _choices.Add((button, label, option.selected));
                x += buttonWidth + 10f;
            }
        }

        /// <summary>테마 슬라이더: 어두운 칸 + 호박색 채움 + 버튼 모양 손잡이.</summary>
        private static Slider CreateSlider(Transform parent, Vector2 topLeft, float width)
        {
            var go = new GameObject("VolumeSlider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            PlaceTop((RectTransform)go.transform, topLeft.x, topLeft.y - 12f, width, 20f, center: false);

            var background = CreateSliced(go.transform, "Track", UITheme.Inset, Color.white);
            background.raycastTarget = true; // 칸 아무 데나 눌러도 그 자리로
            Stretch(background.rectTransform);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            Stretch(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(3f, 3f);
            fillAreaRect.offsetMax = new Vector2(-3f, -3f);
            // 채움은 단색(RawImage): 어두운 칸 그림에 색을 곱하면 호박색이 거의 안 보인다. RawImage라 테마 자동 적용도 건드리지 않는다.
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(RawImage));
            fillGO.transform.SetParent(fillArea.transform, false);
            var fill = fillGO.GetComponent<RawImage>();
            fill.color = new Color(UITheme.TitleText.r * 0.85f, UITheme.TitleText.g * 0.85f, UITheme.TitleText.b * 0.85f, 1f);
            fill.raycastTarget = false;
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);
            var handle = CreateSliced(handleArea.transform, "Handle", UITheme.Button, Color.white);
            handle.rectTransform.sizeDelta = new Vector2(22f, 32f);
            handle.raycastTarget = true;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            var colors = slider.colors;
            colors.highlightedColor = new Color(1.15f, 1.1f, 1.05f, 1f);
            colors.pressedColor = new Color(0.8f, 0.75f, 0.7f, 1f);
            slider.colors = colors;
            return slider;
        }

        private static Image CreateSliced(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var go = new GameObject(text + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            UITheme.StyleButton(button);
            button.onClick.AddListener(onClick);
            label = CreateText(go.transform, text, UITheme.BodySize, TextAnchor.MiddleCenter, UITheme.BodyText);
            Stretch(label.rectTransform);
            return button;
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = UITheme.Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>판 위쪽 기준 배치. center면 가로 가운데(x는 가운데에서의 거리), 아니면 왼쪽에서 x.</summary>
        private static void PlaceTop(RectTransform rect, float x, float y, float width, float height, bool center)
        {
            var anchor = center ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(x, y);
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
