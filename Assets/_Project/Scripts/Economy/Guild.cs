using System;
using GN3.Mercenaries;
using UnityEngine;

using GN3.World;

namespace GN3.Economy
{
    /// <summary>
    /// 길드 명성과 단계. 임무 성공으로 명성이 오르고(실패하면 조금 깎임), 단계가 오르면
    /// 높은 등급 퀘스트·높은 등급 용병·시장 레벨 범위·파티 정원·창고 칸이 늘어난다.
    /// 처음엔 "견습 용병단"이다: 길드 홀을 지으면 같은 단계가 "… 길드"가 되고, 길드 홀 없이는 MercenaryBandMaxRank까지만 오른다.
    /// </summary>
    public static class Guild
    {
        public struct RankInfo
        {
            public string Title; // "견습", "돌빛" … — 우리는 길드 홀 전엔 "… 용병단"(RankName), 근처 길드는 늘 "… 길드"(Name)
            public int Required;
            public MercenaryGrade MaxQuestGrade;
            public MercenaryGrade MaxMercGrade; // 시장에 나오는 용병 재능 상한
            public int MarketMinGrowth; // 시장 용병 경력(성장 횟수) 범위 — 나이 경력은 따로 더해진다
            public int MarketMaxGrowth;
            public int PartySize;
            public int StorageSlots; // 길드 창고 칸 수

            public string Name => Title + " 길드";
        }

        // 10단계(2026-10-19). 시작 정원 4명이라 은월의 숲 Lv3·4(정확히 4명)를 바로 보낼 수 있고,
        // 3단계(명성 150, 정원 6)면 Lv5 앤트킹 두 분대(3+3)를 꾸릴 수 있다.
        public static readonly RankInfo[] Ranks =
        {
            new RankInfo { Title = "견습",        Required = 0,    MaxQuestGrade = MercenaryGrade.C, MaxMercGrade = MercenaryGrade.B, MarketMinGrowth = 0,  MarketMaxGrowth = 4,  PartySize = 4,  StorageSlots = 10 },
            new RankInfo { Title = "돌빛",        Required = 50,   MaxQuestGrade = MercenaryGrade.C, MaxMercGrade = MercenaryGrade.B, MarketMinGrowth = 0,  MarketMaxGrowth = 6,  PartySize = 5,  StorageSlots = 12 },
            new RankInfo { Title = "철빛",        Required = 150,  MaxQuestGrade = MercenaryGrade.B, MaxMercGrade = MercenaryGrade.A, MarketMinGrowth = 0,  MarketMaxGrowth = 8,  PartySize = 6,  StorageSlots = 15 },
            new RankInfo { Title = "동빛",        Required = 300,  MaxQuestGrade = MercenaryGrade.B, MaxMercGrade = MercenaryGrade.A, MarketMinGrowth = 2,  MarketMaxGrowth = 10, PartySize = 7,  StorageSlots = 18 },
            new RankInfo { Title = "은빛",        Required = 500,  MaxQuestGrade = MercenaryGrade.A, MaxMercGrade = MercenaryGrade.A, MarketMinGrowth = 4,  MarketMaxGrowth = 12, PartySize = 8,  StorageSlots = 20 },
            new RankInfo { Title = "금빛",        Required = 750,  MaxQuestGrade = MercenaryGrade.A, MaxMercGrade = MercenaryGrade.S, MarketMinGrowth = 6,  MarketMaxGrowth = 14, PartySize = 9,  StorageSlots = 25 },
            new RankInfo { Title = "백금",        Required = 1050, MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinGrowth = 8,  MarketMaxGrowth = 16, PartySize = 10, StorageSlots = 30 },
            new RankInfo { Title = "미스릴",      Required = 1400, MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinGrowth = 10, MarketMaxGrowth = 18, PartySize = 12, StorageSlots = 35 },
            new RankInfo { Title = "용비늘",      Required = 1800, MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinGrowth = 12, MarketMaxGrowth = 20, PartySize = 14, StorageSlots = 40 },
            new RankInfo { Title = "전설의",      Required = 2300, MaxQuestGrade = MercenaryGrade.S, MaxMercGrade = MercenaryGrade.S, MarketMinGrowth = 14, MarketMaxGrowth = 22, PartySize = 16, StorageSlots = 50 },
        };

        public const int FailPenalty = 5;

        /// <summary>길드 홀이 없으면 이 단계(동빛 용병단)까지만 오른다. 길드 홀은 철빛(2)부터 지을 수 있다.</summary>
        public const int MercenaryBandMaxRank = 3;
        private const string GuildHallId = "guildhall";

        public static int Reputation { get; private set; }
        public static int Rank { get; private set; }
        public static RankInfo Current => Ranks[Rank];
        public static bool IsMaxRank => Rank >= Ranks.Length - 1;

