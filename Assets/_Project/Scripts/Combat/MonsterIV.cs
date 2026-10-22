using System;

namespace GN3.Combat
{
    /// <summary>몬스터 개체 등급(개체값 합으로 정한다). 보통이 아니면 이름 앞에 붙는다("강한 고블린").</summary>
    public enum MonsterRank { Weak, Normal, Strong, Alpha }

    /// <summary>
    /// 몬스터 개체값: 공격·방어·체력·속도마다 0~31(숨은 값). 같은 몬스터라도 개체마다 0.85~1.15배로 세기가 다르고(속도는 −2~+2),
    /// 합(0~124)으로 등급이 정해진다 — 약한(~12%) · 보통 · 강한(~11%) · 우두머리(~1.5%). EnemySquadGenerator가 적마다 굴린다.
    /// 등급은 Combatant에 따로 두지 않고 이름 앞말로 읽는다(RankOf) — 전리품(LootCatalog.Roll)·도감(Bestiary)이 이름만 받기 때문.
    /// </summary>
    public readonly struct MonsterIV
    {
        public const int Max = 31;
        public const int WeakBelow = 40, StrongFrom = 84, AlphaFrom = 104;
        public const float MinMultiplier = 0.85f, MultiplierRange = 0.30f;

        public readonly int Attack, Defense, Health, Speed;

        public MonsterIV(int attack, int defense, int health, int speed)
        {
            Attack = attack;
            Defense = defense;
            Health = health;
            Speed = speed;
        }

        public int Total => Attack + Defense + Health + Speed;

        public MonsterRank Rank => Total >= AlphaFrom ? MonsterRank.Alpha
            : Total >= StrongFrom ? MonsterRank.Strong
            : Total < WeakBelow ? MonsterRank.Weak
            : MonsterRank.Normal;

        public static MonsterIV Roll(Random rng) =>
            new MonsterIV(rng.Next(Max + 1), rng.Next(Max + 1), rng.Next(Max + 1), rng.Next(Max + 1));

        /// <summary>개체값 하나 → 능력치 배율 0.85~1.15.</summary>
        public static float Multiplier(int iv) => MinMultiplier + MultiplierRange * iv / Max;

        /// <summary>개체값 하나 → 속도 보정 −2~+2.</summary>
        public static int SpeedBonus(int iv) => (int)Math.Round((iv - Max / 2f) / 10f, MidpointRounding.AwayFromZero);

        // ---------- 등급 이름 ----------

        private static readonly string[] Prefixes = { "약한", null, "강한", "우두머리" };

        /// <summary>등급 앞말을 붙인 이름("강한 고블린"). 보통은 그대로.</summary>
        public static string Name(MonsterRank rank, string baseName)
        {
            string prefix = Prefixes[(int)rank];
            return prefix == null ? baseName : $"{prefix} {baseName}";
        }

        /// <summary>등급만 한 단어("약한", "보통", "강한", "우두머리").</summary>
        public static string Label(MonsterRank rank) => Prefixes[(int)rank] ?? "보통";

        /// <summary>적 이름의 앞말로 등급을 읽는다(앞말 없으면 보통 — 보스도 앞말을 붙이지 않아 보통으로 읽힌다).</summary>
        public static MonsterRank RankOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return MonsterRank.Normal;
            for (int i = 0; i < Prefixes.Length; i++)
                if (Prefixes[i] != null && name.StartsWith(Prefixes[i] + " ")) return (MonsterRank)i;
            return MonsterRank.Normal;
        }
    }
}
