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
            DrawQuests();
            EditorGUILayout.Space(8);
            DrawQuickBattle();
            EditorGUILayout.Space(8);
            DrawRivals();
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
                int hours = controller != null ? controller.AdvanceHours : (int)GameClock.HoursPerStep;
                if (GUILayout.Button($"진행 (+{hours}시간)")) controller.RequestAdvance();
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
                    EditorGUILayout.LabelField($"[{GradeTable.Label(merc.Grade)}] {merc.Name}  ·  {merc.Class.ClassName}  ·  {merc.Age}세  ·  재능 {GradeTable.Label(merc.Talent)}  ·  전투력 {merc.CombatPower} (등급 기준 {merc.GradePower})  ·  {where}");
                    var stats = merc.CurrentStats;
                    string weapon = merc.Weapon != null ? $"{merc.Weapon.Name} ({merc.Weapon.DescribeFor(merc.Class.Kind)})" : "맨손";
                    string holy = stats.Holy > 0 ? $" · 신성력 {stats.Holy}" : "";
                    EditorGUILayout.LabelField($"{merc.OffenseLabel} {stats.Offense} · 방어 {stats.Defense} · 속도 {stats.MoveSpeed}{holy}   무기: {weapon}");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("성장", $"{GrowthTuning.RateLabel(merc.Age)} · 다음까지 {merc.GrowthProgress:P0} · {merc.GrowthSummary()}");
                        using (new EditorGUI.DisabledScope(!merc.CanGrow))
                            if (GUILayout.Button("성장 +1", GUILayout.Width(100)))
                            {
                                var gains = merc.AddGrowth((1f - merc.GrowthProgress) / merc.GrowthRate + 0.0001f);
                                if (gains.Count > 0) ToastLog.Show($"[테스트] {merc.Name} {string.Join(", ", gains)}");
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
                        if (GUILayout.Button("나이 +1")) merc.SetAge(merc.Age + 1);
                        using (new EditorGUI.DisabledScope(merc.Age <= GrowthTuning.MinHireAge))
                            if (GUILayout.Button("나이 −1")) merc.SetAge(merc.Age - 1);
                        using (new EditorGUI.DisabledScope(merc.Talent == MercenaryGrade.S))
                            if (GUILayout.Button("재능 ▲")) merc.SetTalent(merc.Talent + 1);
                        using (new EditorGUI.DisabledScope(merc.Talent == MercenaryGrade.F))
                            if (GUILayout.Button("재능 ▼")) merc.SetTalent(merc.Talent - 1);
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
            if (party.TryAdd(merc)) ToastLog.Show($"[테스트] {merc.Name} 무료 고용 ({merc.Class.ClassName} {merc.Age}세)");
        }

        // ---------- 퀘스트 ----------

        private int _questLevel = 1;

        private void DrawQuests()
        {
            EditorGUILayout.LabelField("퀘스트 게시판", EditorStyles.boldLabel);
            var board = QuestBoardListings.Current;
            string boardInfo = board == null ? "아직 안 뽑음" : $"{board.Count}개 · 새 의뢰까지 {QuestBoardListings.DaysUntilRefresh}일";
            EditorGUILayout.LabelField("현재", $"은월의 숲 열린 레벨 Lv{ForestProgress.UnlockedLevel} · 게시판 {boardInfo}");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("열린 레벨", GUILayout.Width(80));
                using (new EditorGUI.DisabledScope(ForestProgress.UnlockedLevel <= 1))
                    if (GUILayout.Button("Lv −")) SetForestLevel(ForestProgress.UnlockedLevel - 1);
                using (new EditorGUI.DisabledScope(ForestProgress.UnlockedLevel >= ForestProgress.MaxLevel))
                    if (GUILayout.Button("Lv +")) SetForestLevel(ForestProgress.UnlockedLevel + 1);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _questLevel = EditorGUILayout.IntSlider("추가할 레벨", _questLevel, 1, ForestProgress.MaxLevel);
                if (GUILayout.Button("이 레벨 퀘스트 추가", GUILayout.Width(140))) AddQuest(_questLevel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Lv1~5 하나씩 추가"))
                    for (int lv = ForestProgress.MaxLevel; lv >= 1; lv--) AddQuest(lv); // 맨 위에 붙으니 거꾸로 넣어 Lv1이 맨 위
                if (GUILayout.Button("앤트킹(Lv5) 추가")) AddQuest(ForestProgress.MaxLevel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("게시판 새로 뽑기"))
                {
                    QuestBoardListings.DebugExpire();
                    RedrawQuestBoard();
                    ToastLog.Show("[테스트] 퀘스트 게시판 새로 뽑음");
                }
                if (GUILayout.Button("용병시장 새로 뽑기"))
                {
                    MarketListings.DebugExpire();
                    Object.FindFirstObjectByType<MercenaryMarketUI>(FindObjectsInactive.Include)?.RefreshFromOutside();
                    ToastLog.Show("[테스트] 용병시장 새로 뽑음");
                }
            }

            if (board != null)
                foreach (var quest in board)
                {
                    string need = quest.RequiredDispatchSize > 0
                        ? $"정확히 {quest.RequiredDispatchSize}명" + (quest.SquadCount > 1 ? " (두 분대)" : "")
                        : $"최대 {quest.MaxDispatchSize}명";
                    EditorGUILayout.LabelField($"  Lv{quest.Difficulty} [{GradeTable.Label(QuestDifficulty.Grade(quest))}] {quest.Title}  ·  {quest.WaveCount}라운드 · {need}");
                }
        }

        private void AddQuest(int level)
        {
            var quest = QuestGenerator.Generate(RegionInfo.QuestRegion, level, 2, 5, _rng);
            QuestBoardListings.DebugAdd(quest);
            RedrawQuestBoard();
            ToastLog.Show($"[테스트] Lv{level} {quest.Title} 퀘스트 추가");
        }

        /// <summary>열린 레벨을 바로 바꾼다(그 레벨 도전 퀘스트를 게시판이 맨 위에 더한다 — QuestBoardUI.OnLevelUnlocked).</summary>
        private static void SetForestLevel(int level)
        {
            ForestProgress.Restore(level);
            ToastLog.Show($"[테스트] 은월의 숲 Lv{ForestProgress.UnlockedLevel}까지 열림");
        }

        /// <summary>게시판이 비활성이어도 다시 그린다(아직 한 번도 안 열려 Awake 전이면 열 때 그린다).</summary>
        private static void RedrawQuestBoard() =>
            Object.FindFirstObjectByType<QuestBoardUI>(FindObjectsInactive.Include)?.RefreshFromOutside();

        // ---------- 근처 길드 ----------

        private void DrawRivals()
        {
            EditorGUILayout.LabelField("근처 길드", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("순위", $"우리 길드 {RivalGuilds.OurPlace}위 · 자유 계약 {RivalGuilds.FreeAgents.Count}명 · 대여 {Loans.All.Count}건");
            foreach (var g in RivalGuilds.All)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{g.Name} ({g.Info.Label})", GUILayout.Width(150));
                    EditorGUILayout.LabelField($"명성 {g.Reputation} · 관계 {g.Relation} {RivalGuilds.RelationLabel(g.Relation)} · 용병 {g.Roster.Count} · {g.Gold}G", GUILayout.MinWidth(200));
                    if (GUILayout.Button("관계−20", GUILayout.Width(64))) RivalGuilds.DebugAddRelation(g, -20);
                    if (GUILayout.Button("관계+20", GUILayout.Width(64))) RivalGuilds.DebugAddRelation(g, 20);
                    if (GUILayout.Button("명성+50", GUILayout.Width(64))) RivalGuilds.DebugAddReputation(g, 50);
                    if (GUILayout.Button("시장 고용", GUILayout.Width(70))) RivalGuilds.DebugHire(g);
                }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("1일 시뮬레이션")) RivalGuilds.DebugSimulate(1);
                if (GUILayout.Button("7일 시뮬레이션")) RivalGuilds.DebugSimulate(7);
                if (GUILayout.Button("30일 시뮬레이션")) RivalGuilds.DebugSimulate(30);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var board = QuestBoardListings.Current;
                var takeable = board?.FirstOrDefault(q => !q.IsChallenge);
                using (new EditorGUI.DisabledScope(takeable == null))
                    if (GUILayout.Button("의뢰 하나 가로채기"))
                    {
                        var g = RivalGuilds.All[Random.Range(0, RivalGuilds.All.Count)];
                        RivalGuilds.TakeQuest(g, takeable, GameClock.CurrentDay);
                    }
                using (new EditorGUI.DisabledScope(board == null))
                {
                    if (GUILayout.Button("내일 노릴 의뢰 정하기")) { RivalGuilds.PickTarget(); RedrawQuestBoard(); }
                    if (GUILayout.Button("연합 의뢰 만들기"))
                    {
                        RivalDeals.MakeJointQuests(board, force: true);
                        if (!board.Any(q => q.IsJoint)) Debug.Log("[Debug] 연합 의뢰를 못 만듦: Lv3 이상 의뢰와 우호(관계 15+) 길드가 필요");
                        QuestBoardListings.NotifyChanged();
                        RedrawQuestBoard();
                    }
                }
            }
            if (GUILayout.Button("근처 길드 창 열기")) RivalGuildsPanel.ShowGlobal();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("선택 사건", EditorStyles.boldLabel);
            var moving = ExpeditionLog.Instance.Active.FirstOrDefault(x => !x.IsReady && !x.AwaitingEvent);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(moving == null))
                    if (GUILayout.Button(moving != null ? $"원정 사건 ({moving.Quest.Title})" : "원정 사건 (이동 중인 파견대 없음)")) ChoiceEvents.TriggerTravel(moving);
                if (GUILayout.Button("마을 사건")) ChoiceEvents.TriggerVillage();
            }
            EditorGUILayout.HelpBox("시뮬레이션은 날짜를 넘기지 않고 근처 길드의 하루만 돌린다(의뢰 정산·고용·가로채기 포함 — 우리 시장·게시판도 바뀐다).", MessageType.None);
        }

        // ---------- 몬스터 바로 전투 ----------

        // (몬스터 이름, 계층: 0 약탈 세력 · 1 지형 동물 · 2 보스)
        private static readonly (string Name, int Tier)[] QuickMonsters =
        {
            ("고블린", 0), ("고블린 늑대기수", 0), ("야생늑대", 1), ("독버섯", 1), ("앤트킹", 2),
        };

        private int _quickMonster;
        private int _quickCount = 3;
        private int _quickLevel = 1;
        private int _quickMembers = 3;

        /// <summary>
        /// 퀘스트 수락·이동 없이 고른 몬스터와 바로 싸운다: 테스트 퀘스트(라운드 1개)를 만들어 마을 용병을 보내고 즉시 도착시킨 뒤
        /// 직접 전투(BattleScene) 또는 자동 전투. 실제 전투 경로 그대로라 보상·전리품·도감·사망이 모두 반영된다.
        /// </summary>
        private void DrawQuickBattle()
        {
            EditorGUILayout.LabelField("몬스터 바로 전투", EditorStyles.boldLabel);
            _quickMonster = GUILayout.Toolbar(_quickMonster, QuickMonsters.Select(m => m.Name).ToArray());
            var (monster, tier) = QuickMonsters[_quickMonster];
            bool boss = tier >= 2;

            using (new EditorGUI.DisabledScope(boss))
                _quickCount = EditorGUILayout.IntSlider("마릿수", boss ? 1 : _quickCount, 1, 6);
            _quickLevel = EditorGUILayout.IntSlider("레벨(적 능력치)", _quickLevel, 1, ForestProgress.MaxLevel);
            _quickMembers = EditorGUILayout.IntSlider("보낼 용병 수", _quickMembers, 1, 6);

            var ready = AvailableMercs().Take(_quickMembers).ToList();
            EditorGUILayout.LabelField("보낼 용병", ready.Count > 0 ? string.Join(", ", ready.Select(m => m.Name)) : "(마을에 출전할 수 있는 용병이 없습니다 — 위에서 무료 고용)");
            if (monster == "고블린") EditorGUILayout.LabelField("", "직접 전투에선 고블린 3마리 이상이면 셋째마다 궁수로 나온다", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(ready.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("직접 전투")) StartQuickBattle(monster, tier, boss ? 1 : _quickCount, ready, direct: true);
                if (GUILayout.Button("자동 전투")) StartQuickBattle(monster, tier, boss ? 1 : _quickCount, ready, direct: false);
            }
            EditorGUILayout.HelpBox("실제 전투라 보상·전리품·도감·명성·사망이 그대로 반영됩니다(필요하면 먼저 저장 백업). " +
                                    "열린 최고 레벨로 이기면 다음 레벨이 열립니다.", MessageType.None);
        }

        /// <summary>파티 화면과 같은 기준으로 지금 마을에서 출전할 수 있는 용병(파견·훈련·입원·쉼터 아님, 지치거나 크게 앓지 않음).</summary>
        private static IEnumerable<Mercenary> AvailableMercs() =>
            PlayerParty.Instance.Members.Where(m => m.IsAlive
                && !ExpeditionLog.Instance.IsOnExpedition(m) && !TrainingHall.IsTraining(m) && !Hospital.IsAdmitted(m) && RestVenues.Find(m) == null
                && !m.IsExhausted && !m.IsSeverelyAiling);

        private void StartQuickBattle(string monster, int tier, int count, List<Mercenary> members, bool direct)
        {
            if (members.Count == 0) return;
            string location = tier >= 2 ? "은월의 숲 깊은 곳" : "은월의 숲";
            var waves = new List<QuestWave> { new QuestWave(monster, count, tier) };
            var quest = new Quest($"[테스트] {monster} ×{count}", RegionInfo.QuestRegion, location, monster, count, _quickLevel, 1, waves);

            // 두 분대 퀘스트(Lv5)면 번갈아 1·2분대, 분대마다 첫 사람이 분대장
            List<int> squads = null;
            Mercenary leader2 = null;
            if (quest.SquadCount > 1 && members.Count > 1)
            {
                squads = members.Select((m, i) => i % 2).ToList();
                leader2 = members[1];
            }

            var expedition = ExpeditionLog.Instance.Dispatch(quest, members, members[0], squads, leader2);
            ExpeditionLog.Instance.DebugArriveNow(expedition);
            ToastLog.Show($"[테스트] {monster} ×{count} (Lv{_quickLevel}) — {(direct ? "직접" : "자동")} 전투");
            if (direct) MailActions.DirectFight(expedition);
            else MailActions.AutoFight(expedition);
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
                    if (expedition.IsReturning)
                    {
                        EditorGUILayout.LabelField($"{quest.Title} — 귀환 중 남은 {GameClock.FormatDuration(expedition.ReturnHoursLeft)}");
                        if (GUILayout.Button("즉시 귀환", GUILayout.Width(80))) ExpeditionLog.Instance.DebugReturnNow(expedition);
                        continue;
                    }
                    EditorGUILayout.LabelField(expedition.IsReady ? $"{quest.Title} — 도착" : $"{quest.Title} — 남은 {GameClock.FormatDuration(quest.RemainingHours)}");
                    using (new EditorGUI.DisabledScope(expedition.IsReady))
                        if (GUILayout.Button("즉시 도착", GUILayout.Width(80))) ExpeditionLog.Instance.DebugArriveNow(expedition);
                }
            }
        }
    }
}
