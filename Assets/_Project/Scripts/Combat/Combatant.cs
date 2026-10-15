using System;
using System.Collections.Generic;

namespace GN3.Combat
{
    public class Combatant
    {
        public string Name { get; }
        public CombatStats Stats { get; }
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;
        public List<PassiveBase> Passives { get; }
        public bool IsTaunting { get; set; }

        public Combatant(string name, CombatStats stats, List<PassiveBase> passives = null)
        {
            Name = name;
            Stats = stats;
            CurrentHealth = stats.MaxHealth;
            Passives = passives ?? new List<PassiveBase>();
        }

        public void TakeDamage(int damage)
        {
            CurrentHealth = Math.Max(0, CurrentHealth - damage);
        }

        public void Heal(int amount)
        {
            CurrentHealth = Math.Min(Stats.MaxHealth, CurrentHealth + amount);
        }

        // ---------- 독(CombatResolver.TickPoison이 깎는다) ----------

        /// <summary>독 한 번에 받는 피해.</summary>
        public int PoisonDamage { get; private set; }
        /// <summary>남은 독 피해 횟수(0이면 중독 아님).</summary>
        public int PoisonTicks { get; private set; }
        /// <summary>독을 건 유닛(로그·연출용).</summary>
        public Combatant PoisonSource { get; private set; }
        public bool IsPoisoned => PoisonTicks > 0 && IsAlive;

        /// <summary>독을 건다. 이미 중독이면 남은 횟수를 새로 채우고 더 센 피해를 쓴다(겹쳐 쌓이지 않음).</summary>
        public void ApplyPoison(Combatant source, int damage, int ticks)
        {
            if (!IsAlive || damage <= 0 || ticks <= 0) return;
            PoisonDamage = PoisonTicks > 0 ? Math.Max(PoisonDamage, damage) : damage;
            PoisonTicks = Math.Max(PoisonTicks, ticks);
            PoisonSource = source;
        }

        /// <summary>독 한 번: 줄어든 횟수를 반영하고 실제로 들어간 피해를 돌려준다.</summary>
        public int TakePoisonTick()
        {
            if (!IsPoisoned) return 0;
            int damage = Math.Min(PoisonDamage, CurrentHealth);
            TakeDamage(damage);
            PoisonTicks--;
            if (PoisonTicks <= 0 || !IsAlive) ClearPoison();
            return damage;
        }

        public void ClearPoison()
        {
            PoisonTicks = 0;
            PoisonDamage = 0;
            PoisonSource = null;
        }
    }
}
