using System;
using GN3.Mercenaries;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 길드 명성과 단계. 임무 성공으로 명성이 오르고(실패하면 조금 깎임), 단계가 오르면
    /// 높은 등급 퀘스트·높은 등급 용병·시장 레벨 범위·파티 정원·창고 칸이 늘어난다.
    /// </summary>
    public static class Guild
    {
        public struct RankInfo
        {
            public string Name;
            public int Required;
            public MercenaryGrade MaxQuestGrade;
            public MercenaryGrade MaxMercGrade;
            public int MarketMinLevel;
            public int MarketMaxLevel;
            public int PartySize;
            public int StorageSlots; // 길드 창고 칸 수
        }

        public static readonly RankInfo[] Ranks =
        {
            new RankInfo { Name = "견습 길드",   Required = 0,    MaxQuestGrade = MercenaryGrade.C, MaxMercGrade = MercenaryGrade.B, MarketMinLevel = 1, MarketMaxLevel = 2, PartySize = 3, StorageSlots = 10 },
            new RankInfo { Name = "동빛 길드",   Required = 100,  MaxQuestGrade = MercenaryGrade.B, MaxMercGrade = MercenaryGrade.A, MarketMinLevel = 1, MarketMaxLevel = 3, PartySize = 4, StorageSlots = 15 },
            new RankInfo { Name = "은빛 길드",   Required = 300,  MaxQuestGrade = MercenaryGrade.A, MaxMercGrade = MercenaryGrade.A, MarketMinLevel = 2, MarketMaxLevel = 4, PartySize = 5, StorageSlots = 20 },
            new RankInfo { Name = "금빛 길드",   Required = 600,  MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinLevel = 3, MarketMaxLevel = 5, PartySize = 6, StorageSlots = 30 },
            new RankInfo { Name = "전설의 길드", Required = 1000, MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinLevel = 4, MarketMaxLevel = 6, PartySize = 8, StorageSlots = 40 },
        };

        public const int FailPenalty = 5;

        public static int Reputation { get; private set; }
        public static int Rank { get; private set; }
        public static RankInfo Current => Ranks[Rank];
        public static bool IsMaxRank => Rank >= Ranks.Length - 1;

        /// <summary>명성이 바뀔 때. 인자는 단계가 올랐는지.</summary>
        public static event Action<bool> OnChanged;

        /// <summary>퀘스트 등급(F=0…S=6)에 따른 승리 명성.</summary>
        public static int VictoryReputation(MercenaryGrade questGrade) => 10 * ((int)questGrade + 1);

        public static void Add(int amount)
        {
            if (amount == 0) return;
            Reputation = Math.Max(0, Reputation + amount);
            int before = Rank;
            Rank = RankFor(Reputation);
            ApplyPartySize();
            OnChanged?.Invoke(Rank > before);
        }

        /// <summary>저장 파일에서 불러올 때. 알림 없이 값만 맞춘다(OnChanged는 표시 갱신용으로 부른다).</summary>
        public static void Restore(int reputation)
        {
            Reputation = Math.Max(0, reputation);
            Rank = RankFor(Reputation);
            ApplyPartySize();
            OnChanged?.Invoke(false);
        }

        /// <summary>파티 정원을 단계에 맞춘다. 이미 더 많이 데리고 있어도 내보내지는 않는다(새 고용만 막힘).</summary>
        public static void ApplyPartySize() => PlayerParty.Instance.MaxSize = Current.PartySize;

        private static int RankFor(int reputation)
        {
            int rank = 0;
            for (int i = 0; i < Ranks.Length; i++)
                if (reputation >= Ranks[i].Required) rank = i;
            return rank;
        }

        public static string UnlockText(RankInfo info) =>
            $"퀘스트 {info.MaxQuestGrade}급까지 · 용병 {info.MaxMercGrade}급까지 · 시장 Lv.{info.MarketMinLevel}~{info.MarketMaxLevel} · 파티 정원 {info.PartySize}명 · 창고 {info.StorageSlots}칸";

        /// <summary>HUD 툴팁: 지금 해금 내용과 다음 단계 조건.</summary>
        public static string Tooltip()
        {
            string text = $"<b>{Current.Name}</b> (명성 {Reputation})\n{UnlockText(Current)}";
            if (!IsMaxRank)
            {
                var next = Ranks[Rank + 1];
                text += $"\n\n다음 단계: <b>{next.Name}</b> — 명성 {next.Required} 필요 (앞으로 {next.Required - Reputation})\n{UnlockText(next)}";
            }
            text += $"\n\n임무 성공 시 명성 +10×(퀘스트 등급+1), 실패 시 −{FailPenalty}";
            return text;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Reputation = 0;
            Rank = 0;
            OnChanged = null;
        }
    }
}
