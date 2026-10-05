using System.Collections;
using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>"진행" 버튼과 스페이스바로 GameClock을 3시간 진행시키고, 마을의 낮/밤 사이클(DayNightCycle)을
    /// 그 3시간만큼 약 1초 동안 부드럽게 흘려 해·그림자·가로등·창문 불빛이 함께 바뀌게 한다. DayControlBar에 붙는다.
    /// 그동안 마을 화면이 잠깐 어두워졌다 밝아지고 가운데에 "3시간 후…" 배너가 떠서 시간이 흐른 느낌을 준다.
    /// 가장 어두운 순간에 Midpoint를 알려 마을 쪽이 용병 위치를 바꾼다(막에 가려 순간이동이 안 보인다).
    /// 프로젝트가 Player Settings에서 Input System 패키지만 쓰도록 돼 있어(레거시 UnityEngine.Input 비활성)
    /// 새 Input System API를 사용한다.</summary>
    public class TimeAdvanceController : MonoBehaviour
    {
        private const float TransitionSeconds = 1f;
        private const float MaxDim = 0.55f;
        private const float BannerFadeSeconds = 0.8f;

        /// <summary>진행 전환 중 화면이 가장 어두운 순간(0.5초)에 한 번 불린다.</summary>
        public static event System.Action Midpoint;

        // Domain Reload가 꺼져 있어도 Play 진입마다 구독을 비운다(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => Midpoint = null;

        private const float SkipTransitionSeconds = 1.4f;
        private const int StepsPerDay = 8; // 3시간 × 8 = 하루

        private Text _clockText;
        private Button _button;
        private Button[] _skipButtons = new Button[0];
        private bool _isAdvancing;

        private Image _dim;
        private CanvasGroup _banner;
        private Text _bannerTitle;
        private Text _bannerSub;
        private Coroutine _bannerFade;

        public void Init(Text clockText, Button button)
        {
            _clockText = clockText;
            _button = button;
            UpdateClockText(GameClock.CurrentHour);
        }

        /// <summary>하루·4일·일주일 건너뛰기 버튼. 전환 중에는 진행 버튼과 함께 잠근다.</summary>
        public void SetSkipButtons(params Button[] buttons) => _skipButtons = buttons ?? new Button[0];

        private void SetLocked(bool locked)
        {
            if (_button != null) _button.interactable = !locked;
            foreach (var b in _skipButtons) if (b != null) b.interactable = !locked;
        }

        private void Start()
        {
            // 프리팹에 저장된 timeOfDay와 상관없이 게임 시계 시각(오전 6시 시작)에 맞춘다.
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                RequestAdvance();
        }

        public void RequestAdvance()
        {
            if (_isAdvancing) return;
            StartCoroutine(AdvanceRoutine());
        }

        private IEnumerator AdvanceRoutine()
        {
            _isAdvancing = true;
            SetLocked(true);
            int dayBefore = GameClock.CurrentDay;

            var cycle = DayNightCycle.Instance;
            float from = GameClock.CurrentHour;
            float to = from + GameClock.HoursPerStep; // 24를 넘어도 TimeOfDay setter가 Mathf.Repeat로 감는다.
            ShowBanner(from, to);

            bool midpointSent = false;
            for (float t = 0f; t < TransitionSeconds; t += Time.deltaTime)
            {
                float k = t / TransitionSeconds;
                float hour = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k));
                if (cycle != null) cycle.TimeOfDay = hour;
                UpdateClockText(hour);
                SetDim(MaxDim * Mathf.Sin(Mathf.PI * k));
                if (!midpointSent && k >= 0.5f)
                {
                    midpointSent = true;
                    Midpoint?.Invoke();
                }
                yield return null;
            }
            if (!midpointSent) Midpoint?.Invoke();

            GameClock.AdvanceTime();
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            UpdateClockText(GameClock.CurrentHour);
            SetDim(0f);
            _bannerFade = StartCoroutine(FadeBanner());

            SetLocked(false);
            _isAdvancing = false;

            // 자정을 넘겼으면 그날 보고서(파견 진행·있었던 일)를 띄우고 자동 저장한다.
            if (GameClock.CurrentDay != dayBefore)
            {
                DayReportPanel.Show(GameClock.CurrentDay, GameClock.CurrentDay);
                AutoSave();
            }
        }

        /// <summary>하루(1)·4일(4)·일주일(7)을 한 번에 건너뛴다. 시각은 그대로, 끝나면 그동안의 날짜별 보고서를 띄운다.</summary>
        public void RequestSkipDays(int days)
        {
            if (_isAdvancing || days <= 0) return;
            StartCoroutine(SkipRoutine(days));
        }

        private IEnumerator SkipRoutine(int days)
        {
            _isAdvancing = true;
            SetLocked(true);
            int startDay = GameClock.CurrentDay;
            float hour = GameClock.CurrentHour;

            EnsureOverlay();
            if (_bannerFade != null) StopCoroutine(_bannerFade);
            _bannerFade = null;
            _bannerTitle.text = days == 1 ? "하루 후…" : days == 7 ? "일주일 후…" : $"{days}일 후…";
            _bannerSub.text = $"{startDay}일차 {DayNightCycle.FormatTime(hour)}  →  {startDay + days}일차 {DayNightCycle.FormatTime(hour)}";
            _banner.alpha = 1f;

            bool skipped = false;
            for (float t = 0f; t < SkipTransitionSeconds; t += Time.deltaTime)
            {
                float k = t / SkipTransitionSeconds;
                SetDim(MaxDim * Mathf.Sin(Mathf.PI * k));
                if (!skipped && k >= 0.5f)
                {
                    skipped = true;
                    SkipNow(days);
                }
                yield return null;
            }
            if (!skipped) SkipNow(days);

            SetDim(0f);
            _bannerFade = StartCoroutine(FadeBanner());
            SetLocked(false);
            _isAdvancing = false;
            DayReportPanel.Show(startDay + 1, GameClock.CurrentDay);
            AutoSave();
        }

        private static void AutoSave()
        {
            if (SaveSystem.Save()) ToastLog.Show("자동 저장됨");
        }

        /// <summary>불러오기 직후: 낮/밤과 시각 표시를 게임 시계에 맞춘다.</summary>
        public void SyncToClock()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            UpdateClockText(GameClock.CurrentHour);
        }

        // 가장 어두운 순간: 3시간 진행을 하루 8번씩 즉시 반복한다. 자정마다 파견 진행·습격·여관 회복이 그대로 일어나 DailyLog에 쌓인다.
        private void SkipNow(int days)
        {
            for (int i = 0; i < days * StepsPerDay; i++) GameClock.AdvanceTime();
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            UpdateClockText(GameClock.CurrentHour);
            Midpoint?.Invoke(); // 마을 사람 자리 섞기
        }

        // 자정을 넘는 중에도 일차는 GameClock 기준이라 0시에 확정될 때 바뀐다.
        private void UpdateClockText(float hour)
        {
            if (_clockText != null)
                _clockText.text = $"{GameClock.CurrentDay}일차 · {DayNightCycle.FormatTime(hour)}";
        }

        private void ShowBanner(float from, float to)
        {
            EnsureOverlay();
            if (_bannerFade != null) StopCoroutine(_bannerFade);
            _bannerFade = null;

            bool overnight = to >= 24f;
            _bannerTitle.text = overnight ? "밤이 지나…" : $"{GameClock.HoursPerStep:0}시간 후…";
            _bannerSub.text = overnight
                ? $"{GameClock.CurrentDay + 1}일차 · {DayNightCycle.FormatTime(to)}"
                : $"{DayNightCycle.FormatTime(from)}  →  {DayNightCycle.FormatTime(to)}";
            _banner.alpha = 1f;
        }

        private IEnumerator FadeBanner()
        {
            for (float t = 0f; t < BannerFadeSeconds; t += Time.deltaTime)
            {
                _banner.alpha = 1f - t / BannerFadeSeconds;
                yield return null;
            }
            _banner.alpha = 0f;
            _bannerFade = null;
        }

        private void SetDim(float alpha)
        {
            if (_dim != null) _dim.color = new Color(0f, 0f, 0f, alpha);
        }

        /// <summary>
        /// 마을만 덮는 어두운 막 + 가운데 배너. sortingOrder -2라 이름표(-1)·메뉴(0)보다 아래에 깔리고,
        /// Raycaster가 없어 클릭을 막지 않는다.
        /// </summary>
        private void EnsureOverlay()
        {
            if (_dim != null) return;

            var canvasGO = new GameObject("TimeSkipOverlay", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var dimGO = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dimGO.transform.SetParent(canvasGO.transform, false);
            var dimRect = dimGO.GetComponent<RectTransform>();
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            _dim = dimGO.GetComponent<Image>();
            _dim.raycastTarget = false;
            SetDim(0f);

            var bannerGO = new GameObject("TimeSkipBanner", typeof(RectTransform), typeof(CanvasGroup));
            bannerGO.transform.SetParent(canvasGO.transform, false);
            var bannerRect = bannerGO.GetComponent<RectTransform>();
            bannerRect.sizeDelta = new Vector2(800f, 120f);
            bannerRect.anchoredPosition = new Vector2(0f, 60f);
            _banner = bannerGO.GetComponent<CanvasGroup>();
            _banner.alpha = 0f;
            _banner.blocksRaycasts = false;
            _banner.interactable = false;

            _bannerTitle = CreateBannerText(bannerGO.transform, "BannerTitle", 34, FontStyle.Bold, UITheme.TitleText, 22f);
            _bannerSub = CreateBannerText(bannerGO.transform, "BannerSub", 19, FontStyle.Normal, UITheme.BodyText, -26f);
        }

        private static Text CreateBannerText(Transform parent, string name, int size, FontStyle style, Color color, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(800f, 48f);
            rect.anchoredPosition = new Vector2(0f, y);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}
