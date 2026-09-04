using System;
using GN3.Combat;

namespace GN3.Mercenaries
{
    public class Mercenary
    {
        public string Id { get; }
        public string Name { get; }
        public MercenaryClassSO Class { get; }
        public int Level { get; private set; }

        public CombatStats CurrentStats => MercenaryStatCalculator.Calculate(Class, Level);

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int level = 1)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Class = mercenaryClass;
            Level = level;
        }

        public void LevelUp()
        {
            Level++;
        }

        public Combatant ToCombatant()
        {
            return new Combatant(Name, CurrentStats);
        }
    }
}
