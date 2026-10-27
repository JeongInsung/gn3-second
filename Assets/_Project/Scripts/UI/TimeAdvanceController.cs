using System.Collections;
using GN3.Quests;
using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>"진행" 버튼과 스페이스바로 GameClock을 슬라이더로 정한 시간(1~24시간, 처음 3시간)만큼 진행시키고,
    /// 마을의 낮/밤 사이클(DayNightCycle)을 그만큼 1~2.5초 동안 부드럽게 흘려 해·그림자·가로등·창문 불빛이 함께 바뀌게 한다. DayControlBar에 붙는다.
    /// 그동안 마을 화면이 잠깐 어두워졌다 밝아지고 가운데에 "N시간 후…" 배너가 떠서 시간이 흐른 느낌을 준다.
    /// 가장 어두운 순간에 Midpoint를 알려 마을 쪽이 용병 위치를 바꾼다(막에 가려 순간이동이 안 보인다).
    /// 프로젝트가 Player Settings에서 Input System 패키지만 쓰도록 돼 있어(레거시 UnityEngine.Input 비활성)
    /// 새 Input System API를 사용한다.
    /// 가만히 둬도 시간이 흐른다(게임 1시간 = 실제 30초). 2배속 버튼은 Time.timeScale을 2로 올려 시계와 함께 캐릭터·애니메이션·파티클까지 두 배로 돌린다(UI 연출은 unscaled라 그대로).
    /// 전환 중이거나 게임이 멈춘 동안(GamePause — 중요 알림창)은 시간이 흐르지 않고 timeScale도 0이다. 하루 보고서는 우편함(Mailbox)으로 간다.</summary>
    public class TimeAdvanceController : MonoBehaviour
    {
        // 진행 전환 길이: 넘기는 시간이 길수록 길게(24시간을 1초에 돌리면 해가 너무 휙 돈다).
        private const float MinTransitionSeconds = 1f;
        private const float MaxTransitionSeconds = 2.5f;
        private const float TransitionBaseSeconds = 0.6f;
        private const float TransitionSecondsPerHour = 0.08f;

        /// <summary>진행 버튼 한 번에 넘길 수 있는 시간(진행 버튼 아래 슬라이더).</summary>
        public const int MinAdvanceHours = 1;
        public const int MaxAdvanceHours = 24;
        private const float MaxDim = 0.55f;
        private const float BannerFadeSeconds = 0.8f;

        /// <summary>진행 전환 중 화면이 가장 어두운 순간(0.5초)에 한 번 불린다.</summary>
        public static event System.Action Midpoint;

        // Domain Reload가 꺼져 있어도 Play 진입마다 구독을 비운다(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Midpoint = null;
            IsAdvancing = false;
            IsSkippingDays = false;
        }

        // 건너뛰기 연출: 하루 1.4초, 하루 더할 때마다 0.5초, 최대 3초(4일 2.9초 · 일주일 3초)
        private const float SkipTransitionSeconds = 1.4f;
        private const float SkipSecondsPerExtraDay = 0.5f;
        private const float SkipMaxSeconds = 3f;
        private const float ArrivalUndimSeconds = 0.3f; // 도착·중요 알림으로 멈췄을 때 남은 어둠을 걷는 시간
        private const float RealSecondsPerGameHour = 30f; // 1배속에서 게임 1시간이 흐르는 실제 시간
        private static readonly Color SpeedNormalColor = new Color(0.3f, 0.3f, 0.35f, 0.9f);
        private static readonly Color SpeedFastColor = new Color(0.75f, 0.5f, 0.15f, 0.95f);

        private Text _clockText;
        private TooltipTrigger _clockTip; // 마우스를 올리면 "N일차"
        private Button _button;
        private Selectable[] _skipButtons = new Selectable[0];
        private Selectable _hoursSlider;
        private int _advanceHours = (int)GameClock.HoursPerStep;
        private bool _isAdvancing;
        /// <summary>진행·건너뛰기 연출 중인지(그 사이 마을은 걸어 들어가기·나가기 연출을 하지 않는다 — VillagePartyPresenter).</summary>
        public static bool IsAdvancing { get; private set; }
        /// <summary>하루·4일·일주일 건너뛰기 중인지(그 사이 마을 캐릭터를 숨기고, 끝나면 무작위 자리에 둔다 — VillagePartyPresenter).</summary>
        public static bool IsSkippingDays { get; private set; }
        private string _skipPeriod = "하루"; // 건너뛰기 기간 글자("하루"/"4일"/"일주일") — 모아 둔 알림 한 줄에 쓴다
        private Mailbox.Mail _stopMail;// 진행·건너뛰기 중 처음 생긴 중요 편지(도착·전멸·사망·주급 미지급…). 생기면 거기서 멈춘다

        /// <summary>진행 버튼·스페이스바가 한 번에 넘길 시간(시). 슬라이더로 1~24시간.</summary>
        public int AdvanceHours => _advanceHours;

        /// <summary>넘길 시간이 바뀌었을 때(버튼 글자 갱신용).</summary>
        public event System.Action<int> AdvanceHoursChanged;
        private int _speed = 1;
        private Button _speedButton;

        private Image _dim;
        private CanvasGroup _banner;
        private Text _bannerTitle;
        private Text _bannerSub;
        private Coroutine _bannerFade;

        public void Init(Text clockText, Button button)
        {
            _clockText = clockText;
            _clockTip = clockText != null ? clockText.GetComponent<TooltipTrigger>() : null;
            _button = button;
            UpdateClockText(GameClock.CurrentHour);
        }

        /// <summary>진행 버튼 우클릭 메뉴의 하루·4일·일주일 건너뛰기 버튼(AdvanceButton). 전환 중에는 진행 버튼과 함께 잠근다.</summary>
        public void SetSkipButtons(params Selectable[] buttons) => _skipButtons = buttons ?? new Selectable[0];

        /// <summary>진행 버튼 아래 시간 슬라이더. 전환 중에는 진행 버튼과 함께 잠근다.</summary>
        public void SetHoursSlider(Selectable slider) => _hoursSlider = slider;

        public void SetAdvanceHours(int hours)
        {
            hours = Mathf.Clamp(hours, MinAdvanceHours, MaxAdvanceHours);
            if (hours == _advanceHours) return;
            _advanceHours = hours;
            AdvanceHoursChanged?.Invoke(hours);
        }

        private void SetLocked(bool locked)
        {
            if (_button != null) _button.interactable = !locked;
            if (_hoursSlider != null) _hoursSlider.interactable = !locked;
            foreach (var b in _skipButtons) if (b != null) b.interactable = !locked;
        }

        private void Start()
        {
            // 프리팹에 저장된 timeOfDay와 상관없이 게임 시계 시각(오전 6시 시작)에 맞춘다.
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            Time.timeScale = _speed; // 버튼 상태와 배속을 맞춘다(시작은 1배속)
        }

        // 씬을 벗어나거나 Play를 끌 때 2배속이 남지 않게
        private void OnDestroy()
        {
            Time.timeScale = 1f;
            Mailbox.ImportantPosted -= MarkStop; // 넘기는 도중 씬이 바뀌어도 구독이 남지 않게
        }

        /// <summary>배속 버튼(1 → 2 → 4배속, F). 지금 속도를 글자로 보여 주고 빠르면 강조색, 일시정지(P)면 "일시정지".</summary>
        public void SetSpeedButton(Button button)
        {
            _speedButton = button;
            if (button != null && button.GetComponent<TooltipTrigger>() == null)
                button.gameObject.AddComponent<TooltipTrigger>().Text = "배속 바꾸기 (F) · 일시정지 (P)";
            UpdateSpeedButton();
        }

        private static readonly int[] Speeds = { 1, 2, 4 };

        /// <summary>1 → 2 → 4 → 1배속. 일시정지 중에 누르면 풀린다.</summary>
        public void ToggleSpeed()
        {
            if (_userPaused) _userPaused = false;
            else _speed = Speeds[(System.Array.IndexOf(Speeds, _speed) + 1) % Speeds.Length];
            ApplyTimeScale(); // 시계·캐릭터·Animator·파티클이 함께 빨라진다
            UpdateSpeedButton();
        }

        /// <summary>플레이어가 건 일시정지(P). 시계·캐릭터가 멈춘다. 진행·배속을 누르면 풀린다.</summary>
        public void TogglePause()
        {
            _userPaused = !_userPaused;
            ApplyTimeScale();
            UpdateSpeedButton();
        }

        private bool _userPaused;

        // 중요 알림창(GamePause)이나 일시정지면 시계·캐릭터·애니메이션이 모두 멈춘다.
        private void ApplyTimeScale() => Time.timeScale = GamePause.IsPaused || _userPaused ? 0f : _speed;

        private void UpdateSpeedButton()
        {
            if (_speedButton == null) return;
            var label = _speedButton.GetComponentInChildren<Text>();
            if (label != null) label.text = _userPaused ? "일시정지" : $"{_speed}배속";
            var image = _speedButton.GetComponent<Image>();
            if (image != null) image.color = _speed == 1 && !_userPaused ? SpeedNormalColor : SpeedFastColor;
        }

        private void Update()
        {
            ApplyTimeScale();
            var keyboard = Keyboard.current;
            if (keyboard != null && !VillageHotkeys.IsTyping)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) RequestAdvance();
                if (keyboard.fKey.wasPressedThisFrame) ToggleSpeed();
                if (keyboard.pKey.wasPressedThisFrame) TogglePause();
            }
            FlowTime();
        }

        /// <summary>가만히 둬도 시간이 흐른다. 진행·건너뛰기 전환 중이거나 게임이 멈춰 있으면 멈춘다.</summary>
        private void FlowTime()
        {
            if (_isAdvancing || GamePause.IsPaused || _userPaused) return;
            int dayBefore = GameClock.CurrentDay;
            GameClock.AdvanceHours(Time.deltaTime / RealSecondsPerGameHour); // deltaTime에 배속(timeScale)이 이미 들어 있다
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            UpdateClockText(GameClock.CurrentHour);

            // 자정을 넘겼으면 진행 버튼과 같이 그날 보고서를 우편함에 넣고 자동 저장한다.
            if (GameClock.CurrentDay != dayBefore)
            {
                Mailbox.PostReport(GameClock.CurrentDay, GameClock.CurrentDay);
                AutoSave();
            }
        }

        public void RequestAdvance()
        {
            if (_isAdvancing || GamePause.IsPaused) return;
            if (_userPaused) // 진행하면 일시정지는 풀린다
            {
                _userPaused = false;
                ApplyTimeScale();
                UpdateSpeedButton();
            }
            StartCoroutine(AdvanceRoutine());
        }

        private IEnumerator AdvanceRoutine()
        {
            _isAdvancing = IsAdvancing = true; // 첫 yield 전에 잠가 두 번 눌리지 않게
            SetLocked(true);
            int hours = _advanceHours;
            float from = GameClock.CurrentHour;
            ShowBanner(from, from + hours, hours);
            float duration = Mathf.Clamp(TransitionBaseSeconds + hours * TransitionSecondsPerHour, MinTransitionSeconds, MaxTransitionSeconds);
            yield return FlowRoutine(hours, duration);
        }

        /// <summary>하루(1)·4일(4)·일주일(7)을 한 번에 건너뛴다. 끝나면 그동안의 날짜별 보고서를 띄운다. 중간에 중요한 일(파견대 도착·전멸·사망·주급 미지급 등)이 생기면 거기서 멈춘다.</summary>
        public void RequestSkipDays(int days)
        {
            if (_isAdvancing || GamePause.IsPaused || days <= 0) return;
            StartCoroutine(SkipRoutine(days));
        }

        private IEnumerator SkipRoutine(int days)
        {
            _isAdvancing = IsAdvancing = true;
            IsSkippingDays = true;
            SetLocked(true);
            int startDay = GameClock.CurrentDay;
            float hour = GameClock.CurrentHour;

            EnsureOverlay();
            if (_bannerFade != null) StopCoroutine(_bannerFade);
            _bannerFade = null;
            _skipPeriod = days == 1 ? "하루" : days == 7 ? "일주일" : $"{days}일";
            _bannerTitle.text = $"{_skipPeriod} 후…";
            _bannerSub.text = $"{GameCalendar.Format(startDay)} {DayNightCycle.FormatTime(hour)}  →  {GameCalendar.Format(startDay + days)} {DayNightCycle.FormatTime(hour)}";
            _banner.alpha = 1f;

            float duration = Mathf.Min(SkipMaxSeconds, SkipTransitionSeconds + (days - 1) * SkipSecondsPerExtraDay);
            yield return FlowRoutine(days * 24f, duration);
        }

        /// <summary>
        /// 진행·건너뛰기 연출 동안 시간을 실제로 조금씩(최대 1시간씩) 흘린다. 하늘·시계는 흐른 시각을 그대로 따라간다.
        /// 중요 편지(Mailbox.ImportantPosted — 파견대 도착·전멸·용병 사망·주급 미지급·등급 상승 등)가 생기면 그 시각에서 멈추고
        /// 배너를 "파견대 도착!" / "주급 미지급!"처럼 바꾼다. 도착은 도착 시각을 넘지 않게 끊어 흘려 정확히 그 시각에 멈춘다.
        /// 알림은 ImportantAlertPanel이 다음 프레임에 게임을 멈추고 띄운다. 날짜가 바뀌었으면 보고서를 우편함에 넣고 자동 저장한다.
        /// </summary>
        private IEnumerator FlowRoutine(float totalHours, float duration)
        {
            _isAdvancing = IsAdvancing = true;
            SetLocked(true);
            int dayBefore = GameClock.CurrentDay;
            var cycle = DayNightCycle.Instance;
            _stopMail = null;
            Mailbox.ImportantPosted += MarkStop;

            float done = 0f, dim = 0f;
            bool midpointSent = false;
            void PassUntil(float target)
            {
                // 한 번에 1시간 이하로, 그리고 가장 먼저 도착할 파견의 도착 시각을 넘지 않게 끊어 넘긴다(도착한 그 시각에서 멈춘다).
                while (done < target - 0.0001f && _stopMail == null)
                {
                    float step = Mathf.Min(1f, target - done);
                    float? arrival = ExpeditionLog.Instance.HoursUntilNextArrival;
                    if (arrival > 0f) step = Mathf.Min(step, arrival.Value);
                    GameClock.AdvanceTime(step);
                    done += step;
                }
                if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
                UpdateClockText(GameClock.CurrentHour);
            }

            for (float t = 0f; t < duration && _stopMail == null; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                PassUntil(totalHours * Mathf.SmoothStep(0f, 1f, k));
                dim = MaxDim * Mathf.Sin(Mathf.PI * k);
                SetDim(dim);
                if (!midpointSent && k >= 0.5f)
                {
                    midpointSent = true;
                    Midpoint?.Invoke(); // 가장 어두운 순간에 마을 사람 자리 섞기
                }
                yield return null;
            }
            PassUntil(totalHours); // 연출이 끝났는데 덜 흘렀으면 마저(도착했으면 그대로 멈춤)
            Mailbox.ImportantPosted -= MarkStop;
            if (!midpointSent) Midpoint?.Invoke();

            if (_stopMail != null)
            {
                _bannerTitle.text = StopBannerTitle(_stopMail);
                // 잘게 더한 시간의 소수점 오차(5.99999시 → "5:59")를 분 단위로 반올림해 보여 준다.
                float shown = Mathf.Round(GameClock.CurrentHour * 60f) / 60f;
                _bannerSub.text = $"{GameCalendar.Format(GameClock.CurrentDay)} {DayNightCycle.FormatTime(shown)}";
                for (float t = 0f; t < ArrivalUndimSeconds; t += Time.unscaledDeltaTime)
                {
                    SetDim(dim * (1f - t / ArrivalUndimSeconds));
                    yield return null;
                }
            }
            SetDim(0f);
            _bannerFade = StartCoroutine(FadeBanner());
            SetLocked(false);
            _isAdvancing = IsAdvancing = false;
            if (IsSkippingDays) ToastLog.FlushHeld(_skipPeriod); // 넘기는 동안 모아 둔 알림을 한 줄로(자동 저장 알림보다 먼저)
            IsSkippingDays = false;

            // 자정을 넘겼으면 넘긴 날들(파견 진행·있었던 일)을 보고서 한 통으로 우편함에 넣고 자동 저장한다.
            if (GameClock.CurrentDay != dayBefore)
            {
                Mailbox.PostReport(dayBefore + 1, GameClock.CurrentDay);
                AutoSave();
            }
        }

        /// <summary>멈춘 이유 배너: 도착은 "파견대 도착!", 그 밖은 짧은 제목에 "!"(이미 "!"·"…"로 끝나면 그대로 — "임무 실패...", "용병 사망!").</summary>
        private static string StopBannerTitle(Mailbox.Mail mail)
        {
            if (mail.Kind == MailKind.Arrival) return "파견대 도착!";
            string title = mail.ShortTitle ?? "";
            return title.Length > 0 && char.IsLetterOrDigit(title[title.Length - 1]) ? title + "!" : title;
        }

        private void MarkStop(Mailbox.Mail mail)
        {
            if (_stopMail == null) _stopMail = mail; // 처음 생긴 것 기준
        }

        private static void AutoSave()
        {
            if (SaveSystem.Save(auto: true)) ToastLog.Show("자동 저장됨", false); // 저장 알림은 우편함에 쌓지 않는다
        }

        /// <summary>불러오기 직후: 낮/밤과 시각 표시를 게임 시계에 맞춘다.</summary>
        public void SyncToClock()
        {
            var cycle = DayNightCycle.Instance;
            if (cycle != null) cycle.TimeOfDay = GameClock.CurrentHour;
            UpdateClockText(GameClock.CurrentHour);
        }

        // 자정을 넘는 중에도 일차는 GameClock 기준이라 0시에 확정될 때 바뀐다.
        private void UpdateClockText(float hour)
        {
            var weather = Weather.Today;
            if (_clockText != null)
                _clockText.text = $"{GameCalendar.FormatWithSeason(GameClock.CurrentDay)} · {DayNightCycle.FormatTime(hour)} · " +
                                  $"{Weather.KindName(weather.Kind)} {Mathf.RoundToInt(Weather.CurrentTemperatureAt(hour))}°C";
            if (_clockTip != null)
                _clockTip.Text = $"{GameClock.CurrentDay}일차\n오늘 {Weather.KindName(weather.Kind)} · 최저 {Mathf.RoundToInt(weather.Min)}°C / 최고 {Mathf.RoundToInt(weather.Max)}°C" +
                                (Weather.DebugTemperature.HasValue ? "\n(기온 시험용 고정 중)" : "");
        }

        private void ShowBanner(float from, float to, int hours)
        {
            EnsureOverlay();
            if (_bannerFade != null) StopCoroutine(_bannerFade);
            _bannerFade = null;

            bool overnight = to >= 24f;
            _bannerTitle.text = overnight ? "밤이 지나…" : $"{hours}시간 후…";
            _bannerSub.text = overnight
                ? $"{GameCalendar.Format(GameClock.CurrentDay + 1)} · {DayNightCycle.FormatTime(to)}"
                : $"{DayNightCycle.FormatTime(from)}  →  {DayNightCycle.FormatTime(to)}";
            _banner.alpha = 1f;
        }

        private IEnumerator FadeBanner()
        {
            // BattleScene 등으로 씬이 전환되면 이 코루틴의 자기 자신(TimeAdvanceController)보다 _banner가
            // 먼저 파괴될 수 있다 - 매 틱 null 체크로 막는다(SetDim과 같은 방어 방식).
            for (float t = 0f; t < BannerFadeSeconds; t += Time.unscaledDeltaTime)
            {
                if (_banner == null) yield break;
                _banner.alpha = 1f - t / BannerFadeSeconds;
                yield return null;
            }
            if (_banner != null) _banner.alpha = 0f;
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
            text.font = UITheme.Font;
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
