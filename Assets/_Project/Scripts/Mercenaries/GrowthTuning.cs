using System;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병 성장·나이 수치를 한곳에 모은 표(레벨·경험치 대신). 규칙은 Mercenary(성장 횟수)·TrainingHall·ExpeditionBattle·MercenaryAge에 있고 숫자만 여기서 읽는다.
    /// 성장 1회 = 직업의 성장치(MercenaryClassSO.AttackGrowth 등) 하나만큼 그 능력치가 오른다. 어느 능력치인지는 직업 성향대로 무작위(Mercenary.PickGrowthStat).
    /// 성장량 = 기본량 × 나이 배율(RateForAge). 소수점은 용병마다 쌓았다가 1이 될 때 성장 1회.
    /// </summary>
    public static class GrowthTuning
    {
        // ---------- 성장 원천 ----------
        // 1년 기준(20대, 하루 훈련 1시간 + 5일마다 2단계 승리)으로 예전 레벨제 1년(약 Lv11, 전사 전투력 +200)과 비슷하게 맞춤: 성장 약 44회 · 전사 전투력 약 +300
        public const float TrainingGrowthPerHour = 1f / 20f;  // 훈련소: 나이 배율 1이면 20시간에 1회
        public const float VictoryGrowth = 0.25f;              // 파견 전투 승리(살아남은 사람)
        public const float VictoryGrowthPerQuestGrade = 0.1f;  // + 퀘스트 등급(0~)마다
        public const float DefeatGrowthRatio = 0.5f;           // 패배는 승리의 절반
        public const float AmbushGrowth = 0.05f;               // 이동 중 습격에서 살아남음

        // ---------- 나이 ----------
        public const int MinHireAge = 16;
        public const int MaxHireAge = 45;
        public const int DeclineStartAge = 45;   // 이 나이부터 생일마다 쇠퇴
        public const int DeclinePerYear = 2;     // 생일마다 잃는 성장 횟수
        public const int RetireAge = 55;         // 이 나이가 되면 은퇴(파견 중이면 돌아온 뒤)
        public const float StartGrowthPerYear = 0.5f; // 시장에 나올 때 (나이 - 16) × 이것만큼 이미 성장해 있다(경력)

        // ---------- 재능(타고난 등급) · 등급(전투력 구간) ----------
        // 재능 F~S는 성장 속도 배율로만 쓴다(능력치 배율 없음). 성장 배율 = 나이 배율 × 재능 배율.
        //                                                F     E     D     C      B     A     S
        private static readonly float[] TalentRates = { 0.6f, 0.8f, 1f, 1.15f, 1.3f, 1.5f, 1.8f };
        public static float TalentRate(MercenaryGrade talent) => TalentRates[(int)talent];

        // 등급 = 전투력 구간(피로·사기·부상 뺀 전투력, Mercenary.GradePower). E 100 · D 150 · C 220 · B 320 · A 460 · S 650부터(그 아래는 F).
        // 갓 들어온 용병은 F~C(견습 시장 전사 약 150 D, 전설 시장 약 245 C). 25세를 1년 키우면(하루 훈련 1시간 + 5일마다 승리)
        // 전사 재능 F 330 B · D 450 B · S 690 S, 마법사 D 560 A(공격 대신 마력, 같은 가중치), 힐러 D 약 340 B(신성력 ×3 포함, 전엔 280 C) — 재능이 높거나 오래 키워야 S.
        public static readonly int[] GradePowerThresholds = { 0, 100, 150, 220, 320, 460, 650 };

        /// <summary>나이에 따른 성장 배율: 10대 1.5 · 20대 1.0 · 30대 0.6 · 40~44 0.3 · 45 이상 0(멈춤).</summary>
        public static float RateForAge(int age) =>
            age < 20 ? 1.5f : age < 30 ? 1f : age < 40 ? 0.6f : age < DeclineStartAge ? 0.3f : 0f;

        public static string RateLabel(int age) =>
            age < 20 ? "성장 빠름" : age < 30 ? "성장 보통" : age < 40 ? "성장 느림" : age < DeclineStartAge ? "성장 아주 느림" : "성장 멈춤";

        /// <summary>고용비 나이 배율: 잘 자라는 10대는 비싸고 나이 든 용병은 싸다.</summary>
        public static float PriceMultiplier(int age) =>
            age < 20 ? 1.2f : age < 30 ? 1f : age < 40 ? 0.9f : 0.75f;

        /// <summary>시장에 나올 때 나이로 이미 쌓은 성장 횟수.</summary>
        public static int StartGrowthForAge(int age) => (int)Math.Round(Math.Max(0, age - MinHireAge) * StartGrowthPerYear);

        /// <summary>고용 나이: 16~19세 20% · 20~29세 45% · 30~39세 25% · 40~45세 10%.</summary>
        public static int RollHireAge(Random rng)
        {
            double r = rng.NextDouble();
            if (r < 0.20) return rng.Next(16, 20);
            if (r < 0.65) return rng.Next(20, 30);
            if (r < 0.90) return rng.Next(30, 40);
            return rng.Next(40, MaxHireAge + 1);
        }

        /// <summary>파견 전투에서 살아남았을 때 성장량(나이 배율 전).</summary>
        public static float BattleGrowth(int questGrade, bool victory)
        {
            float growth = VictoryGrowth + VictoryGrowthPerQuestGrade * Math.Max(0, questGrade);
            return victory ? growth : growth * DefeatGrowthRatio;
        }
    }
}
