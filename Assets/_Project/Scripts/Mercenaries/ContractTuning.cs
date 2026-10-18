namespace GN3.Mercenaries
{
    /// <summary>계약·협상 수치를 한곳에 모은다(Contracts·ContractTerms·협상 창이 쓴다).</summary>
    public static class ContractTuning
    {
        // ---------- 계약 ----------
        public static readonly int[] LengthOptions = { 30, 60, 90, 180 }; // 협상 창에서 고르는 계약 기간(일)
        public const int DefaultLengthDays = 60;       // 예전 저장을 불러왔을 때 주는 기본 계약
        public const int LongContractDays = 90;        // 이 이상이면 "긴 계약"
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
        public const int SignedHappyMood = 70, SignedGrudgeMood = 25, SignedMorale = 3; // 서명 때 기분에 따라 사기 ±

        // ---------- 성과 보너스 ----------
        public const int BonusPaidMorale = 3;
        public const int BonusUnpaidMorale = 10;
    }
}
