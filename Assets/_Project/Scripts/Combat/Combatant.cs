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
    }
}
