using GN3.Combat;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>용병의 타고난 등급. 시장에서 생길 때 정해지고, 높을수록 드물고 강하다.</summary>
    public enum MercenaryGrade { F, E, D, C, B, A, S }

    /// <summary>등급별 능력치 배율·등장 확률·고용비 배율·표시 색.</summary>
    public static class GradeTable
    {
        //                                         F      E      D      C      B      A      S
        private static readonly float[] StatMultipliers  = { 0.80f, 0.90f, 1.00f, 1.12f, 1.25f, 1.45f, 1.70f };
        private static readonly float[] Chances          = { 0.20f, 0.25f, 0.22f, 0.15f, 0.10f, 0.06f, 0.02f };
        private static readonly float[] PriceMultipliers = { 0.6f,  0.8f,  1.0f,  1.3f,  1.7f,  2.3f,  3.2f };
        private static readonly string[] HexColors = { "8a8a8a", "b8b8b8", "6fbf5a", "5a9be0", "a66be0", "f08a3c", "ffd34a" };

        public static float StatMultiplier(MercenaryGrade grade) => StatMultipliers[(int)grade];
        public static float PriceMultiplier(MercenaryGrade grade) => PriceMultipliers[(int)grade];
        public static string Label(MercenaryGrade grade) => grade.ToString();

        public static Color Color(MercenaryGrade grade) =>
            ColorUtility.TryParseHtmlString("#" + HexColors[(int)grade], out var c) ? c : UnityEngine.Color.white;

        /// <summary>uGUI rich text용 "[S]"(등급 색).</summary>
        public static string RichLabel(MercenaryGrade grade) => $"<color=#{HexColors[(int)grade]}>[{Label(grade)}]</color>";

        /// <summary>등장 확률대로 굴린다. maxGrade보다 높은 등급은 빼고, 남은 등급의 확률 비율대로 뽑는다(길드 단계 제한).</summary>
        public static MercenaryGrade Roll(System.Random rng, MercenaryGrade maxGrade = MercenaryGrade.S)
        {
            int last = (int)maxGrade;
            double total = 0;
            for (int i = 0; i <= last; i++) total += Chances[i];
            double roll = rng.NextDouble() * total;
            double sum = 0;
            for (int i = 0; i <= last; i++)
            {
                sum += Chances[i];
                if (roll < sum) return (MercenaryGrade)i;
            }
            return MercenaryGrade.F;
        }

        /// <summary>클래스·레벨 기본 능력치에 등급 배율(공격·방어·체력)을 건다. 이동 속도는 회피율이 깨지지 않게 그대로.</summary>
        public static CombatStats Apply(CombatStats stats, MercenaryGrade grade)
        {
            float m = StatMultiplier(grade);
            return new CombatStats(
                attack: Mathf.Max(1, Mathf.RoundToInt(stats.Attack * m)),
                defense: Mathf.Max(0, Mathf.RoundToInt(stats.Defense * m)),
                maxHealth: Mathf.Max(1, Mathf.RoundToInt(stats.MaxHealth * m)),
                moveSpeed: stats.MoveSpeed);
        }

        /// <summary>전투력 = 공격×4 + 방어×3 + 최대 체력 + 속도×2.</summary>
        public static int CombatPower(CombatStats stats) =>
            stats.Attack * 4 + stats.Defense * 3 + stats.MaxHealth + stats.MoveSpeed * 2;
    }
}
