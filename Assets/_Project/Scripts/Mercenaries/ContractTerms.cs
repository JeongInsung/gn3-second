using GN3.Economy;
using GN3.Traits;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>용병이 길드에 얼마나 오고 싶어 하는지.</summary>
    public enum Interest { Keen, Neutral, Reluctant, NotInterested }

    /// <summary>계약 제안(주급·기간·계약금·성과 보너스).</summary>
    public struct ContractOffer
    {
        public int WeeklyWage;
        public int LengthDays;
        public int SigningFee;
        public int WinBonus;
    }

    /// <summary>용병이 처음 부르는 조건과 속마음(숨은 값). 협상 창은 귀띔 범위·기분·대답으로만 보여 준다.</summary>
    public struct ContractDemand
    {
        public int WeeklyWage;       // 처음 부르는 주급(만족도 1)
        public int SigningFee;
        public float Floor;          // 속마음 마지노선(만족도 비율) — 이 아래로는 절대 서명하지 않는다
        public NegotiationStyle Style;
        public Personality Personality;
        public Interest Interest;
        public int LengthPreference; // +1 긴 계약을 좋아함, −1 짧은 계약을 좋아함, 0 상관없음
        public float BonusValue;     // 성과 보너스 1G를 주급 몇 G로 쳐주는지
    }

    /// <summary>
    /// 용병의 요구 조건·관심도와 제안 평가(풋볼매니저식 계약 협상). 수치는 ContractTuning.
    ///  - 요구 주급 = 고용비 × 20% × 성격 욕심 × 관심도 보정, 요구 계약금 = 고용비(재계약은 25%) × 같은 보정.
    ///  - 관심도: 길드 상한 등급보다 약한 용병일수록, 지금 등급 안 명성이 높을수록 오고 싶어 한다. 재계약은 사기로 정한다.
    ///  - 평가: 계약 기간 전체 가치(주급 × 주 수 + 계약금 + 보너스 × 가치 × 주 수)를 요구 가치와 비교한다.
    ///  - 요구는 처음 부르는 값일 뿐, 성향(NegotiationStyle)마다 그 아래 숨은 마지노선이 있다. 밀고 당기기는 Negotiation.
    /// 같은 용병은 같은 날 늘 같은 요구를 한다(용병 id로 정한 seed).
    /// </summary>
    public static class ContractTerms
    {
        public static ContractDemand For(Mercenary merc, bool renewal)
        {
            var rng = new System.Random(Seed(merc.Id));
            var p = merc.Personality;
            float greed = Greedy(p) ? ContractTuning.GreedyMultiplier : p == Personality.Lethargic ? ContractTuning.ModestMultiplier : 1f;
            greed *= 1f + ((float)rng.NextDouble() * 2f - 1f) * ContractTuning.GreedJitter;

            var interest = renewal ? RenewalInterest(merc) : MarketInterest(merc, rng);
            float mood = interest switch
            {
                Interest.Keen => ContractTuning.KeenMultiplier,
                Interest.Reluctant => ContractTuning.ReluctantMultiplier,
                _ => 1f,
            };
            if (renewal && merc.Morale < ConditionTuning.LowMorale) mood *= ContractTuning.LowMoraleRenewalMultiplier;

            int cost = Pricing.HireCost(merc);
            var style = NegotiationStyles.For(p);
            float floor = NegotiationStyles.Get(style).Floor + ((float)rng.NextDouble() * 2f - 1f) * ContractTuning.FloorJitter;

            return new ContractDemand
            {
                WeeklyWage = Mathf.Max(1, Mathf.RoundToInt(cost * ContractTuning.WageRatio * greed * mood)),
                SigningFee = Mathf.Max(0, Mathf.RoundToInt(cost * (renewal ? ContractTuning.RenewalFeeRatio : 1f) * greed * mood)),
                Floor = Mathf.Min(1f, floor),
                Style = style,
                Personality = p,
                Interest = interest,
                LengthPreference = merc.Age >= 30 || p == Personality.Cautious || p == Personality.Pessimistic ? 1
                    : merc.Age <= 22 && merc.Talent >= MercenaryGrade.C ? -1 : 0,
                BonusValue = p == Personality.Brave || p == Personality.Confident || p == Personality.Hotblooded
                    ? ContractTuning.HighBonusValue : ContractTuning.LowBonusValue,
            };
        }

        private static bool Greedy(Personality p) => p == Personality.Confident || p == Personality.Hotblooded || p == Personality.Reckless;

        /// <summary>시장 용병의 관심도: 길드 상한 등급과의 차이·명성·성격·운.</summary>
        private static Interest MarketInterest(Mercenary merc, System.Random rng)
        {
            int gap = (int)Guild.Current.MaxMercGrade - (int)merc.Grade;
            float score = ContractTuning.InterestBase + ContractTuning.InterestPerGradeGap * gap
                          + ContractTuning.InterestReputation * ReputationProgress()
                          + PersonalityInterest(merc.Personality)
                          + rng.Next(-ContractTuning.InterestJitter, ContractTuning.InterestJitter + 1);
            return score >= ContractTuning.KeenAt ? Interest.Keen
                : score >= ContractTuning.NeutralAt ? Interest.Neutral
                : score >= ContractTuning.ReluctantAt ? Interest.Reluctant
                : Interest.NotInterested;
        }

        /// <summary>재계약은 지금 사기로: 즐거우면 남고 싶어 하고, 사기가 바닥이면 협상도 안 한다.</summary>
        private static Interest RenewalInterest(Mercenary merc) =>
            merc.Morale >= 70 ? Interest.Keen
            : merc.Morale >= ConditionTuning.LowMorale ? Interest.Neutral
            : merc.Morale > 0 ? Interest.Reluctant
            : Interest.NotInterested;

        private static int PersonalityInterest(Personality p) =>
            Greedy(p) ? -10 : p == Personality.Cheerful || p == Personality.Steady ? 10 : 0;

        /// <summary>지금 길드 등급 안에서 다음 등급까지 명성이 얼마나 찼는지 0~1(최고 등급이면 1).</summary>
        public static float ReputationProgress()
        {
            if (Guild.IsMaxRank) return 1f;
            int from = Guild.Current.Required, to = Guild.Ranks[Guild.Rank + 1].Required;
            return Mathf.Clamp01((Guild.Reputation - from) / (float)Mathf.Max(1, to - from));
        }

        // ---------- 평가 ----------

        /// <summary>제안 가치 ÷ 요구 가치. 1 이상이면 받아들인다.</summary>
        public static float Satisfaction(ContractDemand demand, ContractOffer offer)
        {
            float weeks = offer.LengthDays / 7f;
            float offered = offer.WeeklyWage * weeks + offer.SigningFee + offer.WinBonus * demand.BonusValue * ContractTuning.QuestsPerWeek * weeks;
            float wanted = demand.WeeklyWage * weeks * LengthFactor(demand, offer.LengthDays) + demand.SigningFee;
            return wanted <= 0f ? 2f : offered / wanted;
        }

        /// <summary>원하는 기간이면 요구가 줄고, 싫은 기간이면 는다.</summary>
        private static float LengthFactor(ContractDemand demand, int days)
        {
            if (demand.LengthPreference == 0) return 1f;
            bool longContract = days >= ContractTuning.LongContractDays;
            bool liked = (demand.LengthPreference > 0) == longContract;
            return liked ? 1f - ContractTuning.LengthPreference : 1f + ContractTuning.LengthPreference;
        }

        /// <summary>역제안: 기간·계약금·보너스는 그대로 두고 만족도가 target(지금 부르는 값)에 닿도록 주급을 올린 조건.</summary>
        public static ContractOffer Counter(ContractDemand demand, ContractOffer offer, float target = 1f)
        {
            float weeks = offer.LengthDays / 7f;
            float wanted = (demand.WeeklyWage * weeks * LengthFactor(demand, offer.LengthDays) + demand.SigningFee) * target;
            float rest = offer.SigningFee + offer.WinBonus * demand.BonusValue * ContractTuning.QuestsPerWeek * weeks;
            var counter = offer;
            counter.WeeklyWage = Mathf.Max(offer.WeeklyWage + 1, Mathf.CeilToInt((wanted - rest) / Mathf.Max(0.1f, weeks)));
            return counter;
        }

        /// <summary>요구에 딱 맞춘 첫 제안의 기준값(협상 창의 시작 값은 이보다 조금 낮게).</summary>
        public static ContractOffer Fair(ContractDemand demand) => new ContractOffer
        {
            WeeklyWage = demand.WeeklyWage,
            LengthDays = demand.LengthPreference > 0 ? ContractTuning.PreferredLongDays : demand.LengthPreference < 0 ? ContractTuning.ShortContractDays : ContractTuning.DefaultLengthDays,
            SigningFee = demand.SigningFee,
            WinBonus = 0,
        };

        public static string InterestLabel(Interest interest) => interest switch
        {
            Interest.Keen => "관심 많음",
            Interest.Neutral => "보통",
            Interest.Reluctant => "까다로움",
            _ => "관심 없음",
        };

        public static Color InterestColor(Interest interest) => interest switch
        {
            Interest.Keen => new Color(0.55f, 0.85f, 0.45f),
            Interest.Neutral => new Color(0.85f, 0.8f, 0.6f),
            Interest.Reluctant => new Color(0.95f, 0.6f, 0.3f),
            _ => new Color(0.6f, 0.55f, 0.5f),
        };

        /// <summary>id 문자열 → 안정된 seed(플랫폼·실행마다 같다). 날짜를 섞어 같은 용병도 날이 바뀌면 조금 달라진다.</summary>
        public static int Seed(string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? "") hash = (hash ^ c) * 16777619;
                hash = (hash ^ (uint)GN3.World.GameClock.CurrentDay) * 16777619;
                return (int)hash;
            }
        }
    }
}
