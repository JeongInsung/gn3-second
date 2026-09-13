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

            RaiseBattleStart(teamA, teamB);

            int round = 0;
            while (round < maxRounds && teamA.Any(c => c.IsAlive) && teamB.Any(c => c.IsAlive))
            {
                round++;
                RaiseRoundStart(order, round);

                foreach (var entry in order)
                {
                    if (!entry.unit.IsAlive) continue;

                    var enemies = entry.isTeamA ? teamB : teamA;

                    if (entry.unit.Passives.Any(p => p.TryTriggerAoeAttack(_random)))
                    {
                        foreach (var enemyTarget in enemies.Where(c => c.IsAlive).ToList())
                            ResolveAttack(entry.unit, enemyTarget, round, enemies, log);
                    }
                    else
                    {
                        var target = PickTarget(enemies);
                        if (target == null) break;
                        ResolveAttack(entry.unit, target, round, enemies, log);
                    }

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

        private static void RaiseBattleStart(List<Combatant> teamA, List<Combatant> teamB)
        {
            foreach (var c in teamA)
                foreach (var passive in c.Passives)
                    passive.OnBattleStart(c, teamA, teamB);
            foreach (var c in teamB)
                foreach (var passive in c.Passives)
                    passive.OnBattleStart(c, teamB, teamA);
        }

        private void RaiseRoundStart(List<(Combatant unit, bool isTeamA)> order, int round)
        {
            foreach (var entry in order)
            {
                if (!entry.unit.IsAlive) continue;
                foreach (var passive in entry.unit.Passives)
                    passive.OnRoundStart(entry.unit, round, _random);
            }
        }

        private void ResolveAttack(Combatant attacker, Combatant target, int round, List<Combatant> targetTeam, List<BattleEvent> log)
        {
            int baseDamage = CombatFormulas.CalculateDamage(attacker, target);
            var context = new AttackContext(attacker, target, round, baseDamage, _random);

            foreach (var passive in attacker.Passives)
                passive.OnAttack(context);
            foreach (var passive in target.Passives)
                passive.OnDefend(context);

            if (!context.Evaded && _random.NextDouble() < CombatFormulas.CalculateEvadeChance(target))
                context.Evaded = true;

            bool targetDefeated = false;
            if (!context.Evaded)
            {
                if (context.Damage >= target.CurrentHealth &&
                    target.Passives.Any(p => p.OnLethalDamage(context)))
                {
                    context.Damage = Math.Max(0, target.CurrentHealth - 1);
                }

                target.TakeDamage(context.Damage);
                foreach (var passive in attacker.Passives)
                    passive.OnDamageDealt(context);

                targetDefeated = !target.IsAlive;
            }

            int loggedDamage = context.Evaded ? 0 : context.Damage;
            log.Add(new BattleEvent(round, attacker, target, loggedDamage, targetDefeated, context.Evaded));

            if (targetDefeated)
                RaiseAllyDefeated(targetTeam, target);
        }

        private static void RaiseAllyDefeated(List<Combatant> allies, Combatant defeated)
        {
            foreach (var c in allies)
            {
                if (!c.IsAlive) continue;
                foreach (var passive in c.Passives)
                    passive.OnAllyDefeated(c, defeated);
            }
        }

        private Combatant PickTarget(List<Combatant> enemies)
        {
            var alive = enemies.Where(c => c.IsAlive).ToList();
            if (alive.Count == 0) return null;

            var taunting = alive.Where(c => c.IsTaunting).ToList();
            if (taunting.Count > 0)
                return taunting[_random.Next(taunting.Count)];

            return alive[_random.Next(alive.Count)];
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
