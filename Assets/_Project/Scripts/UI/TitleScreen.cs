using System.Collections;
using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 게임을 켜면 마을 위를 덮는 메인화면: 저녁 마을 그림(Resources/Title/title_bg) + "길드 마스터" + 버튼 4개
    /// (이어하기 · 게임 불러오기 · 새로 시작하기 · 설정). 떠 있는 동안 게임을 멈추고(GamePause), 고르면 흐려지며 마을로 넘어간다.
    /// 새 씬 없이 MainScene 위 오버레이라 저장·불러오기 흐름은 그대로다(새로 시작 = 지금 막 만든 새 게임 상태 그대로).
    /// sortingOrder 5: 마을 HUD(3)·알림창(4) 위, 불러오기 창(6)·설정 창(7) 아래.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        private const string BackgroundPath = "Title/title_bg";
        private const float Left = 160f;              // 제목·버튼 왼쪽 여백
        private const float ButtonWidth = 380f, ButtonHeight = 64f, ButtonSpacing = 16f;
        private const float FadeInSeconds = 0.6f, FadeOutSeconds = 0.4f;
        private const float ZoomPeriodSeconds = 30f, ZoomAmount = 0.04f; // 배경이 천천히 커졌다 돌아온다
        private const string NewGameLabel = "새로 시작하기";
        private const string NewGameConfirmLabel = "정말 새로 시작?";

        private CanvasGroup _group;
        private RectTransform _background;
        private Text _newGameLabel;
        private bool _newGameArmed;
        private bool _closing;
        private bool _fromGame; // 게임 중 메뉴(PauseMenu)의 "타이틀로"로 연 경우: 새로 시작하기를 막는다(게임 상태 초기화 기능이 없음)

        public static TitleScreen Create(bool fromGame = false)
        {
            var go = new GameObject("TitleScreen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(TitleScreen));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var title = go.GetComponent<TitleScreen>();
            title._fromGame = fromGame;
            title.Build();
            EscapeCloser.Register(go, () => false, WindowRole.Popup); // 반드시 고른다: ESC는 먹기만 한다
            return title;
        }

        private void OnEnable()
        {
            GamePause.Push(this);
            SaveSystem.OnLoaded += OnLoaded; // 불러오기 창에서 불러오면 닫힌다
        }

        private void OnDestroy()
        {
            GamePause.Pop(this);
            SaveSystem.OnLoaded -= OnLoaded;
        }

        private void Start() => StartCoroutine(Fade(0f, 1f, FadeInSeconds, null));

        private void Update()
        {
            if (_background == null) return;
            float k = (1f - Mathf.Cos(Time.unscaledTime / ZoomPeriodSeconds * Mathf.PI * 2f)) * 0.5f;
            _background.localScale = Vector3.one * (1f + ZoomAmount * k);
        }

        // ---------- 버튼 ----------

        private void Continue()
        {
            if (_closing) return;
            bool ok = SaveSystem.Load();
            ToastLog.Show(ok ? "저장된 게임을 불러왔습니다" : "저장 파일을 읽지 못해 새로 시작합니다", false);
            Close(); // 불러오기 성공이면 OnLoaded가 먼저 닫기를 시작한다
        }

        private void NewGame()
        {
            if (_closing) return;
            if (SaveSystem.HasSave && !_newGameArmed)
            {
                _newGameArmed = true; // 한 번 더 눌러야 시작(이전 저장은 기록에 남지만 다음 자동 저장이 save.json을 덮는다)
                _newGameLabel.text = NewGameConfirmLabel;
                return;
            }
            if (SaveSystem.HasSave) ToastLog.Show("새로 시작합니다 · 이전 저장은 '불러오기'에 있습니다", false);
            TutorialGuide.Begin(); // 타이틀이 흐려지며 닫힌 뒤 보인다
            Close();
        }

        private void OnLoaded() => Close();

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            _group.blocksRaycasts = false;
            StartCoroutine(Fade(1f, 0f, FadeOutSeconds, () => Destroy(gameObject)));
        }

        private IEnumerator Fade(float from, float to, float seconds, System.Action done)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            _group.alpha = to;
            done?.Invoke();
        }

        // ---------- 화면 만들기 ----------

        private void Build()
        {
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;

            // 검은 바탕(그림 비율이 화면과 달라 남는 곳·페이드 중). 장식은 RawImage라 테마 자동 적용(Image만)이 판으로 바꾸지 않는다.
            var black = CreatePlain(transform, "Black", Color.black);
            black.raycastTarget = true; // 뒤 마을 클릭 막기
            Stretch(black.rectTransform);

            // 배경 그림: 화면을 가득 채우게(넘치는 쪽은 잘린다)
            var bg = CreateImage(transform, "Background", Color.white);
            var texture = Resources.Load<Texture2D>(BackgroundPath);
            if (texture != null)
            {
                bg.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = (float)texture.width / texture.height;
            }
            else
            {
                bg.color = UITheme.PanelFill;
                Stretch(bg.rectTransform);
            }
            _background = bg.rectTransform;

            // 왼쪽 그늘: 제목·버튼이 그림 위에서도 잘 읽히게
            var shade = new GameObject("LeftShade", typeof(RectTransform), typeof(RawImage));
            shade.transform.SetParent(transform, false);
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = Vector2.zero;
            shadeRect.anchorMax = new Vector2(0.55f, 1f);
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            var shadeImage = shade.GetComponent<RawImage>();
            shadeImage.texture = HorizontalFade(0.72f, 0f);
            shadeImage.color = Color.black;
            shadeImage.raycastTarget = false;

            // 반딧불: 그림 속 풀숲·양옆 큰 나무 쪽에서 떠다니며 깜빡인다(제목·버튼 아래)
            var fireflies = new GameObject("Fireflies", typeof(RectTransform), typeof(TitleFireflies));
            fireflies.transform.SetParent(transform, false);
            Stretch((RectTransform)fireflies.transform);

            // 제목
            var title = CreateText(transform, "길드 마스터", 96, TextAnchor.UpperLeft, UITheme.TitleText, bold: true);
            title.name = "Title";
            PlaceTopLeft(title.rectTransform, new Vector2(Left, -190f), new Vector2(900f, 120f));
            var outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.05f, 0.03f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);
            var drop = title.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.6f);
            drop.effectDistance = new Vector2(6f, -8f);

            var subtitle = CreateText(transform, "G U I L D   M A S T E R", UITheme.HeadingSize, TextAnchor.UpperLeft, UITheme.MutedText, bold: false);
            subtitle.name = "Tagline"; // 이름에 "Title"이 들어가면 테마가 제목색으로 바꾼다
            PlaceTopLeft(subtitle.rectTransform, new Vector2(Left + 6f, -316f), new Vector2(600f, 30f));
            subtitle.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            // 장식 줄(청동)
            var rule = CreatePlain(transform, "Rule", UITheme.BronzeLight);
            PlaceTopLeft(rule.rectTransform, new Vector2(Left + 4f, -356f), new Vector2(ButtonWidth, 2f));

            // 버튼
            float y = -392f;
            var continueButton = CreateButton("ContinueButton", "이어하기", Continue, ref y, out _);
            if (SaveSystem.HasSave)
                AddSubLabel(continueButton, FirstLine(SaveSystem.Describe()));
            else
            {
                continueButton.interactable = false;
                AddSubLabel(continueButton, "저장 없음");
            }
            CreateButton("LoadButton", "게임 불러오기", LoadGamePanel.Open, ref y, out _);
            var newGameButton = CreateButton("NewGameButton", NewGameLabel, NewGame, ref y, out _newGameLabel);
            if (_fromGame)
            {
                newGameButton.interactable = false;
                AddSubLabel(newGameButton, "게임 중에는 사용할 수 없음");
            }
            CreateButton("SettingsButton", "설정", SettingsPanel.Open, ref y, out _);

            // 버전
            var version = CreateText(transform, $"v{Application.version}", UITheme.SmallSize, TextAnchor.LowerRight, UITheme.MutedText, bold: false);
            var versionRect = version.rectTransform;
            versionRect.anchorMin = versionRect.anchorMax = versionRect.pivot = new Vector2(1f, 0f);
            versionRect.anchoredPosition = new Vector2(-24f, 18f);
            versionRect.sizeDelta = new Vector2(300f, 24f);
        }

        /// <summary>이어하기처럼 줄이 둘인 버튼: 이름을 위로 올리고 아래에 작은 설명.</summary>
        private void AddSubLabel(Button button, string text)
        {
            var label = button.transform.Find("Label").GetComponent<Text>();
            label.rectTransform.offsetMin = new Vector2(0f, 22f);
            label.rectTransform.offsetMax = new Vector2(0f, -4f);
            var sub = CreateText(button.transform, text, UITheme.SmallSize, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.6f), bold: false);
            sub.name = "SubLabel";
            var rect = sub.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(12f, 8f);
            rect.offsetMax = new Vector2(-12f, 28f);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            sub.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private Button CreateButton(string name, string label, UnityEngine.Events.UnityAction onClick, ref float y, out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            bool twoLines = name == "ContinueButton" || (name == "NewGameButton" && _fromGame);
            float height = twoLines ? ButtonHeight + 14f : ButtonHeight; // 설명 줄이 붙는 버튼은 한 줄만큼 높게
            PlaceTopLeft((RectTransform)go.transform, new Vector2(Left, y), new Vector2(ButtonWidth, height));
            y -= height + ButtonSpacing;

            var button = go.GetComponent<Button>();
            UITheme.StyleButton(button);
            button.onClick.AddListener(onClick);

            labelText = CreateText(go.transform, label, UITheme.HeadingSize, TextAnchor.MiddleCenter, Color.white, bold: true);
            labelText.name = "Label";
            Stretch(labelText.rectTransform);
            labelText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return button;
        }

        private static string FirstLine(string text)
        {
            int newline = text.IndexOf('\n');
            return newline < 0 ? text : text.Substring(0, newline);
        }

        private static Texture2D HorizontalFade(float left, float right)
        {
            const int w = 64;
            var pixels = new Color[w];
            for (int x = 0; x < w; x++) pixels[x] = new Color(1f, 1f, 1f, Mathf.Lerp(left, right, Mathf.SmoothStep(0f, 1f, x / (w - 1f))));
            var texture = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage CreatePlain(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color, bool bold)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = bold ? UITheme.BoldFont : UITheme.Font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void PlaceTopLeft(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// 메인화면 반딧불: 그림 속 풀숲(아래)과 양옆 큰 나무 아래에서 천천히 떠돌며 서서히 켜졌다 꺼진다.
    /// 꺼질 때마다 일부는 같은 영역의 새 자리로 옮겨 간다. 실제 시간이라 게임이 멈춰 있어도 움직인다.
    /// 위치는 화면 비율(0~1)로 정해 해상도가 달라도 그림의 같은 곳에 머문다.
    /// </summary>
    public class TitleFireflies : MonoBehaviour
    {
        private const int Count = 34;
        private const float MinSize = 16f, MaxSize = 32f; // 빛 번짐까지 포함한 크기(밝은 심은 그 1/4쯤)
        private const float MinSpeed = 10f, MaxSpeed = 25f;  // px/초(1920×1080 기준)
        private const float MinPeriod = 2f, MaxPeriod = 5f;  // 켜졌다 꺼지는 한 주기(초)
        private const float MaxAlpha = 1f;
        private const float MoveOnChance = 0.5f;              // 꺼질 때 새 자리로 옮길 확률
        private const float Leash = 0.06f;                    // 영역 밖으로 이만큼(화면 비율) 넘으면 안으로 끌어당긴다

        // 나타나는 영역(화면 비율 xMin, yMin, xMax, yMax)과 비중: 아래 풀숲 · 왼쪽 큰 나무 · 오른쪽 큰 나무
        private static readonly Rect[] Areas =
        {
            Rect.MinMaxRect(0f, 0.05f, 1f, 0.42f),
            Rect.MinMaxRect(0f, 0.35f, 0.16f, 0.75f),
            Rect.MinMaxRect(0.82f, 0.35f, 1f, 0.75f),
        };
        private static readonly float[] AreaWeights = { 0.6f, 0.2f, 0.2f };
        private static readonly Color[] Colors = { new Color(0.85f, 1f, 0.45f), new Color(1f, 0.9f, 0.5f) };

        private static Texture2D _glow;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _glow = null;

        private class Firefly
        {
            public RawImage Image;
            public Rect Area;
            public Vector2 Position;      // 화면 비율
            public float Speed, Size, Period, Phase;
            public float WanderA, WanderB; // 떠도는 방향을 만드는 사인 곡선 위상
            public Color Color;
            public int Cycle;              // 지금까지 몇 번 켜졌다 꺼졌는지(바뀔 때 새 자리)
        }

        private readonly Firefly[] _flies = new Firefly[Count];
        private RectTransform _area;

        private void Start()
        {
            _area = (RectTransform)transform;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Firefly", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                var image = go.GetComponent<RawImage>();
                image.texture = Glow;
                image.raycastTarget = false;

                var fly = new Firefly
                {
                    Image = image,
                    Area = PickArea(),
                    Speed = Random.Range(MinSpeed, MaxSpeed),
                    Size = Random.Range(MinSize, MaxSize),
                    Period = Random.Range(MinPeriod, MaxPeriod),
                    Phase = Random.value,
                    WanderA = Random.Range(0f, 100f),
                    WanderB = Random.Range(0f, 100f),
                    Color = Colors[Random.Range(0, Colors.Length)],
                };
                fly.Position = RandomPoint(fly.Area);
                fly.Cycle = CycleOf(fly, Time.unscaledTime);
                _flies[i] = fly;
            }
        }

        private void Update()
        {
            var size = _area.rect.size;
            if (size.x <= 0f || size.y <= 0f) return;
            float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            foreach (var fly in _flies)
            {
                if (fly == null) continue;

                // 깜빡임: 0 → 최대 → 0 (부드럽게). 꺼진 순간을 지나면 가끔 새 자리로.
                int cycle = CycleOf(fly, time);
                if (cycle != fly.Cycle)
                {
                    fly.Cycle = cycle;
                    if (Random.value < MoveOnChance)
                    {
                        fly.Area = PickArea();
                        fly.Position = RandomPoint(fly.Area);
                    }
                }
                float k = Mathf.Repeat(time / fly.Period + fly.Phase, 1f);
                float glow = Mathf.Sin(k * Mathf.PI);
                glow *= glow; // 켜진 시간은 짧고 꺼진 시간은 길게

                // 떠돌기: 사인 두 개를 겹친 방향으로 천천히, 영역에서 많이 벗어나면 안쪽으로
                float angle = Mathf.Sin(time * 0.37f + fly.WanderA) * 2.4f + Mathf.Sin(time * 0.71f + fly.WanderB) * 1.3f;
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * fly.Speed;
                var pull = Vector2.zero;
                if (fly.Position.x < fly.Area.xMin - Leash) pull.x = 1f;
                else if (fly.Position.x > fly.Area.xMax + Leash) pull.x = -1f;
                if (fly.Position.y < fly.Area.yMin - Leash) pull.y = 1f;
                else if (fly.Position.y > fly.Area.yMax + Leash) pull.y = -1f;
                velocity += pull * fly.Speed * 1.5f;
                fly.Position += new Vector2(velocity.x / size.x, velocity.y / size.y) * dt;

                var rect = fly.Image.rectTransform;
                rect.anchoredPosition = new Vector2(fly.Position.x * size.x, fly.Position.y * size.y);
                float s = fly.Size * (0.75f + 0.35f * glow);
                rect.sizeDelta = new Vector2(s, s);
                var c = fly.Color;
                c.a = MaxAlpha * glow;
                fly.Image.color = c;
            }
        }

        private static int CycleOf(Firefly fly, float time) => Mathf.FloorToInt(time / fly.Period + fly.Phase);

        private static Rect PickArea()
        {
            float r = Random.value;
            for (int i = 0; i < Areas.Length; i++)
            {
                if (r < AreaWeights[i]) return Areas[i];
                r -= AreaWeights[i];
            }
            return Areas[0];
        }

        private static Vector2 RandomPoint(Rect area) =>
            new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax));

        /// <summary>가운데 밝은 심 + 부드럽게 퍼지는 빛(흰색, RawImage.color로 색을 입힌다).</summary>
        private static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int size = 32;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                    float core = Mathf.Clamp01(1f - d * 3.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(halo * 0.7f + core * 1.2f));
                }
                _glow = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "TitleFireflyGlow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                _glow.SetPixels(pixels);
                _glow.Apply();
                return _glow;
            }
        }
    }
}
