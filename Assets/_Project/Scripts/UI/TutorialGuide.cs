using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 새 게임 튜토리얼: 기본 순환(용병 고용 → 퀘스트 수락·파견 → 시간 진행 → 도착·전투 → 결과)을 차례로 안내한다.
    /// 화면을 막지 않고, 안내 상자와 깜빡이는 금빛 테두리로 진짜 버튼을 가리킨다. 플레이어가 그 일을 하면 다음 단계로 넘어간다
    /// (조건은 게임 상태를 매 프레임 확인 — 이미 해 둔 단계는 바로 넘어간다). 언제든 건너뛸 수 있다.
    /// 진행 단계는 PlayerPrefs("TutorialStep")에 둬서 세이브 파일과 따로이고, 껐다 이어하기 해도 그 단계부터 다시 안내한다.
    /// MainScene마다 새로 만든다(씬과 함께 사라짐). 정렬 5: HUD(3)·중요 알림(4) 위, 일시정지 메뉴(6)·설정(7) 아래.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        private const string PrefKey = "TutorialStep";
        private const int Done = 99;
        private const int LastStep = 7;
        private const float BoxWidth = 820f, BoxHeight = 200f;
        private const float BoxMargin = 110f;     // 화면 아래·위에서 띄우는 거리(아래 오른쪽 건강·관계 버튼, 위 HUD 줄을 피한다)
        private const float FramePadding = 5f, FrameThickness = 4f;
        private static readonly Color FrameColor = new Color(1f, 0.78f, 0.35f);

        private struct Step
        {
            public string Title, Body;
            public string[] Targets;   // 가리킬 것(앞에서부터 켜져 있는 첫 번째)
            public bool NeedsButton;   // "다음"/"끝내기"로만 넘어간다
        }

        private static readonly Step[] Steps =
        {
            default, // 0번은 쓰지 않는다
            new Step
            {
                Title = "길드에 오신 것을 환영합니다",
                Body = "당신은 이제 모험가 길드의 길드장입니다.\n용병을 모아 퀘스트를 보내고, 골드와 명성을 벌어 길드를 키우세요.\n잠깐 시간을 멈춰 두었습니다. 기본 흐름을 함께 해 봅시다.",
                NeedsButton = true,
            },
            new Step
            {
                Title = "1. 용병 고용",
                Body = "왼쪽 위 [용병시장]을 눌러 용병을 한 명 데려오세요(처음 골드 300).\n[협상]을 누르면 주급·계약 기간·계약금을 제안하고, 용병이 받아들이면 계약됩니다.\n너무 짜게 부르면 기분이 상해 협상이 깨지니 버튼 위 귀띔을 참고하세요.",
                Targets = new[] { "MarketPanel", "용병시장Button" },
            },
            new Step
            {
                Title = "2. 퀘스트 보내기",
                Body = "[퀘스트]를 열고 해낼 만한 퀘스트의 [수락]을 누르세요.\n처음엔 퀘스트 게시판이 없어, 며칠마다 마을에 오는 의뢰 중개인이 가져온 의뢰만 받을 수 있습니다(와 있는 날 하루만).\n파티 창이 열리면 보낼 용병의 [선택]을 누르고 [파견 시작]을 누릅니다.\n밤(22시~5시)에는 출전할 수 없습니다.",
                Targets = new[] { "PartyPanel", "QuestPanel", "퀘스트Button" },
            },
            new Step
            {
                Title = "3. 시간 넘기기",
                Body = "파견대가 목적지까지 가는 데 시간이 걸립니다. 시간은 저절로도 흐르지만,\n오른쪽 위 [진행]을 누르면 바로 넘길 수 있습니다. 아래 막대로 몇 시간 넘길지 정하세요.",
                Targets = new[] { "AdvanceDayButton" },
            },
            new Step
            {
                Title = "4. 목적지 도착",
                Body = "파견대가 도착했습니다! [자동 진행]은 바로 결과를 받고,\n[직접 진행]은 전투 화면에서 분대장에게 직접 명령합니다. [나중에]는 우편함에서 다시 고를 수 있습니다.",
                Targets = new[] { "ImportantAlert", "MailboxButton" },
            },
            new Step
            {
                Title = "5. 건물 짓기",
                Body = "길드는 빈 숲에서 시작합니다. 왼쪽 위 [건설] → [짓기]를 누르고 빈 땅을 클릭하면 세워집니다(오른쪽 클릭·ESC는 취소).\n건물마다 금화와 재료가 들지만 길드 천막과 첫 용병 텐트는 무료로 바로 세울 수 있습니다. 용병 텐트는 한 채에 3명이 묵고, 용병이 늘면 재료로 더 지을 수 있습니다. 길드 천막을 세우면 길드 창고·시설을 쓸 수 있고, 돌빛 등급이 되면 퀘스트 게시판을 세워 중개인을 기다리지 않고 의뢰를 볼 수 있습니다.\n재료는 원정 몬스터(게시판에 얻는 재료 표시)·숲 벌목([건설] → [벌목])·며칠마다 오는 떠돌이 상인·근처 길드 교역·의뢰 보상으로 얻습니다. 지은 건물은 그 건물을 눌러 뜬 창이나 [건설] 창의 [옮기기]로 옮길 수 있습니다(무료 · [옮기기 취소]로 되돌리기).\n돌빛 등급이 되면 여관을 지어 용병을 밤에 푹 쉬게 할 수 있습니다(그 전엔 용병 텐트, 자리가 모자라면 야영). 여관이 없으면 길드 본부에 3명까지 묵어 여관처럼 쉽니다. 길드 등급이 오르면 대장간·훈련소·병원도 열립니다(등급이 오르면 [건설] 창에 새 건물·장식품이 나타납니다).\n처음엔 견습 용병단입니다. 길드 홀을 지으면 정식 길드가 되고, 그래야 동빛 위 등급으로 오릅니다.\n[건설] 창의 [장식품]에서 소품·울타리·성벽을 놓을 수 있습니다(옮기기 모드에서 들고 [치우기]로 치움).\n장식은 마을 매력을 올려 용병 사기·명성·시장에 도움이 되고, 용병이 벤치·모닥불·분수에서 쉬고 허수아비로 훈련합니다.",
                Targets = new[] { "BuildMenuPanel", "건설Button" },
                NeedsButton = true,
            },
            new Step
            {
                Title = "6. 이제 길드를 키워 보세요",
                Body = "결과와 소식은 오른쪽 아래 우편함에 쌓입니다.\n지은 건물을 누르면 훈련소·병원·성당·온천 등을 쓸 수 있고, [건강]·[관계]로 용병 상태를 봅니다.\nESC 메뉴에 설정과 튜토리얼 다시 보기가 있습니다.",
                Targets = new[] { "MailboxButton" },
                NeedsButton = true,
            },
        };

        private static TutorialGuide _instance;

        private int _step;
        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _box;
        private Text _title, _body, _counter, _nextLabel;
        private Button _next, _skip;
        private RectTransform _frame;
        private Image[] _frameEdges;
        private float _titleCheck;
        private bool _paused;

        private static int Saved
        {
            get { try { return PlayerPrefs.GetInt(PrefKey, 0); } catch { return 0; } }
            set { try { PlayerPrefs.SetInt(PrefKey, value); PlayerPrefs.Save(); } catch { /* 저장 못 해도 이번 판은 진행 */ } }
        }

        /// <summary>새 게임·다시 보기: 처음 단계부터.</summary>
        public static void Begin()
        {
            Saved = 1;
            Show(1);
        }

        /// <summary>진행 중인 튜토리얼이 있으면 그 단계부터 다시 띄운다(MainScene을 다시 불러올 때마다).</summary>
        public static void ResumeIfActive()
        {
            int step = Saved;
            if (step >= 1 && step <= LastStep) Show(step);
        }

        private static void Show(int step)
        {
            if (_instance == null)
                _instance = new GameObject(nameof(TutorialGuide), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TutorialGuide))
                    .GetComponent<TutorialGuide>();
            _instance.Enter(step);
        }

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)transform;
            BuildFrame();
            BuildBox();
            UIThemeApplier.ApplyNow(_box);
        }

        private void OnDestroy()
        {
            SetPaused(false);
            if (_instance == this) _instance = null;
        }

        private void Enter(int step)
        {
            _step = step;
            Saved = step;
            var s = Steps[step];
            _title.text = s.Title;
            _body.text = s.Body;
            _counter.text = step == 1 ? "" : $"{step - 1} / {LastStep - 1}";
            _next.gameObject.SetActive(s.NeedsButton);
            _nextLabel.text = step == LastStep ? "끝내기" : "다음";
            _skip.gameObject.SetActive(step != LastStep); // 마지막엔 [끝내기]만
            ((RectTransform)_next.transform).anchoredPosition = new Vector2(step == LastStep ? -20f : -140f, 14f);
            SetPaused(step == 1); // 환영 인사 동안만 시간을 멈춘다
        }

        private void Finish()
        {
            Saved = Done;
            Destroy(gameObject);
        }

        private void Advance()
        {
            if (_step >= LastStep) Finish();
            else Enter(_step + 1);
        }

        private void SetPaused(bool paused)
        {
            if (paused == _paused) return;
            _paused = paused;
            if (paused) GamePause.Push(this);
            else GamePause.Pop(this);
        }

        // ---------- 단계 조건 ----------

        private static bool HasMercenary => PlayerParty.Instance.Members.Count > 0;
        private static bool HasExpedition => ExpeditionLog.Instance.Active.Count > 0;
        private static bool HasArrived => ExpeditionLog.Instance.Active.Any(e => e.IsReady);

        /// <summary>이 단계에서 할 일을 이미 했는가. 앞 단계를 건너뛰어 해 둔 경우(먼저 파견까지 끝냄 등)도 넘어간다.</summary>
        private bool StepDone() => _step switch
        {
            2 => HasMercenary,
            3 => HasExpedition,
            4 => HasArrived,
            5 => !HasArrived && (!HasExpedition || ExpeditionLog.Instance.Active.Any(e => e.IsReturning)), // 전투를 마쳤다(결과·귀환)
            _ => false,
        };

        private void Update()
        {
            // 타이틀 화면이 떠 있는 동안은 숨긴다(새 게임은 타이틀이 흐려지며 닫힌 뒤 보인다).
            _titleCheck -= Time.unscaledDeltaTime;
            if (_titleCheck <= 0f)
            {
                _titleCheck = 0.25f;
                _canvas.enabled = FindFirstObjectByType<TitleScreen>() == null;
            }
            if (!_canvas.enabled) return;

            if (!Steps[_step].NeedsButton && StepDone()) Advance();
            if (_instance == null) return; // 끝났다
            UpdateFrame();
        }

        // ---------- 강조 테두리 ----------

        private void BuildFrame()
        {
            var go = new GameObject("Highlight", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _frame = (RectTransform)go.transform;
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frameEdges = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(_frame, false);
                var rect = (RectTransform)edge.transform;
                bool horizontal = i < 2;
                rect.anchorMin = horizontal ? new Vector2(0f, i == 0 ? 1f : 0f) : new Vector2(i == 2 ? 0f : 1f, 0f);
                rect.anchorMax = horizontal ? new Vector2(1f, i == 0 ? 1f : 0f) : new Vector2(i == 2 ? 0f : 1f, 1f);
                rect.sizeDelta = horizontal ? new Vector2(FrameThickness * 2f, FrameThickness) : new Vector2(FrameThickness, 0f);
                var image = edge.GetComponent<Image>();
                image.sprite = WhiteSprite; // 자기 그림이 있어야 UIThemeApplier가 청동 판으로 칠하지 않는다
                image.raycastTarget = false;
                image.color = FrameColor;
                _frameEdges[i] = image;
            }
        }

        private static Sprite _white;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                _white = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
                _white.name = "TutorialFrame";
                return _white;
            }
        }

        private RectTransform FindTarget()
        {
            var targets = Steps[_step].Targets;
            if (targets == null) return null;
            foreach (var name in targets)
            {
                var go = GameObject.Find(name);
                if (go == null || !go.activeInHierarchy) continue;
                // 중요 알림창은 캔버스 이름이라 그 안의 판을 가리킨다(화면 전체를 덮는 어두운 막은 건너뛴다)
                if (go.GetComponent<Canvas>() != null)
                {
                    var screen = ((RectTransform)go.transform).rect.size;
                    var panel = go.GetComponentsInChildren<Image>().FirstOrDefault(i =>
                        i.gameObject != go && i.enabled && i.rectTransform.rect.width < screen.x * 0.9f && i.rectTransform.rect.height < screen.y * 0.9f);
                    if (panel == null) continue;
                    return panel.rectTransform;
                }
                return go.transform as RectTransform;
            }
            return null;
        }

        private void UpdateFrame()
        {
            var target = FindTarget();
            bool show = target != null;
            _frame.gameObject.SetActive(show);
            float targetY = 0.5f; // 화면 높이 비율(가린 것을 피해 상자 자리를 고른다)
            if (show)
            {
                var corners = new Vector3[4];
                target.GetWorldCorners(corners);
                Vector2 min = _root.InverseTransformPoint(corners[0]);
                Vector2 max = _root.InverseTransformPoint(corners[2]);
                _frame.anchoredPosition = (min + max) * 0.5f;
                _frame.sizeDelta = max - min + Vector2.one * FramePadding * 2f;
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
                foreach (var edge in _frameEdges)
                {
                    var c = FrameColor;
                    c.a = pulse;
                    edge.color = c;
                }
                targetY = (_frame.anchoredPosition.y + _root.rect.height * 0.5f) / Mathf.Max(1f, _root.rect.height);
            }
            PlaceBox(show && targetY < 0.4f && Mathf.Abs(_frame.anchoredPosition.x) < BoxWidth * 0.5f + _frame.sizeDelta.x * 0.5f);
            if (show) SlideAway();
        }

        /// <summary>기본은 화면 아래 가운데. 가리키는 것이 아래쪽 가운데라 겹치면 위로.</summary>
        private void PlaceBox(bool top)
        {
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, top ? 1f : 0f);
            _box.anchoredPosition = new Vector2(0f, top ? -BoxMargin - 20f : BoxMargin);
        }

        /// <summary>가리키는 창(파티 창 등)과 옆으로 겹치면 상자를 반대쪽으로 민다(화면 밖으로는 안 나가게).</summary>
        private void SlideAway()
        {
            float half = _root.rect.width * 0.5f;
            float frameLeft = _frame.anchoredPosition.x - _frame.sizeDelta.x * 0.5f;
            float frameRight = _frame.anchoredPosition.x + _frame.sizeDelta.x * 0.5f;
            float boxLeft = -BoxWidth * 0.5f, boxRight = BoxWidth * 0.5f;
            if (frameRight <= boxLeft || frameLeft >= boxRight) return;                       // 옆으로 안 겹친다
            if (frameLeft <= -half + 40f && frameRight >= half - 40f) return;                 // 화면 폭 거의 전체(시장·퀘스트 창): 그대로
            const float gap = 16f;
            float x = _frame.anchoredPosition.x > 0f ? frameLeft - gap - BoxWidth * 0.5f : frameRight + gap + BoxWidth * 0.5f;
            float limit = half - BoxWidth * 0.5f - 16f;
            x = Mathf.Clamp(x, -limit, limit);
            _box.anchoredPosition = new Vector2(x, _box.anchoredPosition.y);
        }

        // ---------- 안내 상자 ----------

        private void BuildBox()
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            _box = (RectTransform)go.transform;
            _box.sizeDelta = new Vector2(BoxWidth, BoxHeight);
            var image = go.GetComponent<Image>();
            image.sprite = UITheme.Panel;
            image.type = Image.Type.Sliced;
            PlaceBox(false);

            _title = CreateText(_box, "", UITheme.HeadingSize, UITheme.TitleText, TextAnchor.UpperLeft);
            _title.name = "Title";
            _title.font = UITheme.BoldFont;
            Place(_title.rectTransform, new Vector2(24f, -16f), new Vector2(BoxWidth - 140f, 28f));

            _counter = CreateText(_box, "", UITheme.SmallSize, UITheme.MutedText, TextAnchor.UpperRight);
            Place(_counter.rectTransform, new Vector2(BoxWidth - 124f, -18f), new Vector2(100f, 24f));

            _body = CreateText(_box, "", UITheme.BodySize, UITheme.BodyText, TextAnchor.UpperLeft);
            _body.lineSpacing = 1.15f;
            Place(_body.rectTransform, new Vector2(24f, -52f), new Vector2(BoxWidth - 48f, 96f));

            _skip = CreateButton(_box, "건너뛰기", Finish);
            PlaceBottomRight((RectTransform)_skip.transform, new Vector2(-20f, 14f), new Vector2(110f, 34f));
            _next = CreateButton(_box, "다음", Advance);
            _nextLabel = _next.GetComponentInChildren<Text>();
            PlaceBottomRight((RectTransform)_next.transform, new Vector2(-140f, 14f), new Vector2(110f, 34f));
        }

        private static Text CreateText(Transform parent, string content, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = UITheme.Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var button = go.GetComponent<Button>();
            UITheme.StyleButton(button);
            button.onClick.AddListener(onClick);
            var text = CreateText(go.transform, label, UITheme.BodySize, UITheme.BodyText, TextAnchor.MiddleCenter);
            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return button;
        }

        /// <summary>상자 왼쪽 위 기준 배치.</summary>
        private static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        private static void PlaceBottomRight(RectTransform rect, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
