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

        public BattleResult Simulate(List<Combatant> teamA, List<Combatant> teamB, int maxRounds = 50)
        {
            var log = new List<BattleEvent>();
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
