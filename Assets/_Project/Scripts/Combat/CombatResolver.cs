using System;
using System.Collections.Generic;
using System.Linq;

namespace GN3.Combat
{
    /// <summary>
    /// 전투 핵심 규칙(턴 순서/대상 선정/공격 판정/패시브 훅 배선)을 한 곳에 모은 static 헬퍼.
    /// AutoBattleSimulator(자동 전투)와 ManualBattleSession(직접 전투)이 똑같이 가져다 써서
    /// 두 모드가 항상 같은 규칙으로 싸우게 한다(로직이 두 군데로 갈라져 결과가 어긋나는 것을 막음).
    /// </summary>
    public static class CombatResolver
    {
        public static List<(Combatant unit, bool isTeamA)> BuildTurnOrder(List<Combatant> teamA, List<Combatant> teamB)
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

        public static void RaiseBattleStart(List<Combatant> teamA, List<Combatant> teamB)
        {
            foreach (var c in teamA)
                foreach (var passive in c.Passives)
                    passive.OnBattleStart(c, teamA, teamB);
            foreach (var c in teamB)
                foreach (var passive in c.Passives)
                    passive.OnBattleStart(c, teamB, teamA);
        }

        public static void RaiseRoundStart(List<(Combatant unit, bool isTeamA)> order, int round, Random rng)
        {
            foreach (var entry in order)
            {
                if (!entry.unit.IsAlive) continue;
                foreach (var passive in entry.unit.Passives)
                    passive.OnRoundStart(entry.unit, round, rng);
            }
        }

        /// <summary>살아있는 적 중 하나를 고른다. 도발 중인 적이 있으면 그중에서만 고른다.</summary>
        public static Combatant PickTarget(List<Combatant> enemies, Random rng)
        {
            var alive = enemies.Where(c => c.IsAlive).ToList();
            if (alive.Count == 0) return null;

            var taunting = alive.Where(c => c.IsTaunting).ToList();
            if (taunting.Count > 0)
                return taunting[rng.Next(taunting.Count)];

            return alive[rng.Next(alive.Count)];
        }

        /// <summary>
        /// 단일 대상 공격 판정 한 번(패시브 훅 → 회피 → 치명상 생존 판정 → 데미지 적용 → 후처리 패시브 → 아군 사망 알림).
        /// damageMultiplier/ignoreDefense는 액티브 스킬이 기본 공격 공식을 변형할 때 쓴다(기본값은 평범한 기본 공격과 동일).
        /// </summary>
        public static BattleEvent ResolveAttack(Combatant attacker, Combatant target, int round, List<Combatant> targetTeam, Random rng,
            float damageMultiplier = 1f, bool ignoreDefense = false)
        {
            int baseDamage = ignoreDefense
                ? Math.Max(CombatFormulas.MinimumDamage, attacker.Stats.Attack)
                : CombatFormulas.CalculateDamage(attacker, target);
            if (damageMultiplier != 1f)
                baseDamage = Math.Max(CombatFormulas.MinimumDamage, (int)Math.Round(baseDamage * damageMultiplier));

            var context = new AttackContext(attacker, target, round, baseDamage, rng);

            foreach (var passive in attacker.Passives)
                passive.OnAttack(context);
            foreach (var passive in target.Passives)
                passive.OnDefend(context);

            if (!context.Evaded && rng.NextDouble() < CombatFormulas.CalculateEvadeChance(target))
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
            var evt = new BattleEvent(round, attacker, target, loggedDamage, targetDefeated, context.Evaded);

            if (targetDefeated)
                RaiseAllyDefeated(targetTeam, target);

            return evt;
        }

        public static void RaiseAllyDefeated(List<Combatant> allies, Combatant defeated)
        {
            foreach (var c in allies)
            {
                if (!c.IsAlive) continue;
                foreach (var passive in c.Passives)
                    passive.OnAllyDefeated(c, defeated);
            }
        }

        public static BattleOutcome DetermineOutcome(List<Combatant> teamA, List<Combatant> teamB)
        {
            bool aAlive = teamA.Any(c => c.IsAlive);
            bool bAlive = teamB.Any(c => c.IsAlive);
            if (aAlive && !bAlive) return BattleOutcome.TeamAVictory;
            if (bAlive && !aAlive) return BattleOutcome.TeamBVictory;
            return BattleOutcome.Draw;
        }
    }
}
