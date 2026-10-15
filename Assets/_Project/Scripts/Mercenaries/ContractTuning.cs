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
        public const float CounterThreshold = 0.85f;   // 만족도가 이 이상이면 역제안(인내심 −1)
        public const float LowballBelow = 0.7f;        // 이 미만은 터무니없는 제안(인내심 −2), 사이는 "모자람"(인내심 −1)
        public const int BasePatience = 3;
        public const float AgentHintSpread = 0.15f;    // 에이전트 귀띔 범위(실제 요구 ±)

        // ---------- 성과 보너스 ----------
        public const int BonusPaidMorale = 3;
        public const int BonusUnpaidMorale = 10;
    }
}
