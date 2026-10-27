using System.Collections.Generic;
using GN3.Combat;

namespace GN3.Mercenaries
{
    public static class ClassPassiveFactory
    {
        /// <summary>용병 생성 시 일반 패시브 대신 레어 패시브를 받을 확률.</summary>
        public const float RarePassiveChance = 0.15f;

        public static bool RollRare(System.Random rng) => rng.NextDouble() < RarePassiveChance;

        /// <summary>직업 패시브(일반 또는 레어) + 직업 고유 특성(있으면 맨 뒤 — 용병 정보는 [0]을 패시브로 보인다).</summary>
        public static List<PassiveBase> Create(MercenaryClassKind kind, bool rare)
        {
            var passives = CreatePassive(kind, rare);
            var trait = TraitFor(kind);
            if (trait != null) passives.Add(trait);
            return passives;
        }

        /// <summary>
        /// 직업 고유 특성: 레어 패시브를 받아도 늘 붙는다. 2026-10-26 근접 직업이 너무 잘 쓰러져 —
        /// 전사는 단단하게(받는 피해 감소), 암살자는 날렵하게(회피 한 번 더). 없으면 null.
        /// </summary>
        public static PassiveBase TraitFor(MercenaryClassKind kind) => kind switch
        {
            MercenaryClassKind.Warrior => new HeavyArmorPassive(),
            MercenaryClassKind.Assassin => new NimblePassive(),
            _ => null,
        };

        private static List<PassiveBase> CreatePassive(MercenaryClassKind kind, bool rare)
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
                    case MercenaryClassKind.Mage:
                        return new List<PassiveBase> { new ChainLightningPassive() };
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
                case MercenaryClassKind.Mage:
                    return new List<PassiveBase> { new ArcaneAmpPassive() };
                default:
                    return new List<PassiveBase>();
            }
        }
    }
}