        /// <summary>길드 홀을 지었는가 — 전엔 "… 용병단", 지으면 "… 길드". 예전 저장(건물이 다 있던 마을)은 길드 홀이 있던 마을이다.</summary>
        public static bool IsGuild => VillageLayout.HasBuilding(GuildHallId) || VillageLayout.PendingFullVillage;

        /// <summary>우리 쪽 단계 이름. 길드 홀 전엔 "견습 용병단"…, 길드 홀이 있어야 오르는 단계는 늘 "… 길드".</summary>
        public static string RankName(int rank) => Ranks[rank].Title + (IsGuild || rank > MercenaryBandMaxRank ? " 길드" : " 용병단");

        public static string CurrentName => RankName(Rank);

        /// <summary>명성은 다음 단계에 닿았는데 길드 홀이 없어 못 오르고 있다.</summary>
        public static bool IsCapped => !IsGuild && Rank == MercenaryBandMaxRank && RankFor(Reputation) > Rank;

        private static int PlayerRank(int reputation) => IsGuild ? RankFor(reputation) : Math.Min(RankFor(reputation), MercenaryBandMaxRank);

        /// <summary>명성이 바뀔 때. 인자는 단계가 올랐는지.</summary>
        public static event Action<bool> OnChanged;

        /// <summary>퀘스트 등급(F=0…S=6)에 따른 승리 명성.</summary>
        public static int VictoryReputation(MercenaryGrade questGrade) => 10 * ((int)questGrade + 1);

        public static void Add(int amount)
        {
            if (amount == 0) return;
            Reputation = Math.Max(0, Reputation + amount);
            int before = Rank;
            Rank = PlayerRank(Reputation);
            ApplyPartySize();
            OnChanged?.Invoke(Rank > before);
        }

        /// <summary>저장 파일에서 불러올 때. 알림 없이 값만 맞춘다(OnChanged는 표시 갱신용으로 부른다).</summary>
        public static void Restore(int reputation)
        {
            Reputation = Math.Max(0, reputation);
            Rank = PlayerRank(Reputation);
            ApplyPartySize();
            OnChanged?.Invoke(false);
        }

        /// <summary>
        /// 마을이 바뀌었다(VillageLayout): 길드 홀을 지으면 막혀 있던 단계가 풀리고(오르면 등급 상승 알림), 이름이 "… 길드"로 바뀐다.
        /// 단계가 그대로여도 이름이 바뀔 수 있어 표시 갱신용으로 OnChanged를 부른다.
        /// </summary>
        public static void RefreshRank(bool announce = true)
        {
            int before = Rank;
            Rank = PlayerRank(Reputation);
            if (PlayerParty.Instance != null) ApplyPartySize();
            OnChanged?.Invoke(announce && Rank > before);
        }

        /// <summary>파티 정원을 단계에 맞춘다. 이미 더 많이 데리고 있어도 내보내지는 않는다(새 고용만 막힘).
        /// 근처 길드에서 빌려 온 손님만큼 늘고, 빌려 준 우리 용병만큼 줄어든다(돌아올 자리 — Loans). 숙소 시설 단계만큼 더 늘어난다.</summary>
        public static void ApplyPartySize() => PlayerParty.Instance.MaxSize = Current.PartySize + GuildFacilities.DormitoryBonus + Loans.CapacityAdjust;

        /// <summary>명성 → 단계 번호(근처 길드도 같은 표를 쓴다 — RivalGuild.Rank).</summary>
        public static int RankFor(int reputation)
        {
            int rank = 0;
            for (int i = 0; i < Ranks.Length; i++)
                if (reputation >= Ranks[i].Required) rank = i;
            return rank;
        }

        public static string UnlockText(RankInfo info) =>
            $"퀘스트 {info.MaxQuestGrade}급까지 · 용병 재능 {info.MaxMercGrade}급까지 · 시장 경력 +{info.MarketMinGrowth}~{info.MarketMaxGrowth} · 파티 정원 {info.PartySize}명 · 창고 {info.StorageSlots}칸";

        /// <summary>HUD 툴팁: 지금 해금 내용과 다음 단계 조건.</summary>
        public static string Tooltip()
        {
            string text = $"<b>{CurrentName}</b> (명성 {Reputation})\n{UnlockText(Current)}";
            if (!IsMaxRank)
            {
                var next = Ranks[Rank + 1];
                string condition = IsCapped ? "길드 홀을 지어야 오를 수 있습니다 (명성은 충분)"
                    : $"명성 {next.Required} 필요 (앞으로 {next.Required - Reputation})" + (!IsGuild && Rank + 1 > MercenaryBandMaxRank ? " · 길드 홀 필요" : "");
                text += $"\n\n다음 단계: <b>{RankName(Rank + 1)}</b> — {condition}\n{UnlockText(next)}";
            }
            if (!IsGuild) text += "\n\n길드 홀을 지으면 용병단이 정식 길드가 됩니다.";
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
