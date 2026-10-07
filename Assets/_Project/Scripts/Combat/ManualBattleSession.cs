using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>
    /// 직접전투(플레이어가 매 턴 기본공격/스킬과 대상을 직접 고르는 전투)의 상태 기계.
    /// AutoBattleSimulator와 같은 핵심 규칙(CombatResolver)을 공유하되, 전체를 한 번에 계산해버리지 않고
    /// 유닛 하나씩(플레이어 턴은 입력을 기다리고, 적 턴은 자동으로) 진행한다. Unity 타입에 의존하지 않는
    /// 순수 C# 클래스(AutoBattleSimulator와 같은 자리) — BattleUIController가 화면에 반영한다.
    /// </summary>
    public class ManualBattleSession
    {
        public const int MaxRounds = 30;

        public List<Combatant> PlayerTeam { get; }
        public List<Combatant> EnemyTeam { get; }
        public List<BattleEvent> Log { get; } = new List<BattleEvent>();
        public int Round { get; private set; } = 1;

        /// <summary>지금 턴인 유닛. 전투가 끝났으면 null.</summary>
        public Combatant CurrentActor { get; private set; }
        public bool CurrentIsPlayer { get; private set; }
        public bool IsOver => CurrentActor == null;
        public BattleOutcome Outcome => CombatResolver.DetermineOutcome(PlayerTeam, EnemyTeam);

        public IEnumerable<Combatant> AliveEnemies => EnemyTeam.Where(c => c.IsAlive);
        public IEnumerable<Combatant> AliveAllies => PlayerTeam.Where(c => c.IsAlive);

        private readonly Dictionary<Combatant, SkillBase> _skills;
        private readonly Dictionary<Combatant, int> _cooldowns = new Dictionary<Combatant, int>();
        private readonly System.Random _rng;
        private readonly List<(Combatant unit, bool isPlayer)> _order;
        private int _orderIndex = -1;

        public ManualBattleSession(List<Combatant> playerTeam, Dictionary<Combatant, SkillBase> skills, List<Combatant> enemyTeam, System.Random rng)
        {
            PlayerTeam = playerTeam;
            EnemyTeam = enemyTeam;
            _skills = skills;
            _rng = rng;
            foreach (var c in playerTeam) _cooldowns[c] = 0;

            CombatResolver.RaiseBattleStart(PlayerTeam, EnemyTeam);
            _order = CombatResolver.BuildTurnOrder(PlayerTeam, EnemyTeam);
            CombatResolver.RaiseRoundStart(_order, Round, _rng);
            AdvanceToNextActor();
        }

        public SkillBase SkillOf(Combatant c) => _skills.TryGetValue(c, out var s) ? s : null;
        public int CooldownRemaining(Combatant c) => _cooldowns.TryGetValue(c, out var v) ? v : 0;

        /// <summary>플레이어 턴 행동. useSkill이 false면 기본공격(패시브의 광역 공격 발동 시 target 무시하고 적 전원을 친다).
        /// useSkill이 true면 CurrentActor의 스킬을 target에게 쓰고 쿨타임을 건다(스킬이 없거나 쿨타임 중이면 무시).</summary>
        public List<BattleEvent> PlayerAct(bool useSkill, Combatant target)
        {
            if (IsOver || !CurrentIsPlayer) return new List<BattleEvent>();
            var actor = CurrentActor;
            var events = new List<BattleEvent>();

            if (useSkill)
            {
                var skill = SkillOf(actor);
                if (skill != null && CooldownRemaining(actor) <= 0 && target != null)
                {
                    events.AddRange(skill.Execute(actor, target, PlayerTeam, EnemyTeam, Round, _rng));
                    _cooldowns[actor] = skill.CooldownTurns;
                }
            }
            else if (actor.Passives.Any(p => p.TryTriggerAoeAttack(_rng)))
            {
                foreach (var enemy in EnemyTeam.Where(c => c.IsAlive).ToList())
                    events.Add(CombatResolver.ResolveAttack(actor, enemy, Round, EnemyTeam, _rng));
            }
            else if (target != null)
            {
                events.Add(CombatResolver.ResolveAttack(actor, target, Round, EnemyTeam, _rng));
            }

            Log.AddRange(events);
            AdvanceToNextActor();
            return events;
        }

        /// <summary>적 턴 행동(자동 - 적은 스킬 없이 기본공격만, 도발 중인 아군을 우선 노린다).
        /// UI가 연출 타이밍에 맞춰 호출한다.</summary>
        public List<BattleEvent> EnemyAct()
        {
            if (IsOver || CurrentIsPlayer) return new List<BattleEvent>();
            var actor = CurrentActor;
            var events = new List<BattleEvent>();
            var target = CombatResolver.PickTarget(PlayerTeam, _rng);
            if (target != null)
                events.Add(CombatResolver.ResolveAttack(actor, target, Round, PlayerTeam, _rng));

            Log.AddRange(events);
            AdvanceToNextActor();
            return events;
        }

        private void AdvanceToNextActor()
        {
            while (true)
            {
                _orderIndex++;

                if (!PlayerTeam.Any(c => c.IsAlive) || !EnemyTeam.Any(c => c.IsAlive))
                {
                    CurrentActor = null;
                    return;
                }

                if (_orderIndex >= _order.Count)
                {
                    if (Round >= MaxRounds)
                    {
                        CurrentActor = null;
                        return;
                    }
                    Round++;
                    _orderIndex = 0;
                    foreach (var key in _cooldowns.Keys.ToList())
                        if (_cooldowns[key] > 0) _cooldowns[key]--;
                    CombatResolver.RaiseRoundStart(_order, Round, _rng);
                }

                var entry = _order[_orderIndex];
                if (entry.unit.IsAlive)
                {
                    CurrentActor = entry.unit;
                    CurrentIsPlayer = entry.isPlayer;
                    return;
                }
            }
        }
    }
}
