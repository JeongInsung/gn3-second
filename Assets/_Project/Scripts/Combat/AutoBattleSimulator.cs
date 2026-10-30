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

        // 앤트킹 포효(직접 전투의 기절 포효와 같은 뜻): 이 라운드마다 teamA 전원이 한 번 쉰다. 격노(체력 절반 아래)면 더 자주.
        public const int RoarEveryRounds = 6;
        public const int RoarEveryRoundsEnraged = 5;

        /// <param name="skills">유닛별 스킬(없으면 기본공격만). 쿨타임이 돌았고 쓸 대상이 있으면(SkillAI) 그 차례엔 기본공격 대신 스킬을 쓴다.</param>
        /// <param name="summonFactory">teamB 앤트킹이 격노할 때 부를 졸개(마릿수 → 적). 없으면 소환 없음. 불러낸 적은 teamB에 더해진다.</param>
        public BattleResult Simulate(List<Combatant> teamA, List<Combatant> teamB, int maxRounds = 50,
            IReadOnlyDictionary<Combatant, SkillBase> skills = null, Func<int, List<Combatant>> summonFactory = null)
        {
            var enraged = new HashSet<Combatant>();
            int lastRoar = 0;
            var log = new List<BattleEvent>();
            var cooldowns = new Dictionary<Combatant, int>(); // 남은 라운드(0이면 쓸 수 있음)
            var order = CombatResolver.BuildTurnOrder(teamA, teamB);

            CombatResolver.RaiseBattleStart(teamA, teamB);

            int round = 0;
            while (round < maxRounds && teamA.Any(c => c.IsAlive) && teamB.Any(c => c.IsAlive))
            {
                round++;
                log.AddRange(CombatResolver.TickPoison(teamA, teamB, round)); // 독은 라운드 시작마다 한 번
                if (!teamA.Any(c => c.IsAlive) || !teamB.Any(c => c.IsAlive)) break;
                CombatResolver.RaiseRoundStart(order, round, _random);

                // 앤트킹: 체력이 절반 아래로 처음 내려가면 격노(졸개 소환), 때가 되면 포효해 teamA가 이번 라운드를 쉰다
                bool roared = false;
                foreach (var boss in teamB.Where(c => c.IsAlive && GN3.World.RegionInfo.IsEnt(c.Name)).ToList())
                {
                    if (!enraged.Contains(boss) && boss.CurrentHealth * 2 < boss.Stats.MaxHealth)
                    {
                        enraged.Add(boss);
                        log.Add(new BattleEvent(round, boss, boss, 0, false, proc: "격노!"));
                        var summons = summonFactory?.Invoke(FieldBattleSession.EnrageSummonCount);
                        if (summons != null && summons.Count > 0)
                        {
                            foreach (var c in summons)
                                foreach (var passive in c.Passives) passive.OnBattleStart(c, teamB, teamA);
                            teamB.AddRange(summons);
                            order = CombatResolver.BuildTurnOrder(teamA, teamB);
                        }
                    }
                    int every = enraged.Contains(boss) ? RoarEveryRoundsEnraged : RoarEveryRounds;
                    if (!roared && round - lastRoar >= every)
                    {
                        roared = true;
                        lastRoar = round;
                        log.Add(new BattleEvent(round, boss, boss, 0, false, proc: "포효!"));
                    }
                }

                foreach (var entry in order)
                {
                    if (!entry.unit.IsAlive) continue;
                    if (roared && entry.isTeamA) continue; // 포효에 얼어 이번 라운드는 쉰다

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
