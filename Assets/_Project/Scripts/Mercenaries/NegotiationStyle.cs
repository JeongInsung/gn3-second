using GN3.Traits;

namespace GN3.Mercenaries
{
    /// <summary>성격이 정하는 협상 버릇. 마지노선·양보 속도·허세·최종 제안/자리 뜨기에 대한 반응이 다르다.</summary>
    public enum NegotiationStyle { Stubborn, Volatile, Timid, Calculating, Straight, Easygoing }

    /// <summary>성향 하나의 협상 수치. 비율은 모두 ContractTerms.Satisfaction(요구 = 1) 기준.</summary>
    public readonly struct NegotiationProfile
    {
        public readonly string Label, Hint;
        public readonly float Floor;        // 속마음 마지노선(이 이상이면 받을 수는 있다)
        public readonly float Concession;   // 역제안 한 번에 부르는 값을 마지노선 쪽으로 내리는 비율(계산형은 맞불이라 기본값)
        public readonly float InsultMargin; // 마지노선보다 이만큼 더 낮으면 모욕
        public readonly int InsultMood;     // 모욕당했을 때 잃는 기분
        public readonly int Fatigue;        // 역제안이 오갈 때마다 잃는 기분
        public readonly float FinalAccept;  // 마지노선 이상의 최종 제안에 굴복할 확률
        public readonly float CallBack;     // 자리를 뜨면 붙잡을 확률
        public readonly float Bluff;        // 첫 역제안 뒤 "다른 길드 제의" 허세를 부릴 확률
        public readonly bool BluffTell;     // 거짓말이 서툴러 가짜 허세에 티가 난다

        public NegotiationProfile(string label, string hint, float floor, float concession, float insultMargin, int insultMood, int fatigue,
            float finalAccept, float callBack, float bluff, bool bluffTell)
        {
            Label = label;
            Hint = hint;
            Floor = floor;
            Concession = concession;
            InsultMargin = insultMargin;
            InsultMood = insultMood;
            Fatigue = fatigue;
            FinalAccept = finalAccept;
            CallBack = callBack;
            Bluff = bluff;
            BluffTell = bluffTell;
        }
    }

    public static class NegotiationStyles
    {
        public static NegotiationStyle For(Personality p) => p switch
        {
            Personality.Confident => NegotiationStyle.Stubborn,
            Personality.Hotblooded or Personality.Reckless => NegotiationStyle.Volatile,
            Personality.Coward or Personality.Nervous or Personality.Pessimistic => NegotiationStyle.Timid,
            Personality.Calm or Personality.Cautious or Personality.Composed => NegotiationStyle.Calculating,
            Personality.Brave or Personality.Steady => NegotiationStyle.Straight,
            _ => NegotiationStyle.Easygoing, // Cheerful, Lethargic
        };

        public static NegotiationProfile Get(NegotiationStyle style) => style switch
        {
            NegotiationStyle.Stubborn => new NegotiationProfile("고집형", "쉽게 물러서지 않고 허세를 자주 부린다. 치켜세우면 누그러진다",
                0.92f, 0.25f, 0.12f, 20, 4, 0.40f, 0.15f, 0.50f, false),
            NegotiationStyle.Volatile => new NegotiationProfile("다혈질형", "화도 빨리 내고 마음도 빨리 바뀐다. 무시당하면 바로 판을 엎는다",
                0.85f, 0.35f, 0.08f, 30, 6, 0.55f, 0.30f, 0.30f, false),
            NegotiationStyle.Timid => new NegotiationProfile("소심형", "압박에 약하고 결렬을 두려워한다. 안정적인 조건을 좋아한다",
                0.78f, 0.50f, 0.25f, 8, 4, 1.00f, 0.85f, 0.00f, false),
            NegotiationStyle.Calculating => new NegotiationProfile("계산형", "말보다 숫자. 이쪽이 올린 만큼만 내려온다",
                0.85f, 0.30f, 0.15f, 15, 4, 1.00f, 0.50f, 0.35f, false),
            NegotiationStyle.Straight => new NegotiationProfile("솔직형", "처음 부른 값과 속마음이 거의 같다. 거짓말이 서툴다",
                0.95f, 0.20f, 0.12f, 15, 4, 0.80f, 0.40f, 0.10f, true),
            _ => new NegotiationProfile("느긋형", "여유가 많고 잘 내려온다. 거짓말이 서툴다",
                0.80f, 0.40f, 0.20f, 12, 2, 0.90f, 0.60f, 0.10f, true),
        };
    }
}
