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

        /// <summary>전투/이동 중 습격 등으로 깎이고, 회복 수단이 생기기 전까지는 계속 남아있는 실제 체력.</summary>
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public Mercenary(string name, MercenaryClassSO mercenaryClass, int level = 1, ComposedCharacter appearance = null, Personality personality = Personality.Calm, bool hasRarePassive = false)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Class = mercenaryClass;
            Level = level;
            Appearance = appearance ?? new ComposedCharacter();
            Personality = personality;
            HasRarePassive = hasRarePassive;
            CurrentHealth = CurrentStats.MaxHealth;
        }

        public void LevelUp()
        {
            Level++;
        }

        /// <summary>전투 시뮬레이션이 끝난 뒤 그 결과(Combatant.CurrentHealth)를 그대로 반영할 때 쓴다.</summary>
        public void SetHealth(int value)
        {
            CurrentHealth = Math.Clamp(value, 0, CurrentStats.MaxHealth);
        }

        /// <summary>휴식으로 체력을 완전히 회복한다.</summary>
        public void HealFully()
        {
            CurrentHealth = CurrentStats.MaxHealth;
        }

        public Combatant ToCombatant()
        {
            var combatant = new Combatant(Name, CurrentStats, ClassPassiveFactory.Create(Class.Kind, HasRarePassive));
            int missingHealth = combatant.Stats.MaxHealth - CurrentHealth;
            if (missingHealth > 0)
                combatant.TakeDamage(missingHealth);
            return combatant;
        }
    }
}
