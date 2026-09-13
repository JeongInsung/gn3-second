using System.Collections.Generic;
using GN3.Combat;

namespace GN3.Mercenaries
{
    public static class ClassPassiveFactory
    {
        /// <summary>용병 생성 시 일반 패시브 대신 레어 패시브를 받을 확률.</summary>
        public const float RarePassiveChance = 0.15f;

        public static bool RollRare(System.Random rng) => rng.NextDouble() < RarePassiveChance;

        public static List<PassiveBase> Create(MercenaryClassKind kind, bool rare)
        {
            if (rare)
            {
                switch (kind)
                {
                    case MercenaryClassKind.Warrior:
                        return new List<PassiveBase> { new AoeTauntPassive() };
                    case MercenaryClassKind.Archer:
                        return new List<PassiveBase> { new AoeAttackPassive() };
                    case MercenaryClassKind.Healer:
                        return new List<PassiveBase> { new ReviveAllyPassive() };
                    case MercenaryClassKind.Assassin:
                        return new List<PassiveBase> { new InstakillPassive() };
                }
            }

            switch (kind)
            {
                case MercenaryClassKind.Warrior:
                    return new List<PassiveBase> { new UnyieldingBulwarkPassive() };
                case MercenaryClassKind.Archer:
                    return new List<PassiveBase> { new PrecisionShotPassive() };
                case MercenaryClassKind.Healer:
                    return new List<PassiveBase> { new HealingAuraPassive() };
                case MercenaryClassKind.Assassin:
                    return new List<PassiveBase> { new AmbushPassive() };
                default:
                    return new List<PassiveBase>();
            }
        }
    }
}
