using System.Collections.Generic;
using GN3.Combat;
using UnityEngine;

namespace GN3.Traits
{
    public static class PersonalityTable
    {
        private static readonly Dictionary<Personality, PersonalityModifier> Modifiers = new Dictionary<Personality, PersonalityModifier>
        {
            { Personality.Brave,    new PersonalityModifier("용맹", "공격력 +15%", 1.15f, 1.0f, 1.0f) },
            { Personality.Coward,   new PersonalityModifier("겁쟁이", "공격력 -20% / 방어력 -10%", 0.8f, 0.9f, 1.0f) },
            { Personality.Calm,     new PersonalityModifier("냉정", "방어력 +15%", 1.0f, 1.15f, 1.0f) },
            { Personality.Reckless, new PersonalityModifier("저돌적", "공격력 +25% / 방어력 -15%", 1.25f, 0.85f, 1.0f) },
            { Personality.Cautious, new PersonalityModifier("신중", "공격력 -10% / 방어력 +20%", 0.9f, 1.2f, 1.0f) },
            { Personality.Cheerful, new PersonalityModifier("낙천", "최대체력 +15%", 1.0f, 1.0f, 1.15f) },
            { Personality.Hotblooded,  new PersonalityModifier("다혈질", "공격력 +30% / 방어력 -25%", 1.30f, 0.75f, 1.0f) },
            { Personality.Composed,    new PersonalityModifier("침착함", "방어력 +10% / 체력 +10%", 1.0f, 1.10f, 1.10f) },
            { Personality.Pessimistic, new PersonalityModifier("비관적", "공격력 -15% / 방어력 +20% / 체력 -10%", 0.85f, 1.20f, 0.90f) },
            { Personality.Confident,   new PersonalityModifier("자신만만", "공격력 +10% / 방어력 +10%", 1.10f, 1.10f, 1.0f) },
            { Personality.Lethargic,   new PersonalityModifier("무기력", "공격력 -15% / 체력 -15%", 0.85f, 1.0f, 0.85f) },
            { Personality.Nervous,     new PersonalityModifier("예민함", "공격력 -10% / 방어력 -10%", 0.90f, 0.90f, 1.0f) },
            { Personality.Steady,      new PersonalityModifier("우직함", "최대체력 +25%", 1.0f, 1.0f, 1.25f) },
        };

        public static PersonalityModifier Get(Personality personality) => Modifiers[personality];

        public static Personality GetRandom(System.Random rng)
        {
            var values = (Personality[])System.Enum.GetValues(typeof(Personality));
            return values[rng.Next(values.Length)];
        }

        public static CombatStats Apply(CombatStats baseStats, Personality personality)
        {
            var mod = Get(personality);
            return new CombatStats(
                attack: Mathf.Max(1, Mathf.RoundToInt(baseStats.Attack * mod.AttackMultiplier)),
                defense: Mathf.Max(0, Mathf.RoundToInt(baseStats.Defense * mod.DefenseMultiplier)),
                maxHealth: Mathf.Max(1, Mathf.RoundToInt(baseStats.MaxHealth * mod.HealthMultiplier)),
                moveSpeed: baseStats.MoveSpeed
            );
        }
    }
}
