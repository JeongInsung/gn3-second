using System;

namespace GN3.Combat
{
    public class Combatant
    {
        public string Name { get; }
        public CombatStats Stats { get; }
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public Combatant(string name, CombatStats stats)
        {
            Name = name;
            Stats = stats;
            CurrentHealth = stats.MaxHealth;
        }

        public void TakeDamage(int damage)
        {
            CurrentHealth = Math.Max(0, CurrentHealth - damage);
        }
    }
}
