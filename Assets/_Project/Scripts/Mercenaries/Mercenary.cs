using System;
using GN3.Combat;
using GN3.Characters;

namespace GN3.Mercenaries
{
    public class Mercenary
    {
        public string Id { get; }
        public string Name { get; }
        public MercenaryClassSO Class { get; }
        public int Level { get; private set; }
        public CharacterAppearance Appearance { get; }

        public CombatStats CurrentStats => MercenaryStatCalculator.Calculate(Class, Level);

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int level = 1, CharacterAppearance appearance = null)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Class = mercenaryClass;
            Level = level;
            Appearance = appearance ?? new CharacterAppearance();
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
