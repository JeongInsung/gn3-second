using GN3.Traits;

namespace GN3.Mercenaries
{
    /// <summary>계약·협상 수치를 한곳에 모은다(Contracts·ContractTerms·협상 창이 쓴다).</summary>
    public static class ContractTuning
    {
        // ---------- 계약 ----------
        public static readonly int[] LengthOptions = { 180, 360, 720, 1080 }; // 협상 창에서 고르는 계약 기간: 반년·1년·2년·3년(1년 = 360일)
        public const int DefaultLengthDays = 360;      // 기본 계약(예전 저장·재계약 시작값·기간 선호 없음). LengthOptions 안의 값이어야 한다
        public const int LongContractDays = 720;       // 이 이상이면 "긴 계약"
        public const int ShortContractDays = 180, PreferredLongDays = 720; // 짧은/긴 계약을 좋아하는 용병의 첫 제안 기간

        /// <summary>계약 기간 표시: 180 → "반년", 360의 배수 → "N년", 그 밖(예전 저장)은 "N일".</summary>
        public static string LengthLabel(int days) =>
            days == GN3.World.GameCalendar.DaysPerYear / 2 ? "반년"
            : days % GN3.World.GameCalendar.DaysPerYear == 0 ? $"{days / GN3.World.GameCalendar.DaysPerYear}년"
            : $"{days}일";
        public const float ReleaseRatio = 0.5f;        // 해고 위약금 = 남은 주급 × 이 비율
        public const int NoticeDays = 14;              // 만료 이만큼 전에 우편함 알림
        public const int UrgentNoticeDays = 3;         // 만료 이만큼 전엔 중요 알림

        // ---------- 요구 ----------
        public const float WageRatio = ConditionTuning.WageRatio; // 요구 주급 = 고용비 × 이 비율(예전 자동 주급과 같다)
        public const float RenewalFeeRatio = 0.25f;    // 재계약 계약금 = 고용비 × 이 비율
        public const float GreedyMultiplier = 1.15f;   // 자신만만·다혈질·저돌적
        public const float ModestMultiplier = 0.9f;    // 무기력
        public const float GreedJitter = 0.05f;        // 같은 성격이라도 사람마다 ±
        public const float LengthPreference = 0.08f;   // 원하는 기간이면 요구 −, 싫은 기간이면 +
        public const float HighBonusValue = 1.0f;      // 용맹·자신만만·다혈질: 성과 보너스를 주급처럼 친다
        public const float LowBonusValue = 0.6f;       // 그 외: 보너스는 덜 쳐준다
        public const float QuestsPerWeek = 1f;         // 보너스 가치 계산용 예상 퀘스트 성공 수

        // ---------- 관심도 ----------
        public const int InterestBase = 50;
        public const int InterestPerGradeGap = 15;     // 길드 상한 등급보다 한 등급 낮을 때마다
        public const int InterestReputation = 20;      // 지금 길드 등급 안에서 명성이 다 찼을 때
        public const int InterestJitter = 15;
        public const int KeenAt = 70, NeutralAt = 40, ReluctantAt = 20;
        public const float KeenMultiplier = 0.9f;
        public const float ReluctantMultiplier = 1.15f;
        public const float LowMoraleRenewalMultiplier = 1.2f; // 재계약 때 사기가 낮으면

        // ---------- 협상 ----------
        public const float AgentHintSpread = 0.15f;    // 에이전트 귀띔 범위(처음 부르는 값 ±)
        public const float FloorJitter = 0.03f;        // 같은 성향이라도 마지노선이 사람마다 ±
        public const int KeenMood = 70, NeutralMood = 55, ReluctantMood = 40; // 협상 시작 기분(0이 되면 결렬)
        public const int StallMood = 10;               // 직전 제안보다 올리지 않고 버티면
        public const int RaiseMood = 3;                // 의미 있게 올려 주면
        public const float MeaningfulRaise = 0.02f;    // 만족도가 이만큼 오르면 "올려 줬다"
        public const float CloseEnough = 0.01f;        // 부르는 값과 이만큼 가까워지면 그냥 받아들인다
        public const float CalculatingRaiseScale = 4f; // 계산형 양보율 = 플레이어가 올린 만족도 × 이 값
        public const float CalculatingMin = 0.1f, CalculatingMax = 0.6f;
        public const int BluffCalledMood = 25;         // 최종 제안 허세를 들켰을 때(다혈질형은 결렬)
        public const float TimidCaveMargin = 0.05f;    // 소심형은 마지노선보다 이만큼 낮은 최종 제안에도
        public const float TimidCaveChance = 0.5f;     //   이 확률로 굴복한다
        public const int WalkAwayMood = 10;            // 자리를 떴다 붙잡혔을 때
        public const float RivalRealChance = 0.4f;     // 용병의 "다른 길드 제의"가 진짜일 확률
        public const float RivalWageMin = 1.1f, RivalWageMax = 1.25f; // 주장하는 다른 길드 주급 = 역제안 주급 × 이 범위
        public const int RivalDeadline = 2;            // 진짜 제의면 이만큼 라운드 안에 합의해야 한다
        public const int RivalExposedMood = 5;         // 가짜를 들켰을 때(값은 크게 내려온다)
        public const int RivalDoubtedMood = 20;        // 진짜인데 의심했을 때(기한도 1로 준다)
        public const float TalkFloorDrop = 0.03f;      // 잘 먹힌 화법은 마지노선도 이만큼 낮춘다
        public const float PraiseFailMin = 0.05f, PraiseFailMax = 0.6f; // 기분 보정 뒤 아부가 들통날 확률 범위

        /// <summary>실력 칭찬이 아부로 들통날 기본 확률(기분 보정 전)과 들통났을 때 잃는 기분 범위(최소~최대, 랜덤).</summary>
        public static (float chance, int moodMin, int moodMax) PraiseFail(Personality p) => p switch
        {
            Personality.Calm => (0.40f, 8, 14),         // 침착: 속을 잘 꿰뚫어 본다
            Personality.Composed => (0.35f, 6, 12),
            Personality.Cautious => (0.35f, 8, 14),     // 신중: 의심이 많다
            Personality.Pessimistic => (0.30f, 10, 18), // 비관: 놀림으로 받아들인다
            Personality.Confident => (0.10f, 5, 10),    // 자신만만: 칭찬은 당연하다
            Personality.Hotblooded => (0.25f, 12, 25),  // 열혈: 들통나면 크게 화낸다
            Personality.Reckless => (0.20f, 10, 22),
            Personality.Brave => (0.20f, 6, 12),
            Personality.Steady => (0.25f, 5, 10),
            Personality.Cheerful => (0.10f, 3, 8),      // 쾌활: 잘 넘어가고 금방 잊는다
            Personality.Lethargic => (0.15f, 2, 6),     // 무기력: 별 반응이 없다
            Personality.Coward => (0.15f, 8, 15),
            Personality.Nervous => (0.20f, 10, 16),     // 예민: 쉽게 상처받는다
            _ => (0.20f, 5, 12),
        };
        public const int SignedHappyMood = 70, SignedGrudgeMood = 25, SignedMorale = 3; // 서명 때 기분에 따라 사기 ±

        // ---------- 성과 보너스 ----------
        public const int BonusPaidMorale = 3;
        public const int BonusUnpaidMorale = 10;
    }
}
