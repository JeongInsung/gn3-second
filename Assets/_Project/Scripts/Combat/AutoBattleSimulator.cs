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
            var order = BuildTurnOrder(teamA, teamB);

            int round = 0;
            while (round < maxRounds && teamA.Any(c => c.IsAlive) && teamB.Any(c => c.IsAlive))
            {
                round++;
                foreach (var entry in order)
                {
                    if (!entry.unit.IsAlive) continue;

                    var enemies = entry.isTeamA ? teamB : teamA;
                    var target = PickTarget(enemies);
                    if (target == null) break;

                    int damage = CombatFormulas.CalculateDamage(entry.unit, target);
                    target.TakeDamage(damage);
                    log.Add(new BattleEvent(round, entry.unit, target, damage, !target.IsAlive));

                    if (!teamA.Any(c => c.IsAlive) || !teamB.Any(c => c.IsAlive))
                        break;
                }
            }

            return new BattleResult
            {
                Outcome = DetermineOutcome(teamA, teamB),
                Rounds = round,
                Log = log,
                TeamA = teamA,
                TeamB = teamB
            };
        }

        private Combatant PickTarget(List<Combatant> enemies)
        {
            var alive = enemies.Where(c => c.IsAlive).ToList();
            return alive.Count == 0 ? null : alive[_random.Next(alive.Count)];
        }

        private static List<(Combatant unit, bool isTeamA)> BuildTurnOrder(List<Combatant> teamA, List<Combatant> teamB)
        {
            var order = new List<(Combatant, bool)>();
            int max = Math.Max(teamA.Count, teamB.Count);
            for (int i = 0; i < max; i++)
            {
                if (i < teamA.Count) order.Add((teamA[i], true));
                if (i < teamB.Count) order.Add((teamB[i], false));
            }
            return order;
        }

        private static BattleOutcome DetermineOutcome(List<Combatant> teamA, List<Combatant> teamB)
        {
            bool aAlive = teamA.Any(c => c.IsAlive);
            bool bAlive = teamB.Any(c => c.IsAlive);
            if (aAlive && !bAlive) return BattleOutcome.TeamAVictory;
            if (bAlive && !aAlive) return BattleOutcome.TeamBVictory;
            return BattleOutcome.Draw;
        }
    }
}
