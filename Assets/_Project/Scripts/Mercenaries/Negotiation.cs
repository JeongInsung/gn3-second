using System.Collections.Generic;
using UnityEngine;

namespace GN3.Mercenaries
{
    public enum TalkTopic { Praise, Vision, Stability }

    /// <summary>협상 한 수의 결과. 협상 창은 이것과 성향으로 대사를 고른다.</summary>
    public enum NegotiationEvent
    {
        Accepted,      // 부르는 값 이상이라 받아들였다
        ClosedGap,     // 양보하다 보니 차이가 거의 없어 그냥 받아들였다
        Countered,     // 조금 내려온 값으로 역제안(Counter)
        Insulted,      // 마지노선보다 한참 낮아 기분이 상했다
        FinalAccepted, // 최종 제안에 굴복
        FinalCalled,   // 최종 제안이 허세라고 보고 버텼다(다음 라운드 양보 없음)
        FinalWalkout,  // 최종 제안을 거절하고 떠났다(결렬)
        CalledBack,    // 자리를 뜨려 하자 붙잡으며 값을 내렸다
        LetGo,         // 자리를 뜨게 그냥 뒀다(결렬)
        RivalExposed,  // "다른 길드 제의"가 거짓말로 드러났다
        RivalDoubted,  // 진짜 제의였는데 의심받아 언짢다(기한 1라운드)
        RivalNoted,    // 제의 이야기를 그냥 들었다
        RivalLeft,     // 기한 안에 합의하지 못해 다른 길드로 갔다(결렬)
        TalkLiked, TalkFlat, TalkDisliked,
        PraiseBackfired, // 실력 칭찬이 아부로 들통나 기분이 상했다
        MoodGone,      // 기분이 바닥나 판을 접었다(결렬)
    }

    /// <summary>
    /// 밀고 당기는 계약 협상 한 판의 상태와 규칙(UI 없음). 수치는 NegotiationStyles·ContractTuning.
    ///  - 용병은 요구(만족도 1)를 부르고 시작해 역제안마다 숨은 마지노선(Floor) 쪽으로 내려온다. 내려오는 폭은 성향·기분이 정한다.
    ///  - 기분(0~100)이 바닥나면 결렬. 버티면 깎이고, 올려 주면 조금 오른다.
    ///  - 플레이어 수: 최종 제안(1회)·자리 뜨기(1회)·화법 3종(각 1회). 용병 수: 첫 역제안 뒤 "다른 길드 제의" 허세(진짜일 수도 있다).
    ///  - 굴림은 용병·날짜 seed와 몇 번째 수인지로 정해져 같은 상황은 늘 같은 결과다.
    /// </summary>
    public class Negotiation
    {
        public readonly ContractDemand Demand;
        public readonly NegotiationProfile Profile;
        public float Ask { get; private set; } = 1f;
        public float Floor { get; private set; }
        public int Mood { get; private set; }
        public int StartMood { get; }
        public int Rounds { get; private set; }
        public bool Broken { get; private set; }
        public bool FinalUsed { get; private set; }
        public bool WalkedAway { get; private set; }
        public ContractOffer? Counter { get; private set; }

        // 다른 길드 제의(용병의 허세)
        public bool RivalPending { get; private set; } // 말을 꺼냈고 대답을 기다린다
        public bool RivalReal { get; private set; }
        public int RivalWage { get; private set; }
        public int RivalDeadline { get; private set; }  // 0이면 기한 없음

        private readonly HashSet<TalkTopic> _talked = new HashSet<TalkTopic>();
        private readonly int _seed;
        private int _steps;
        private bool _rivalRolled;
        private bool _stonewall;      // 최종 제안 허세를 들킨 뒤 한 라운드는 양보하지 않는다
        private float _lastSatisfaction = -1f;
        private ContractOffer _lastOffer;

        public Negotiation(ContractDemand demand, int seed)
        {
            Demand = demand;
            Profile = NegotiationStyles.Get(demand.Style);
            Floor = demand.Floor;
            _seed = seed;
            StartMood = Mood = demand.Interest switch
            {
                Interest.Keen => ContractTuning.KeenMood,
                Interest.Reluctant => ContractTuning.ReluctantMood,
                _ => ContractTuning.NeutralMood,
            };
        }

        public bool HasTalked(TalkTopic topic) => _talked.Contains(topic);
        public bool CanWalkAway => !Broken && !WalkedAway && Rounds > 0;
        public float Satisfaction(ContractOffer offer) => ContractTerms.Satisfaction(Demand, offer);

        // ---------- 제안 ----------

