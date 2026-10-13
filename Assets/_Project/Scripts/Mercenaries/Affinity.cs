using System;
using System.Collections.Generic;
using System.Linq;
using GN3.Quests;
using GN3.Traits;
using GN3.UI;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>
    /// 용병 두 사람 사이의 친밀도(-100~100). 같이 파견을 다녀오거나 훈련소에서 같이 훈련하면 오르고,
    /// 성격이 안 맞는 사이는 파견에 실패하면 서로 탓하며 내려간다. 처음 값과 오르는 속도는 성격 궁합(Compatibility)으로 정해진다.
    /// 6단계(앙숙·서먹·낯섦·지인·친구·절친)마다 효과가 다르다(Tiers): 마을에서 같이 걷기·여관·훈련소 동행 확률
    /// (VillagePartyPresenter), 같이 싸울 때·잃었을 때 사기(MercenaryCondition). 관계 창은 RelationsPanel.
    /// </summary>
    public static class Affinity
    {
        public const int Min = -100;
        public const int Max = 100;

        public const int ExpeditionGain = 8;
        public const int VictoryBonus = 4;
        public const int DefeatGain = 4;     // 실패해도 같이 고생한 사이는 조금 가까워진다
        public const int DefeatBlame = -6;   // 성격이 안 맞으면 실패를 서로 탓한다
        public const float TrainingGainPerHour = 1.5f;
        public const float RestGainPerHour = 1.5f;     // 온천·도박장에 같이 있던 시간

        /// <summary>친밀도 단계 하나와 그 효과.</summary>
        public class Tier
        {
            public string Name;
            public int MinValue;          // 이 값 이상이면 이 단계
            public Color Color;
            public float FollowChance;    // 마을에서 같이 걷기 시작할 확률(한 번 굴릴 때)
            public float TogetherChance;  // 여관에서 같이 나오기·훈련소 같이 가기 확률
            public int BattleMorale;      // 같은 파견대로 싸웠을 때 사기
            public int MournMorale;       // 이 사람이 전사했을 때 사기(음수)
            public bool Avoids;           // 같은 무리에 끼지 않는다

            public string ColorHex => "#" + ColorUtility.ToHtmlStringRGB(Color);

            /// <summary>"같이 걷기 35% · 동행 45% · 같이 싸우면 사기 +2 · 잃으면 -10" 같은 효과 요약.</summary>
            public string EffectText
            {
                get
                {
                    var parts = new List<string>();
                    if (Avoids) parts.Add("같이 다니지 않음");
                    else if (FollowChance > 0f) parts.Add($"같이 걷기 {FollowChance * 100f:0}% · 동행 {TogetherChance * 100f:0}%");
                    else parts.Add("따로 다님");
                    if (BattleMorale != 0) parts.Add($"같이 싸우면 사기 {BattleMorale:+0;-0}");
                    if (MournMorale != 0) parts.Add($"잃으면 사기 {MournMorale}");
                    return string.Join(" · ", parts);
                }
            }
        }

        /// <summary>낮은 단계부터. 마지막 단계가 가장 친하다.</summary>
        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "앙숙", MinValue = Min, Color = new Color(0.88f, 0.30f, 0.26f), BattleMorale = -3, Avoids = true },
            new Tier { Name = "서먹", MinValue = -49, Color = new Color(0.92f, 0.58f, 0.25f), BattleMorale = -1, Avoids = true },
            new Tier { Name = "낯섦", MinValue = -14, Color = new Color(0.62f, 0.60f, 0.57f) },
            new Tier { Name = "지인", MinValue = 15, Color = new Color(0.55f, 0.82f, 0.45f), FollowChance = 0.15f, TogetherChance = 0.2f, BattleMorale = 1, MournMorale = -3 },
            new Tier { Name = "친구", MinValue = 45, Color = new Color(0.40f, 0.72f, 0.95f), FollowChance = 0.35f, TogetherChance = 0.45f, BattleMorale = 2, MournMorale = -10 },
            new Tier { Name = "절친", MinValue = 75, Color = new Color(0.96f, 0.76f, 0.30f), FollowChance = 0.6f, TogetherChance = 0.75f, BattleMorale = 3, MournMorale = -15 },
        };

        public static Tier Acquaintance => Tiers[3];

        /// <summary>단계 하나의 범위 글자("15 ~ 44", 맨 위는 "75 이상", 맨 아래는 "-50 이하").</summary>
        public static string RangeText(Tier tier)
        {
            int i = Array.IndexOf(Tiers, tier);
            if (i == Tiers.Length - 1) return $"{tier.MinValue} 이상";
            int upper = Tiers[i + 1].MinValue - 1;
            return i == 0 ? $"{upper} 이하" : $"{tier.MinValue} ~ {upper}";
        }

        private static readonly Dictionary<string, int> _values = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> _pending = new Dictionary<string, float>(); // 훈련으로 쌓인 소수점

        /// <summary>친밀도가 바뀌었을 때(관계 창 갱신용).</summary>
        public static event Action OnChanged;

        // 잘 맞는 쌍 / 안 맞는 쌍(순서 무관). 같은 성격끼리도 잘 맞는다.
        private static readonly (Personality, Personality)[] GoodPairs =
        {
            (Personality.Cheerful, Personality.Calm),
            (Personality.Brave, Personality.Confident),
            (Personality.Steady, Personality.Cautious),
            (Personality.Composed, Personality.Calm),
            (Personality.Hotblooded, Personality.Brave),
            (Personality.Reckless, Personality.Cheerful),
            (Personality.Nervous, Personality.Steady),
            (Personality.Lethargic, Personality.Calm),
        };

        private static readonly (Personality, Personality)[] BadPairs =
        {
            (Personality.Hotblooded, Personality.Composed),
            (Personality.Reckless, Personality.Cautious),
            (Personality.Pessimistic, Personality.Cheerful),
            (Personality.Coward, Personality.Reckless),
            (Personality.Lethargic, Personality.Hotblooded),
            (Personality.Nervous, Personality.Confident),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _values.Clear();
            _pending.Clear();
            OnChanged = null;
        }

        /// <summary>성격 궁합: +1 잘 맞음, 0 보통, -1 안 맞음.</summary>
        public static int Compatibility(Personality a, Personality b)
        {
            if (a == b) return 1;
            if (GoodPairs.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a))) return 1;
            if (BadPairs.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a))) return -1;
            return 0;
        }

        public static string CompatibilityLabel(int compat) => compat > 0 ? "잘 맞음" : compat < 0 ? "안 맞음" : "보통";

        private static int BaseValue(int compat) => compat > 0 ? 20 : compat < 0 ? -20 : 0;
        private static float GainMultiplier(int compat) => compat > 0 ? 1.5f : compat < 0 ? 0.5f : 1f;

        private static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

        public static int Get(Mercenary a, Mercenary b)
        {
            if (a == null || b == null || a.Id == b.Id) return 0;
            return _values.TryGetValue(Key(a.Id, b.Id), out int v) ? v : BaseValue(Compatibility(a.Personality, b.Personality));
        }

        public static Tier TierOf(int value)
        {
            for (int i = Tiers.Length - 1; i > 0; i--)
                if (value >= Tiers[i].MinValue) return Tiers[i];
            return Tiers[0];
        }

        public static Tier TierBetween(Mercenary a, Mercenary b) => TierOf(Get(a, b));

        public static string TierLabel(int value) => TierOf(value).Name;

        /// <summary>
        /// 친밀도를 바꾼다. 오를 때만 궁합 배율을 곱한다(내려갈 때는 그대로). 소수점은 쌍마다 쌓아 둔다. 바뀐 뒤 값을 돌려준다.
        /// </summary>
        public static int Add(Mercenary a, Mercenary b, float amount)
        {
            if (a == null || b == null || a.Id == b.Id) return 0;
            string key = Key(a.Id, b.Id);
            int current = Get(a, b);
            if (amount > 0f) amount *= GainMultiplier(Compatibility(a.Personality, b.Personality));
            _pending.TryGetValue(key, out float pending);
            pending += amount;
            int whole = (int)Math.Truncate(pending);
            _pending[key] = pending - whole;
            int next = Math.Clamp(current + whole, Min, Max);
            _values[key] = next;
            if (next != current) OnChanged?.Invoke();
            return next;
        }

        /// <summary>디버그용: 궁합 배율 없이 그대로 더한다(단계가 바뀌면 알린다).</summary>
        public static void AddRaw(Mercenary a, Mercenary b, int amount)
        {
            if (a == null || b == null || a.Id == b.Id) return;
            int before = Get(a, b);
            int after = Math.Clamp(before + amount, Min, Max);
            _values[Key(a.Id, b.Id)] = after;
            AnnounceTierChange(a, b, before, after);
            OnChanged?.Invoke();
        }

        /// <summary>파티 안 다른 용병을 친밀도 높은 순으로(minValue 이상만).</summary>
        public static List<(Mercenary merc, int value)> Friends(Mercenary merc, int minValue = Min) =>
            PlayerParty.Instance.Members
                .Where(m => m.Id != merc.Id && m.IsAlive)
                .Select(m => (m, Get(merc, m)))
                .Where(p => p.Item2 >= minValue)
                .OrderByDescending(p => p.Item2)
                .ToList();

        /// <summary>함께 지낸 사람들의 모든 쌍에 amount만큼 바꾸고, 단계가 바뀐 쌍을 알린다.</summary>
        public static void AddAmong(IReadOnlyList<Mercenary> members, float amount)
        {
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                    AddAndAnnounce(members[i], members[j], amount);
        }

        /// <summary>
        /// 파견에서 함께 살아 돌아온 사람들: 승리면 모두 +8+4, 실패면 성격이 안 맞는 쌍은 서로 탓해 -6, 나머지는 +4.
        /// </summary>
        public static void AfterExpedition(IReadOnlyList<Mercenary> survivors, bool victory)
        {
            for (int i = 0; i < survivors.Count; i++)
                for (int j = i + 1; j < survivors.Count; j++)
                {
                    var a = survivors[i];
                    var b = survivors[j];
                    float amount = victory ? ExpeditionGain + VictoryBonus
                        : Compatibility(a.Personality, b.Personality) < 0 ? DefeatBlame : DefeatGain;
                    AddAndAnnounce(a, b, amount);
                }
        }

        private static void AddAndAnnounce(Mercenary a, Mercenary b, float amount)
        {
            int before = Get(a, b);
            int after = Add(a, b, amount);
            AnnounceTierChange(a, b, before, after);
        }

        private static void AnnounceTierChange(Mercenary a, Mercenary b, int before, int after)
        {
            var from = TierOf(before);
            var to = TierOf(after);
            if (from == to) return;
            string line = $"{a.Name}와(과) {b.Name}의 관계: {from.Name} → {to.Name}";
            ToastLog.Show(line);
            DailyLog.Add(line);
        }

        /// <summary>
        /// 파티에서 빠진 사람(해고·사망·이탈)이 든 쌍을 지운다. 전사 직후에도 슬픔(MercenaryCondition)을 계산할 수 있게
        /// 파티가 바뀔 때 바로 지우지 않고 저장·불러오기 때만 정리한다.
        /// </summary>
        private static void ForgetDeparted()
        {
            if (_values.Count == 0 && _pending.Count == 0) return;
            var ids = new HashSet<string>(PlayerParty.Instance.Members.Select(m => m.Id));
            bool Gone(string key)
            {
                int bar = key.IndexOf('|');
                return !ids.Contains(key.Substring(0, bar)) || !ids.Contains(key.Substring(bar + 1));
            }
            foreach (var key in _values.Keys.Where(Gone).ToList()) _values.Remove(key);
            foreach (var key in _pending.Keys.Where(Gone).ToList()) _pending.Remove(key);
        }

        // ---------- 저장 ----------

        public static List<Save.AffinitySave> SaveEntries()
        {
            ForgetDeparted();
            return _values.Select(kv =>
            {
                int bar = kv.Key.IndexOf('|');
                return new Save.AffinitySave { a = kv.Key.Substring(0, bar), b = kv.Key.Substring(bar + 1), value = kv.Value };
            }).ToList();
        }

        /// <summary>저장 파일에서 불러올 때(파티 복원 뒤).</summary>
        public static void Restore(IEnumerable<Save.AffinitySave> entries)
        {
            _values.Clear();
            _pending.Clear();
            if (entries != null)
                foreach (var e in entries)
                    if (!string.IsNullOrEmpty(e.a) && !string.IsNullOrEmpty(e.b) && e.a != e.b)
                        _values[Key(e.a, e.b)] = Math.Clamp(e.value, Min, Max);
            ForgetDeparted();
            OnChanged?.Invoke();
        }
    }
}
