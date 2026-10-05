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
    /// 테스트용 게임 상태 조정 창(에디터 전용, 빌드에 안 들어감). Play 중에 골드·시간·용병 체력/레벨·파견을 직접 바꾼다.
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
            EditorGUILayout.LabelField("현재", $"{GameClock.CurrentDay}일차 · {DayNightCycle.FormatTime(GameClock.CurrentHour)}");
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
