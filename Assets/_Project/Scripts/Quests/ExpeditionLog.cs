using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GN3.Combat;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;

namespace GN3.Quests
{
    /// <summary>현재 진행 중인 모든 파견대를 관리하는 싱글턴. GameClock이 하루 지날 때마다
    /// 모든 파견의 남은 일수(Quest.RemainingDays)를 함께 줄이고, 아직 이동 중인 파견에는
    /// 확률적으로 습격(도적단 등) 이벤트를 굴려 용병 체력을 깎거나 사망시킬 수 있다.
    /// 전투를 마친 파견도 귀환 일수가 다 지나 마을에 도착할 때까지 여기 남는다(귀환길에도 습격이 있다).</summary>
    public class ExpeditionLog
    {
        private const double AmbushChance = 0.25;
        private const int AmbushSurvivalXp = 10;

        /// <summary>파견대에 길잡이(Guide)가 한 명이라도 있으면, 발생한 습격을 이 확률로 통째로 무효화한다.</summary>
        private const double GuideAvoidChance = 0.5;

        private static ExpeditionLog _instance;
        public static ExpeditionLog Instance => _instance ??= new ExpeditionLog();

        private readonly List<Expedition> _active = new List<Expedition>();
        private readonly System.Random _eventRng = new System.Random();

        public IReadOnlyList<Expedition> Active => _active;

        public event Action OnChanged;

        /// <summary>습격 등 이동 중 이벤트가 발생했을 때 결과 문구를 전달한다. UI가 구독해서 표시한다.</summary>
        public event Action<string> OnTravelEvent;

        /// <summary>파견대가 목적지에 막 도착했을 때(남은 일수가 0이 된 순간). 도착 알림창이 구독한다.</summary>
        public event Action<Expedition> OnArrived;

        private ExpeditionLog()
        {
            GameClock.OnDayAdvanced += HandleDayAdvanced;
        }

        public bool IsOnExpedition(Mercenary mercenary) => FindExpedition(mercenary) != null;

        public Quest FindQuest(Mercenary mercenary) => FindExpedition(mercenary)?.Quest;

        public Expedition Dispatch(Quest quest, IReadOnlyList<Mercenary> members)
        {
            var expedition = new Expedition(quest, members);
            _active.Add(expedition);

            // 전투력이 넉넉한 편성(쉬움·매우 쉬움)은 더 빨리 도착한다(QuestDifficulty.DaysSaved).
            int before = quest.RemainingDays;
            quest.Shorten(QuestDifficulty.DaysSaved(QuestDifficulty.Rate(QuestDifficulty.TeamPower(members), quest)));
            if (quest.RemainingDays < before)
                OnTravelEvent?.Invoke($"[{quest.Title}] 전투력이 넉넉해 빠르게 진군한다 (소요 {before}일 → {quest.RemainingDays}일)");

            OnChanged?.Invoke();
            return expedition;
        }

        /// <summary>저장 파일에서 불러온 파견을 그대로 넣는다(빠른 진군 계산·출발 알림 없음).
        /// 이미 도착해 선택을 기다리던 파견이면 도착 알림창을 다시 띄운다.</summary>
        public void Restore(Expedition expedition)
        {
            _active.Add(expedition);
            OnChanged?.Invoke();
            if (expedition.IsReady) OnArrived?.Invoke(expedition);
        }

        /// <summary>새로 시작·불러오기 전에 진행 중인 파견을 모두 비운다.</summary>
        public void Clear()
        {
            _active.Clear();
            OnChanged?.Invoke();
        }