        public NegotiationEvent Propose(ContractOffer offer)
        {
            float s = Satisfaction(offer);
            Rounds++;
            if (s >= Ask - ContractTuning.CloseEnough) return Accept();

            if (s < Floor - Profile.InsultMargin)
            {
                AddMood(-Profile.InsultMood);
                Counter = null;
                Remember(offer, s);
                return TickRival() ?? (Mood <= 0 ? Break(NegotiationEvent.MoodGone) : NegotiationEvent.Insulted);
            }

            AddMood(-Profile.Fatigue);
            if (_lastSatisfaction >= 0f)
            {
                if (s <= _lastSatisfaction + 0.005f) AddMood(-ContractTuning.StallMood);
                else if (s - _lastSatisfaction >= ContractTuning.MeaningfulRaise) AddMood(ContractTuning.RaiseMood);
            }
            if (_stonewall) _stonewall = false;
            else Ask = Mathf.Max(Floor, Ask - (Ask - Mathf.Max(s, Floor)) * ConcessionRate(s) * MoodFactor);
            Remember(offer, s);
            if (Mood <= 0) return Break(NegotiationEvent.MoodGone);
            if (Ask - s <= ContractTuning.CloseEnough) return Accept(NegotiationEvent.ClosedGap);

            Counter = ContractTerms.Counter(Demand, offer, Ask);
            var left = TickRival();
            if (left != null) return left.Value;
            MaybeClaimRival();
            return NegotiationEvent.Countered;
        }

        /// <summary>"이게 마지막입니다." 마지노선 이상이면 성향 확률로 굴복, 아래면 대개 결렬.</summary>
        public NegotiationEvent FinalOffer(ContractOffer offer)
        {
            FinalUsed = true;
            float s = Satisfaction(offer);
            Rounds++;
            Remember(offer, s);
            if (s >= Ask - ContractTuning.CloseEnough) return Accept(NegotiationEvent.FinalAccepted);
            float chance = Profile.FinalAccept + MoodBonus;
            if (s >= Floor)
            {
                if (Roll() < chance) return Accept(NegotiationEvent.FinalAccepted);
                if (Demand.Style == NegotiationStyle.Volatile) return Break(NegotiationEvent.FinalWalkout);
                AddMood(-ContractTuning.BluffCalledMood);
                _stonewall = true;
                return Mood <= 0 ? Break(NegotiationEvent.MoodGone) : NegotiationEvent.FinalCalled;
            }
            if (Demand.Style == NegotiationStyle.Timid && s >= Floor - ContractTuning.TimidCaveMargin && Roll() < ContractTuning.TimidCaveChance)
                return Accept(NegotiationEvent.FinalAccepted);
            return Break(NegotiationEvent.FinalWalkout);
        }

        /// <summary>일어서는 척. 붙잡으면 부르는 값이 지난 제안 쪽으로 반쯤 내려온다.</summary>
        public NegotiationEvent WalkAway()
        {
            WalkedAway = true;
            if (Roll() >= Profile.CallBack + MoodBonus) return Break(NegotiationEvent.LetGo);
            Ask = Mathf.Max(Floor, (Ask + Mathf.Max(Floor, _lastSatisfaction)) / 2f);
            Mood = Mathf.Max(1, Mood - ContractTuning.WalkAwayMood);
            Counter = ContractTerms.Counter(Demand, _lastOffer, Ask);
            return NegotiationEvent.CalledBack;
        }

        // ---------- 다른 길드 제의 ----------

        /// <summary>"증거를 대 보시죠." 거짓말이면 값이 크게 내려오고, 진짜면 기분이 상하고 기한이 준다.</summary>
        public NegotiationEvent DoubtRival()
        {
            RivalPending = false;
            if (!RivalReal)
            {
                Ask = Mathf.Max(Floor, Ask - (Ask - Floor) * 0.5f);
                AddMood(-ContractTuning.RivalExposedMood);
                Counter = ContractTerms.Counter(Demand, _lastOffer, Ask);
                return Mood <= 0 ? Break(NegotiationEvent.MoodGone) : NegotiationEvent.RivalExposed;
            }
            AddMood(-ContractTuning.RivalDoubtedMood);
            RivalDeadline = 1;
            return Mood <= 0 ? Break(NegotiationEvent.MoodGone) : NegotiationEvent.RivalDoubted;
        }

        public NegotiationEvent AcknowledgeRival()
        {
            RivalPending = false;
            if (RivalReal) RivalDeadline = ContractTuning.RivalDeadline;
            return NegotiationEvent.RivalNoted;
        }

        // ---------- 화법 ----------

