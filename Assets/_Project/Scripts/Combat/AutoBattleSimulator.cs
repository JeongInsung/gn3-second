using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    public class AutoBattleSimulator
    {
        private readonly Random _random;

        public AutoBattleSimulator(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <param name="skills">유닛별 스킬(없으면 기본공격만). 쿨타임이 돌았고 쓸 대상이 있으면(SkillAI) 그 차례엔 기본공격 대신 스킬을 쓴다.</param>
        public BattleResult Simulate(List<Combatant> teamA, List<Combatant> teamB, int maxRounds = 50,
            IReadOnlyDictionary<Combatant, SkillBase> skills = null)
        {
            var log = new List<BattleEvent>();
            var cooldowns = new Dictionary<Combatant, int>(); // 남은 라운드(0이면 쓸 수 있음)
            var order = CombatResolver.BuildTurnOrder(teamA, teamB);

            CombatResolver.RaiseBattleStart(teamA, teamB);

            int round = 0;
            while (round < maxRounds && teamA.Any(c => c.IsAlive) && teamB.Any(c => c.IsAlive))
            {
                round++;
                CombatResolver.RaiseRoundStart(order, round, _random);

                foreach (var entry in order)
                {
                    if (!entry.unit.IsAlive) continue;

                    var enemies = entry.isTeamA ? teamB : teamA;
                    var allies = entry.isTeamA ? teamA : teamB;

                    if (skills != null && skills.TryGetValue(entry.unit, out var skill) && skill != null)
                    {
                        cooldowns.TryGetValue(entry.unit, out int left);
                        if (left > 0) cooldowns[entry.unit] = left - 1;
                        else
                        {
                            var skillTarget = SkillAI.ChooseTarget(skill, allies, enemies, null);
                            if (skillTarget != null)
                            {
                                log.AddRange(skill.Execute(entry.unit, skillTarget, allies, enemies, round, _random));
                                cooldowns[entry.unit] = SkillAI.CooldownRounds(skill);
                                if (!teamA.Any(c => c.IsAlive) || !teamB.Any(c => c.IsAlive)) break;
                                continue;
                            }
                        }
                    }

                    if (entry.unit.Passives.Any(p => p.TryTriggerAoeAttack(_random)))
                    {
                        foreach (var enemyTarget in enemies.Where(c => c.IsAlive).ToList())
                            log.Add(CombatResolver.ResolveAttack(entry.unit, enemyTarget, round, enemies, _random));
                    }
                    else
                    {
                        var target = CombatResolver.PickTarget(enemies, _random);
                        if (target == null) break;
                        log.Add(CombatResolver.ResolveAttack(entry.unit, target, round, enemies, _random));
                    }

                    if (!teamA.Any(c => c.IsAlive) || !teamB.Any(c => c.IsAlive))
                        break;
                }
            }

            return new BattleResult
            {
                Outcome = CombatResolver.DetermineOutcome(teamA, teamB),
                Rounds = round,
                Log = log,
                TeamA = teamA,
                TeamB = teamB
            };
        }
    }
}
