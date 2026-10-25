using System;
using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 매력: 놓은 장식품의 매력 점수(BuildingCatalog.Entry.Charm) 합. 같은 장식은 MaxCountedPerKind개까지만 센다(한 가지를 잔뜩 깔아 올리지 않게).
    /// 단계(Tiers)가 오를수록 마을에 있는 용병의 하루 사기 회복(MercenaryCondition), 의뢰 성공 명성(ExpeditionBattle),
    /// 시장에 좋은 재능의 용병이 나올 확률(MercenaryMarketUI)이 오른다. 길드 창에 "마을 매력 N (단계)"로 보인다.
    /// </summary>
    public static class VillageCharm
    {
        public const int MaxCountedPerKind = 5;
        private const float ReputationPerTier = 0.05f;
        private const float MarketQualityPerTier = 0.15f;

        public struct Tier
        {
            public string Name;
            public int Required;
        }

        public static readonly Tier[] Tiers =
        {
            new Tier { Name = "수수한", Required = 0 },
            new Tier { Name = "아늑한", Required = 10 },
            new Tier { Name = "단정한", Required = 25 },
            new Tier { Name = "아름다운", Required = 45 },
            new Tier { Name = "이름난", Required = 70 },
        };

        private static int _score;
        private static int _stamp = -1;

        /// <summary>점수가 바뀔 수 있을 때(마을이 바뀜). 길드 창이 다시 그린다.</summary>
        public static event Action OnChanged;

        public static int Score
        {
            get
            {
                if (_stamp != VillageLayout.Stamp) Recount();
                return _score;
            }
        }

        public static int TierIndex
        {
            get
            {
                int score = Score, tier = 0;
                for (int i = 0; i < Tiers.Length; i++)
                    if (score >= Tiers[i].Required) tier = i;
                return tier;
            }
        }

        public static string TierName => Tiers[TierIndex].Name;

        /// <summary>마을에 있는 용병의 하루 사기 회복 보너스(단계마다 +1).</summary>
        public static int MoralePerDay => TierIndex;

        /// <summary>의뢰 성공 명성 배율(단계마다 +5%).</summary>
        public static float ReputationMultiplier => 1f + ReputationPerTier * TierIndex;

        /// <summary>시장 용병마다 재능을 두 번 굴려 좋은 쪽을 쓸 확률(단계마다 15%).</summary>
        public static float MarketQualityChance => MarketQualityPerTier * TierIndex;

        private static void Recount()
        {
            var counted = new Dictionary<BuildingCatalog.Entry, int>();
            int score = 0;
            foreach (var key in VillageLayout.Built.Keys)
            {
                var entry = BuildingCatalog.DecorForKey(key);
                if (entry == null || entry.Charm <= 0) continue;
                counted.TryGetValue(entry, out int n);
                if (n >= MaxCountedPerKind) continue;
                counted[entry] = n + 1;
                score += entry.Charm;
            }
            _score = score;
            _stamp = VillageLayout.Stamp;
        }

        /// <summary>길드 창 툴팁: 지금 효과와 다음 단계.</summary>
        public static string Tooltip()
        {
            int tier = TierIndex;
            string text = $"<b>마을 매력 {Score} ({TierName})</b>\n장식품을 놓으면 오릅니다(같은 장식은 {MaxCountedPerKind}개까지 셉니다).";
            text += tier == 0 ? "\n\n아직 효과가 없습니다."
                : $"\n\n마을 용병 사기 하루 +{MoralePerDay} · 의뢰 명성 +{Mathf.RoundToInt((ReputationMultiplier - 1f) * 100f)}% · 시장에 좋은 용병 {Mathf.RoundToInt(MarketQualityChance * 100f)}%";
            if (tier + 1 < Tiers.Length)
                text += $"\n\n다음 단계: <b>{Tiers[tier + 1].Name}</b> — 매력 {Tiers[tier + 1].Required} (앞으로 {Tiers[tier + 1].Required - Score})";
            return text;
        }

        // 마을이 바뀌면 알린다(점수는 다음에 물을 때 다시 센다)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe()
        {
            VillageLayout.OnChanged -= Notify;
            VillageLayout.OnChanged += Notify;
        }

        private static void Notify() => OnChanged?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _score = 0;
            _stamp = -1;
            OnChanged = null;
        }
    }
}
