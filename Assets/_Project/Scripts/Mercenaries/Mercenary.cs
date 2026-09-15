using System;
using GN3.Combat;
using GN3.CharacterAnim;
using GN3.Traits;

namespace GN3.Mercenaries
{
    public class Mercenary
    {
        public string Id { get; }
        public string Name { get; }
        public MercenaryClassSO Class { get; }
        public int Level { get; private set; }
        public ComposedCharacter Appearance { get; }
        public Personality Personality { get; }
        public bool HasRarePassive { get; }

        public CombatStats CurrentStats => PersonalityTable.Apply(MercenaryStatCalculator.Calculate(Class, Level), Personality);

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int level = 1, ComposedCharacter appearance = null, Personality personality = Personality.Calm, bool hasRarePassive = false)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Class = mercenaryClass;
            Level = level;
            Appearance = appearance ?? new ComposedCharacter();
            Personality = personality;
            HasRarePassive = hasRarePassive;
        }

        public void LevelUp()
        {
            Level++;
        }

        public Combatant ToCombatant()
        {
            return new Combatant(Name, CurrentStats, ClassPassiveFactory.Create(Class.Kind, HasRarePassive));
        }
    }
}