        public void Complete(Expedition expedition)
        {
            if (_active.Remove(expedition))
                OnChanged?.Invoke();
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 파견대를 바로 목적지에 도착시킨다.</summary>
        public void DebugArriveNow(Expedition expedition)
        {
            if (!_active.Contains(expedition)) return;
            expedition.Quest.ArriveNow();
            OnChanged?.Invoke();
            OnArrived?.Invoke(expedition);
        }

        /// <summary>테스트·디버그용(게임 상태 조정 창). 귀환 중인 파견대를 바로 마을에 도착시킨다.</summary>
        public void DebugReturnNow(Expedition expedition)
        {
            if (!_active.Contains(expedition) || !expedition.IsReturning) return;
            FinishReturn(expedition);
        }

        /// <summary>전투를 마친 파견대가 귀환길에 오른다(ExpeditionBattle.Conclude). 마을에 도착할 때까지 파견 중으로 남는다.</summary>
        public void BeginReturn(Expedition expedition)
        {
            if (!_active.Contains(expedition)) return;
            expedition.BeginReturn();
            OnTravelEvent?.Invoke($"[{expedition.Quest.Title}] 귀환길에 오른다 (소요 {expedition.ReturnDaysLeft}일)");
            OnChanged?.Invoke();
        }

        /// <summary>파견대가 하루를 더 들여 휴식하며 체력을 회복한다.</summary>
        public void Rest(Expedition expedition)
        {
            if (!_active.Contains(expedition)) return;

            expedition.Rest();
            string which = expedition.IsReturning ? "귀환" : "남은 일수";
            OnTravelEvent?.Invoke($"[{expedition.Quest.Title}] 휴식을 취해 체력을 회복했다. ({which} +1일)");
            OnChanged?.Invoke();
        }

        private void FinishReturn(Expedition expedition)
        {
            string survivors = string.Join(", ", expedition.Members.Where(m => m.IsAlive).Select(m => m.Name));
            OnTravelEvent?.Invoke($"[{expedition.Quest.Title}] 마을로 돌아왔다 (귀환: {survivors})");
            Complete(expedition);
        }

        private Expedition FindExpedition(Mercenary mercenary)
        {
            return _active.FirstOrDefault(e => e.Members.Any(m => m.Id == mercenary.Id));
        }

        private void HandleDayAdvanced()
        {
            // TryTriggerAmbush가 전멸한 파견을 _active에서 제거할 수 있어 스냅샷을 순회한다.
            var arrived = new List<Expedition>();
            foreach (var expedition in _active.ToList())
            {
                if (expedition.IsReturning)
                {
                    // 돌아오는 길도 갈 때처럼 습격을 받을 수 있다(전멸하면 TryTriggerAmbush가 Complete한다).
                    expedition.AdvanceReturnDay();
                    TryTriggerAmbush(expedition);
                    if (expedition.ReturnDaysLeft <= 0 && _active.Contains(expedition)) FinishReturn(expedition);
                    continue;
                }

                bool wasTraveling = !expedition.Quest.IsReady;
                if (wasTraveling) expedition.CountOutboundDay();
                expedition.Quest.AdvanceDay();

                if (wasTraveling)
                {
                    TryTriggerAmbush(expedition);
                    if (expedition.IsReady && _active.Contains(expedition)) arrived.Add(expedition);
                }
            }

            OnChanged?.Invoke();
            // 순회가 끝난 뒤 알린다(자동 진행이 바로 Complete해도 목록이 안전하다).
            foreach (var expedition in arrived)
                if (_active.Contains(expedition)) OnArrived?.Invoke(expedition);
        }

        private void TryTriggerAmbush(Expedition expedition)
        {
            var aliveMembers = expedition.Members.Where(m => m.IsAlive).ToList();
            if (aliveMembers.Count == 0) return;

            if (_eventRng.NextDouble() >= AmbushChance) return;

            bool hasGuide = aliveMembers.Any(m => m.Class.Kind == MercenaryClassKind.Guide);
            if (hasGuide && _eventRng.NextDouble() < GuideAvoidChance)
            {
                OnTravelEvent?.Invoke($"[{expedition.Quest.Title}] 길잡이 덕분에 습격을 피했다.");
                return;
            }

            // 습격 규모/강도를 퀘스트 자체의 난이도·적 숫자에 맞춰 키운다 — 난이도가 높은 원정일수록
            // 오가는 길도 위험해지고, 그래야 습격을 피하게 해주는 길잡이의 가치도 생긴다.
            int maxAmbushSize = Math.Max(1, Math.Min(aliveMembers.Count, expedition.Quest.EnemyCount));
            int enemyCount = _eventRng.Next(1, maxAmbushSize + 1);
            int ambushDifficulty = Math.Max(1, expedition.Quest.Difficulty);
            var enemies = EnemySquadGenerator.Generate(expedition.Quest.Region, enemyCount, ambushDifficulty, _eventRng, tierOverride: 0);
            var combatants = aliveMembers.Select(m => m.ToCombatant()).ToList();

            var simulator = new AutoBattleSimulator();
            simulator.Simulate(combatants, enemies, maxRounds: 20);

            string raiderNames = string.Join("·", enemies.Select(e => e.Name).Distinct());
            var sb = new StringBuilder();
            sb.Append($"[{expedition.Quest.Title}] {(expedition.IsReturning ? "귀환" : "이동")} 중 {raiderNames}의 습격!");

            for (int i = 0; i < aliveMembers.Count; i++)
            {
                var merc = aliveMembers[i];
                var combatant = combatants[i];
                merc.SetHealth(combatant.CurrentHealth);

                if (!merc.IsAlive)
                {
                    sb.Append($" {merc.Name} 사망.");
                    PlayerParty.Instance.Remove(merc);
                }
                else if (combatant.CurrentHealth < combatant.Stats.MaxHealth)
                {
                    sb.Append($" {merc.Name} 체력 {combatant.CurrentHealth}/{combatant.Stats.MaxHealth}.");
                }
            }

            MercenaryCondition.AfterAmbush(aliveMembers.Where(m => m.IsAlive).ToList(), aliveMembers.Where(m => !m.IsAlive).ToList()); // 피로·사기
            if (expedition.Members.All(m => !m.IsAlive))
            {
                sb.Append(" 파견대가 전멸했다...");
                Complete(expedition);
            }
            else
            {
                // 습격에서 살아남으면 경험치 +10.
                sb.Append($" 생존자 경험치 +{AmbushSurvivalXp}.");
                foreach (var line in ExpeditionBattle.GrantExperience(expedition.Members.Where(m => m.IsAlive), AmbushSurvivalXp))
                    sb.Append($" {line}");
            }

            OnTravelEvent?.Invoke(sb.ToString());
        }

        // GameClock과 마찬가지로 Domain Reload가 꺼져 있어도 Play 진입마다 상태를 초기화하기 위한 지점.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _instance = null;
        }
    }
}