        public NegotiationEvent Talk(TalkTopic topic)
        {
            _talked.Add(topic);
            if (topic == TalkTopic.Praise && Roll() < PraiseFailChance)
            {
                var (_, min, max) = ContractTuning.PraiseFail(Demand.Personality);
                AddMood(-(min + (int)(Roll() * (max - min + 1))));
                return Mood <= 0 ? Break(NegotiationEvent.MoodGone) : NegotiationEvent.PraiseBackfired;
            }
            var (mood, softens) = TalkEffect(topic);
            AddMood(mood);
            if (softens) Floor = Mathf.Max(0f, Floor - ContractTuning.TalkFloorDrop);
            if (Mood <= 0) return Break(NegotiationEvent.MoodGone);
            return mood > 0 ? NegotiationEvent.TalkLiked : mood < 0 ? NegotiationEvent.TalkDisliked : NegotiationEvent.TalkFlat;
        }

        /// <summary>성향별로 먹히는 말이 다르다. (기분 변화, 마지노선을 낮추는지)</summary>
        private (int mood, bool softens) TalkEffect(TalkTopic topic)
        {
            var style = Demand.Style;
            bool famous = ContractTerms.ReputationProgress() >= 0.5f;
            return topic switch
            {
                TalkTopic.Praise => style switch
                {
                    NegotiationStyle.Stubborn or NegotiationStyle.Volatile => (15, true),
                    NegotiationStyle.Easygoing => (10, false),
                    NegotiationStyle.Calculating => (0, false),
                    _ => (5, false),
                },
                TalkTopic.Vision => style switch
                {
                    NegotiationStyle.Straight or NegotiationStyle.Easygoing => famous ? (10, true) : (5, false),
                    NegotiationStyle.Calculating or NegotiationStyle.Stubborn => famous ? (5, false) : (-5, false),
                    _ => (5, false),
                },
                _ => style switch
                {
                    NegotiationStyle.Timid => (15, true),
                    NegotiationStyle.Volatile => (-10, false),
                    NegotiationStyle.Stubborn => (0, false),
                    _ => (5, false),
                },
            };
        }

        // ---------- 안쪽 ----------

        private float MoodFactor => 0.5f + Mood / 100f;
        private float MoodBonus => (Mood - 50) / 200f;

        /// <summary>성격마다 아부를 알아채는 정도가 다르다(ContractTuning.PraiseFail). 기분이 나쁠수록 잘 들통난다.</summary>
        private float PraiseFailChance => Mathf.Clamp(ContractTuning.PraiseFail(Demand.Personality).chance - MoodBonus,
            ContractTuning.PraiseFailMin, ContractTuning.PraiseFailMax);

        /// <summary>계산형은 맞불: 이쪽이 올린 만큼만 내려온다. 나머지는 성향 고정 비율.</summary>
        private float ConcessionRate(float s)
        {
            if (Demand.Style != NegotiationStyle.Calculating || _lastSatisfaction < 0f) return Profile.Concession;
            return Mathf.Clamp((s - _lastSatisfaction) * ContractTuning.CalculatingRaiseScale, ContractTuning.CalculatingMin, ContractTuning.CalculatingMax);
        }

        private void MaybeClaimRival()
        {
            if (_rivalRolled || Counter == null) return;
            _rivalRolled = true;
            if (Roll() >= Profile.Bluff) return;
            RivalPending = true;
            RivalReal = Roll() < ContractTuning.RivalRealChance;
            float lift = Mathf.Lerp(ContractTuning.RivalWageMin, ContractTuning.RivalWageMax, (float)Roll());
            RivalWage = Mathf.CeilToInt(Counter.Value.WeeklyWage * lift);
        }

        /// <summary>진짜 다른 길드 제의의 기한을 한 라운드 줄이고, 다 되면 떠난다.</summary>
        private NegotiationEvent? TickRival()
        {
            if (RivalDeadline <= 0) return null;
            RivalDeadline--;
            return RivalDeadline == 0 ? Break(NegotiationEvent.RivalLeft) : (NegotiationEvent?)null;
        }

        private void Remember(ContractOffer offer, float s)
        {
            _lastOffer = offer;
            _lastSatisfaction = s;
        }

        private NegotiationEvent Accept(NegotiationEvent e = NegotiationEvent.Accepted)
        {
            Counter = null;
            RivalPending = false;
            RivalDeadline = 0;
            return e;
        }

        private NegotiationEvent Break(NegotiationEvent e)
        {
            Broken = true;
            Counter = null;
            RivalPending = false;
            return e;
        }

        private void AddMood(int amount) => Mood = Mathf.Clamp(Mood + amount, 0, 100);

        private double Roll() => new System.Random(unchecked(_seed + 7919 * ++_steps)).NextDouble();
    }
}
