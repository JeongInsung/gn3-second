using GN3.Combat;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 등급 F~S. 용병의 '등급'은 지금 전투력 구간(GradeTable.ForCombatPower — 키우면 승급),
    /// '재능'은 타고난 값(시장에서 생길 때 정해지고 높을수록 드묾, 성장 속도 배율 GrowthTuning.TalentRate). 퀘스트 등급에도 같은 글자를 쓴다.
    /// </summary>
    public enum MercenaryGrade { F, E, D, C, B, A, S }

    /// <summary>재능 등장 확률·재능 고용비 배율·전투력 등급 구간·표시 색.</summary>
    public static class GradeTable
    {
        //                                         F      E      D      C      B      A      S
        private static readonly float[] StatMultipliers  = { 0.80f, 0.90f, 1.00f, 1.12f, 1.25f, 1.45f, 1.70f };
        private static readonly float[] Chances          = { 0.20f, 0.25f, 0.22f, 0.15f, 0.10f, 0.06f, 0.02f };
        private static readonly float[] PriceMultipliers = { 0.7f,  0.85f, 1.0f,  1.2f,  1.45f, 1.8f,  2.3f }; // 재능 고용비 배율(전투력에 곱한다)
        private static readonly string[] HexColors = { "8a8a8a", "b8b8b8", "6fbf5a", "5a9be0", "a66be0", "f08a3c", "ffd34a" };

        /// <summary>예전(저장 버전 3 이하) 재능 능력치 배율. 예전 저장을 불러올 때 전투력을 맞추는 데만 쓴다.</summary>
        public static float LegacyStatMultiplier(MercenaryGrade grade) => StatMultipliers[(int)grade];
        public static float PriceMultiplier(MercenaryGrade talent) => PriceMultipliers[(int)talent];

        /// <summary>전투력 → 등급(GrowthTuning.GradePowerThresholds 구간).</summary>
        public static MercenaryGrade ForCombatPower(int power)
        {
            var thresholds = GrowthTuning.GradePowerThresholds;
            for (int i = thresholds.Length - 1; i > 0; i--)
                if (power >= thresholds[i]) return (MercenaryGrade)i;
            return MercenaryGrade.F;
        }

        /// <summary>다음 등급까지 필요한 전투력(S면 0).</summary>
        public static int PowerToNextGrade(int power)
        {
            int next = (int)ForCombatPower(power) + 1;
            return next < GrowthTuning.GradePowerThresholds.Length ? GrowthTuning.GradePowerThresholds[next] - power : 0;
        }
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

        /// <summary>예전 공식: 기본 능력치에 재능 배율(공격·방어·체력)을 건다. 저장 버전 3 이하를 불러올 때 예전 전투력을 재는 데만 쓴다.</summary>
        public static CombatStats ApplyLegacyTalent(CombatStats stats, MercenaryGrade talent)
        {
            float m = LegacyStatMultiplier(talent);
            return new CombatStats(
                attack: Mathf.Max(1, Mathf.RoundToInt(stats.Attack * m)),
                defense: Mathf.Max(0, Mathf.RoundToInt(stats.Defense * m)),
                maxHealth: Mathf.Max(1, Mathf.RoundToInt(stats.MaxHealth * m)),
                moveSpeed: stats.MoveSpeed,
                magic: stats.Magic,
                holy: stats.Holy);
        }

        /// <summary>전투력 = 공격×4 + 방어×3 + 최대 체력 + 속도×2 + 마력×4 + 신성력×3.</summary>
        public static int CombatPower(CombatStats stats) =>
            stats.Attack * 4 + stats.Defense * 3 + stats.MaxHealth + stats.MoveSpeed * 2 + stats.Magic * 4 + stats.Holy * 3;
    }
}
