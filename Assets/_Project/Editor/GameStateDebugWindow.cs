using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Save;
using GN3.UI;
using GN3.World;
using UnityEditor;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// 테스트용 게임 상태 조정 창(에디터 전용, 빌드에 안 들어감). Play 중에 골드·시간·날짜·날씨·용병 체력/레벨·파견을 직접 바꾼다.
    /// 메뉴: GN3/Debug/게임 상태 조정
    /// </summary>
    public class GameStateDebugWindow : EditorWindow
    {
        private const double RepaintInterval = 0.2;

        private int _goldInput = 1000;
        private Vector2 _scroll;
        private double _nextRepaint;
        private readonly System.Random _rng = new System.Random();

        [MenuItem("GN3/Debug/게임 상태 조정")]
        public static void Open() => GetWindow<GameStateDebugWindow>("게임 상태 조정");

        private void OnEnable() => EditorApplication.update += TickRepaint;
        private void OnDisable() => EditorApplication.update -= TickRepaint;

        private void TickRepaint()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + RepaintInterval;
            Repaint();
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play 중에만 사용할 수 있습니다. MainScene을 Play하세요.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGold();
            EditorGUILayout.Space(8);
            DrawGuildAndSave();
            EditorGUILayout.Space(8);
            DrawTime();
            EditorGUILayout.Space(8);
            DrawDateAndWeather();
            EditorGUILayout.Space(8);
            DrawMercenaries();
            EditorGUILayout.Space(8);
            DrawExpeditions();
            EditorGUILayout.EndScrollView();
        }

        // ---------- 명성·저장 ----------

        private void DrawGuildAndSave()
        {
            EditorGUILayout.LabelField("길드 명성", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("현재", $"{Guild.Current.Name} · 명성 {Guild.Reputation} · 파티 정원 {Guild.Current.PartySize}명");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("명성 +100")) Guild.Add(100);
                if (GUILayout.Button("명성 −100")) Guild.Add(-100);
            }

            EditorGUILayout.LabelField("친밀도", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("파티 전원 친밀도 +20")) AddPartyAffinity(20);
                if (GUILayout.Button("−20")) AddPartyAffinity(-20);
            }

            EditorGUILayout.LabelField("저장", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("파일", SaveSystem.HasSave ? SaveSystem.FilePath : "(없음)");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("지금 저장")) ToastLog.Show(SaveSystem.Save() ? "[테스트] 저장함" : "[테스트] 저장 실패");
                using (new EditorGUI.DisabledScope(!SaveSystem.HasSave))
                {
                    if (GUILayout.Button("불러오기")) ToastLog.Show(SaveSystem.Load() ? "[테스트] 불러옴" : "[테스트] 불러오기 실패");
                    if (GUILayout.Button("저장 파일 삭제")) SaveSystem.Delete();
                }
            }
        }

        /// <summary>파티 안 모든 쌍의 친밀도를 궁합 배율 없이 바꾼다(같이 다니기 확인용).</summary>
        private static void AddPartyAffinity(int amount)
        {
            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    Affinity.AddRaw(members[i], members[j], amount);
            ToastLog.Show($"[테스트] 파티 친밀도 {(amount > 0 ? "+" : "")}{amount}");
        }

        // ---------- 골드 ----------

        private void DrawGold()
        {
            EditorGUILayout.LabelField("골드", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("보유", $"{Wallet.Gold} G");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+100")) Wallet.Add(100);
                if (GUILayout.Button("+1000")) Wallet.Add(1000);
                if (GUILayout.Button("−100")) Wallet.TrySpend(Mathf.Min(100, Wallet.Gold));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _goldInput = Mathf.Max(0, EditorGUILayout.IntField("값으로 설정", _goldInput));
                if (GUILayout.Button("설정", GUILayout.Width(60))) SetGold(_goldInput);
            }
        }

        /// <summary>Wallet에는 Set이 없어 차액만큼 벌거나 쓴다(골드 표시 깜빡임·이벤트가 그대로 동작).</summary>
        private static void SetGold(int target)
        {
            int delta = target - Wallet.Gold;
            if (delta > 0) Wallet.Add(delta);
            else if (delta < 0) Wallet.TrySpend(-delta);
        }

        // ---------- 시간 ----------

        private static void DrawTime()
        {
            EditorGUILayout.LabelField("시간", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("현재", $"{GameClock.CurrentDay}일차 ({GameCalendar.FormatWithSeason(GameClock.CurrentDay)}) · {DayNightCycle.FormatTime(GameClock.CurrentHour)}");
            var controller = Object.FindFirstObjectByType<TimeAdvanceController>();
            using (new EditorGUI.DisabledScope(controller == null))
            {
                if (GUILayout.Button($"진행 (+{GameClock.HoursPerStep:0}시간)")) controller.RequestAdvance();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("하루")) controller.RequestSkipDays(1);
                    if (GUILayout.Button("4일")) controller.RequestSkipDays(4);
                    if (GUILayout.Button("일주일")) controller.RequestSkipDays(7);
                }
            }
        }

        // ---------- 날짜·날씨 ----------

        private bool _dateInputReady;
        private int _yearInput, _monthInput, _dayInput, _hourInput;

        // 두 줄: 자동·맑음·흐림·비 계열 / 눈 계열
        private static readonly (string Label, WeatherKind? Kind)[][] WeatherChoices =
        {
            new (string, WeatherKind?)[]
            {
                ("자동", null), ("맑음", WeatherKind.Clear), ("흐림", WeatherKind.Cloudy),
                ("맑은 비", WeatherKind.SunShower), ("비", WeatherKind.Rain), ("폭우", WeatherKind.HeavyRain),
            },
            new (string, WeatherKind?)[]
            {
                ("맑은 눈", WeatherKind.SunnySnow), ("눈", WeatherKind.Snow), ("폭설", WeatherKind.HeavySnow),
            },
        };

        private void DrawDateAndWeather()
        {
            EditorGUILayout.LabelField("날짜 바로 옮기기", EditorStyles.boldLabel);
            int today = GameClock.CurrentDay;
            EditorGUILayout.LabelField("현재", $"{GameCalendar.FormatWithSeason(today)} · {DayNightCycle.FormatTime(GameClock.CurrentHour)} ({today}일차)");

            if (!_dateInputReady) FillDateInputFromClock();
            _yearInput = Mathf.Max(GameCalendar.StartYear, EditorGUILayout.IntField("년", _yearInput));
            _monthInput = EditorGUILayout.IntSlider("월", _monthInput, 1, GameCalendar.MonthsPerYear);
            _dayInput = EditorGUILayout.IntSlider("일", _dayInput, 1, GameCalendar.DaysPerMonth);
            _hourInput = EditorGUILayout.IntSlider("시", _hourInput, 0, 23);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("이 날짜로 이동")) JumpTo(GameCalendar.ToDay(_yearInput, _monthInput, _dayInput), _hourInput);
                if (GUILayout.Button("현재 값 불러오기", GUILayout.Width(110))) FillDateInputFromClock();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("봄 3/1")) JumpToSeasonStart(3);
                if (GUILayout.Button("여름 6/1")) JumpToSeasonStart(6);
                if (GUILayout.Button("가을 9/1")) JumpToSeasonStart(9);
                if (GUILayout.Button("겨울 12/1")) JumpToSeasonStart(12);
            }
            EditorGUILayout.HelpBox("바로 옮기기는 그사이 날의 일(파견 진행·주급·회복)을 처리하지 않습니다. 실제로 날을 보내려면 위의 하루/4일/일주일을 쓰세요.", MessageType.None);

            EditorGUILayout.LabelField("날씨", EditorStyles.boldLabel);
            var auto = Weather.Generate(today);
            string forced = Weather.DebugOverride.HasValue ? $"  → 강제: {Weather.KindName(Weather.DebugOverride.Value)}" : "";
            EditorGUILayout.LabelField("오늘",
                $"(자동) {Weather.KindName(auto.Kind)} · 최저 {Mathf.RoundToInt(auto.Min)}°C / 최고 {Mathf.RoundToInt(auto.Max)}°C · 지금 {Mathf.RoundToInt(Weather.CurrentTemperature)}°C{forced}");
            foreach (var row in WeatherChoices)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var (label, kind) in row)
                    {
                        bool selected = Weather.DebugOverride == kind;
                        if (GUILayout.Toggle(selected, label, EditorStyles.miniButton) && !selected)
                            Weather.DebugOverride = kind;
                    }
                }
            }

            DrawTemperature();
        }

        private static readonly (string Label, float Value)[] TemperaturePresets =
        {
            ("혹한 −15", -15f), ("쌀쌀 5", 5f), ("포근 15", 15f), ("더움 28", 28f), ("폭염 36", 36f),
        };

        /// <summary>기온 고정(−20~40℃). 기온은 햇빛 세기(±25%)·색(더우면 노랗게, 추우면 푸르스름하게)에 들어간다.</summary>
        private static void DrawTemperature()
        {
            EditorGUILayout.LabelField("기온", EditorStyles.boldLabel);
            bool forced = Weather.DebugTemperature.HasValue;
            bool wantForced = EditorGUILayout.Toggle("기온 직접 정하기", forced);
            if (wantForced != forced)
                Weather.DebugTemperature = wantForced ? Mathf.Round(Weather.CurrentTemperature) : (float?)null;

            if (Weather.DebugTemperature.HasValue)
            {
                Weather.DebugTemperature = EditorGUILayout.Slider("기온(°C)", Weather.DebugTemperature.Value, -20f, 40f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (var (label, value) in TemperaturePresets)
                        if (GUILayout.Button(label, EditorStyles.miniButton)) Weather.DebugTemperature = value;
                }
            }
            EditorGUILayout.LabelField("햇빛", $"세기 ×{Weather.SunFactor:0.00} · 색 따뜻함 {Weather.SunWarmth:+0.00;-0.00;0.00}");
        }

        private void FillDateInputFromClock()
        {
            var date = GameCalendar.Today;
            _yearInput = date.Year;
            _monthInput = date.Month;
            _dayInput = date.Day;
            _hourInput = Mathf.FloorToInt(GameClock.CurrentHour);
            _dateInputReady = true;
        }

        /// <summary>지금 년도의 그 달 1일 정오. 이미 지났으면 다음 해.</summary>
        private void JumpToSeasonStart(int month)
        {
            var date = GameCalendar.Today;
            int day = GameCalendar.ToDay(date.Year, month, 1);
            if (day <= GameClock.CurrentDay) day = GameCalendar.ToDay(date.Year + 1, month, 1);
            JumpTo(day, 12);
        }

        /// <summary>시계만 옮기고(그사이 날 처리 없음) 낮밤 조명·시계 표시를 바로 맞춘다.</summary>
        private void JumpTo(int day, int hour)
        {
            GameClock.Restore(day, hour);
            Object.FindFirstObjectByType<TimeAdvanceController>()?.SyncToClock();
            FillDateInputFromClock();
            ToastLog.Show($"[테스트] {GameCalendar.FormatWithSeason(GameClock.CurrentDay)} {DayNightCycle.FormatTime(GameClock.CurrentHour)}로 이동");
        }

        // ---------- 용병 ----------

        private void DrawMercenaries()
        {
            var party = PlayerParty.Instance;
            EditorGUILayout.LabelField($"용병 ({party.Members.Count}/{party.MaxSize})", EditorStyles.boldLabel);

            if (GUILayout.Button("무료로 랜덤 용병 1명 고용")) HireRandom();

            Mercenary toRemove = null;
            foreach (var merc in party.Members.ToList())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var quest = ExpeditionLog.Instance.FindQuest(merc);
                    string where = quest != null ? $"파견 중: {quest.Title}" : "마을";
                    EditorGUILayout.LabelField($"[{GradeTable.Label(merc.Grade)}] {merc.Name}  ·  {merc.Class.ClassName}  ·  Lv.{merc.Level}  ·  전투력 {merc.CombatPower}  ·  {where}");
                    var stats = merc.CurrentStats;
                    string weapon = merc.Weapon != null ? $"{merc.Weapon.Name} ({merc.Weapon.DescribeFor(merc.Class.Kind)})" : "맨손";
                    EditorGUILayout.LabelField($"공격 {stats.Attack} · 방어 {stats.Defense} · 속도 {stats.MoveSpeed}   무기: {weapon}");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("경험치", $"{merc.Experience} / {merc.XpToNext}");
                        if (GUILayout.Button("경험치 +100", GUILayout.Width(100)))
                        {
                            int ups = merc.AddExperience(100);
                            if (ups > 0) ToastLog.Show($"[테스트] {merc.Name} 레벨 업! Lv.{merc.Level}");
                        }
                    }

                    int max = merc.CurrentStats.MaxHealth;
                    EditorGUI.BeginChangeCheck();
                    int hp = EditorGUILayout.IntSlider("체력", merc.CurrentHealth, 1, max);
                    if (EditorGUI.EndChangeCheck()) merc.SetHealth(hp);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("완전 회복")) merc.HealFully();
                        if (GUILayout.Button("−30%")) DebugHotkeys.Damage(merc);
                        if (GUILayout.Button("Lv +1")) merc.SetLevel(merc.Level + 1);
                        using (new EditorGUI.DisabledScope(merc.Level <= 1))
                            if (GUILayout.Button("Lv −1")) merc.SetLevel(merc.Level - 1);
                        using (new EditorGUI.DisabledScope(merc.Grade == MercenaryGrade.S))
                            if (GUILayout.Button("등급 ▲")) merc.SetGrade(merc.Grade + 1);
                        using (new EditorGUI.DisabledScope(merc.Grade == MercenaryGrade.F))
                            if (GUILayout.Button("등급 ▼")) merc.SetGrade(merc.Grade - 1);
                        if (GUILayout.Button("해고")) toRemove = merc;
                    }
                }
            }
            if (toRemove != null) party.Remove(toRemove);
        }

        private void HireRandom()
        {
            var party = PlayerParty.Instance;
            if (party.Members.Count >= party.MaxSize)
            {
                ToastLog.Show($"[테스트] 파티 정원({party.MaxSize}명)이 가득 찼습니다.");
                return;
            }
            var pool = new List<MercenaryClassSO>(Resources.LoadAll<MercenaryClassSO>("MercenaryClasses"));
            if (pool.Count == 0)
            {
                Debug.LogWarning("[GameStateDebugWindow] Resources/MercenaryClasses에서 클래스를 찾지 못했습니다.");
                return;
            }
            var merc = MercenaryMarketGenerator.Generate(pool, 1, 1, 3, _rng)[0];
            if (party.TryAdd(merc)) ToastLog.Show($"[테스트] {merc.Name} 무료 고용 ({merc.Class.ClassName} Lv.{merc.Level})");
        }

        // ---------- 파견 ----------

        private static void DrawExpeditions()
        {
            var active = ExpeditionLog.Instance.Active;
            EditorGUILayout.LabelField($"진행 중인 파견 ({active.Count})", EditorStyles.boldLabel);
            foreach (var expedition in active.ToList())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    var quest = expedition.Quest;
                    EditorGUILayout.LabelField(expedition.IsReady ? $"{quest.Title} — 도착" : $"{quest.Title} — 남은 {quest.RemainingDays}일");
                    using (new EditorGUI.DisabledScope(expedition.IsReady))
                        if (GUILayout.Button("즉시 도착", GUILayout.Width(80))) ExpeditionLog.Instance.DebugArriveNow(expedition);
                }
            }
        }
    }
}
