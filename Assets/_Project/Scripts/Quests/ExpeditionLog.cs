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
    /// 확률적으로 습격(도적단 등) 이벤트를 굴려 용병 체력을 깎거나 사망시킬 수 있다.</summary>
    public class ExpeditionLog
    {
        private const double AmbushChance = 0.25;

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
            OnChanged?.Invoke();
            return expedition;
        }

        public void Complete(Expedition expedition)
        {
            if (_active.Remove(expedition))
                OnChanged?.Invoke();
        }

        /// <summary>파견대가 하루를 더 들여 휴식하며 체력을 회복한다.</summary>
        public void Rest(Expedition expedition)
        {
            if (!_active.Contains(expedition)) return;

            expedition.Rest();
            OnTravelEvent?.Invoke($"[{expedition.Quest.Title}] 휴식을 취해 체력을 회복했다. (남은 일수 +1일)");
            OnChanged?.Invoke();
        }

        private Expedition FindExpedition(Mercenary mercenary)
        {
            return _active.FirstOrDefault(e => e.Members.Any(m => m.Id == mercenary.Id));
        }

        private void HandleDayAdvanced()
        {
            // TryTriggerAmbush가 전멸한 파견을 _active에서 제거할 수 있어 스냅샷을 순회한다.
            foreach (var expedition in _active.ToList())
            {
                bool wasTraveling = !expedition.Quest.IsReady;
                expedition.Quest.AdvanceDay();

                if (wasTraveling)
                    TryTriggerAmbush(expedition);
            }

            OnChanged?.Invoke();
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
            sb.Append($"[{expedition.Quest.Title}] 이동 중 {raiderNames}의 습격!");

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

            if (expedition.Members.All(m => !m.IsAlive))
            {
                sb.Append(" 파견대가 전멸했다...");
                Complete(expedition);
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
