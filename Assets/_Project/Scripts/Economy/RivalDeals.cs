using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 근처 길드와의 거래(근처 길드 창): 영입(이적료 + 계약 협상), 판매, 1:1 맞교환, 연합 의뢰 보상 나누기.
    /// 값은 시세(Pricing.HireCost)에 관계·성향을 곱한다. 대여는 Loans.
    /// </summary>
    public static class RivalDeals
    {
        public const float JointShare = 0.4f; // 연합 의뢰 보상 중 그 길드 몫
        public const int PoachRelation = -5, PoachFriendlyRelation = -2, SellRelation = 3, TradeRelation = 2, JointWinRelation = 8;

        // ---------- 영입 ----------

        /// <summary>이적료: 시세 × (1.5 − 관계/200), 동맹이면 많아야 × 1.1.</summary>
        public static int TransferFee(RivalGuild g, Mercenary merc)
        {
            float factor = 1.5f - g.Relation / 200f;
            if (RivalGuilds.IsAllied(g)) factor = Mathf.Min(factor, 1.1f);
            return Mathf.RoundToInt(Pricing.HireCost(merc) * factor);
        }

        /// <summary>그 길드 핵심(가장 센 용병) — 팔지 않는다.</summary>
        public static bool IsCore(RivalGuild g, Mercenary merc) =>
            g.Roster.Count > 0 && g.Roster.OrderByDescending(m => m.CombatPower).First() == merc;

        /// <summary>영입할 수 없으면 이유, 되면 null.</summary>
        public static string PoachBlock(RivalGuild g, Mercenary merc)
        {
            if (RivalGuilds.IsHostile(g)) return "적대 관계라 거래하지 않는다.";
            if (IsCore(g, merc)) return "길드의 핵심이라 내주지 않는다.";
            if (g.Roster.Count <= 2) return "남는 용병이 없다.";
            var party = PlayerParty.Instance;
            if (party.Members.Count >= party.MaxSize) return "파티 정원이 가득 찼다.";
            if (ContractTerms.For(merc, renewal: false).Interest == Interest.NotInterested) return "우리 길드에 관심이 없다(명성을 올리면 바뀔 수 있다).";
            if (ContractNegotiationPanel.IsBrokenOff(merc)) return "오늘 협상이 깨졌다.";
            if (Wallet.Gold < TransferFee(g, merc)) return $"이적료 {TransferFee(g, merc)}G가 모자란다.";
            return null;
        }

        /// <summary>이적료를 내고 계약 협상을 연다. 서명하면 우리 길드로, 서명 없이 닫으면 이적료를 돌려받는다.</summary>
        public static void BeginPoach(RivalGuild g, Mercenary merc)
        {
            if (PoachBlock(g, merc) != null) return;
            int fee = TransferFee(g, merc);
            if (!Wallet.TrySpend(fee)) return;
            bool signed = false;
            ContractNegotiationPanel.OpenHire(merc, () =>
            {
                signed = true;
                RivalGuilds.Leave(g, merc);
                g.Gold += fee;
                RivalGuilds.AddRelation(g, RivalGuilds.IsFriendly(g) ? PoachFriendlyRelation : PoachRelation,
                    $"우리 길드가 이적료 {fee}G를 내고 {Josa.EulReul(merc.Name)} 데려갔다");
                ToastLog.Show($"{g.Name}에서 {Josa.EulReul(merc.Name)} 영입했습니다 (이적료 {fee}G).", false);
            }, () =>
            {
                if (signed) return;
                Wallet.Add(fee);
                ToastLog.Show($"영입이 성사되지 않아 이적료 {fee}G를 돌려받았습니다.", false);
            });
        }

        // ---------- 판매·맞교환 ----------

        /// <summary>그 길드가 우리 용병에 내는 값: 시세 × 0.6~0.8(관계) × 성향, 금화 한도. 사지 않으면 0.</summary>
        public static int SellOffer(RivalGuild g, Mercenary merc)
        {
            if (RivalGuilds.IsHostile(g) || g.Roster.Count >= g.TargetSize + 2) return 0;
            float factor = 0.6f + 0.2f * (g.Relation - RivalGuilds.MinRelation) / (RivalGuilds.MaxRelation - RivalGuilds.MinRelation);
            if (g.Temper == RivalTemper.Wealthy) factor *= 1.1f;
            if (g.Temper == RivalTemper.Cunning) factor *= 0.9f;
            int offer = Mathf.RoundToInt(Pricing.HireCost(merc) * factor);
            return offer <= g.Gold ? offer : 0;
        }

        /// <summary>팔거나 맞바꿀 수 없으면 이유, 되면 null.</summary>
        public static string SellBlock(RivalGuild g, Mercenary merc)
        {
            if (RivalGuilds.IsHostile(g)) return "적대 관계라 거래하지 않는다.";
            if (Loans.IsGuest(merc)) return "빌려 온 손님이다.";
            if (!Loans.InVillage(merc)) return "마을에 있는 용병만 보낼 수 있다.";
            return null;
        }

        public static bool Sell(RivalGuild g, Mercenary merc)
        {
            int offer = SellOffer(g, merc);
            if (SellBlock(g, merc) != null || offer <= 0) return false;
            Release(merc);
            g.Gold -= offer;
            Wallet.Add(offer);
            RivalGuilds.Join(g, merc);
            RivalGuilds.AddRelation(g, SellRelation, $"우리 길드에서 {Josa.EulReul(merc.Name)} {offer}G에 샀다");
            ToastLog.Show($"{Josa.EulReul(merc.Name)} {g.Name}에 {offer}G를 받고 보냈습니다.", false);
            return true;
        }

        /// <summary>맞교환 차액: 양수면 우리가 낸다, 음수면 받는다(그 용병 이적료 − 우리 용병 판매가).</summary>
        public static int TradeDifference(RivalGuild g, Mercenary ours, Mercenary theirs)
        {
            int ourValue = Mathf.RoundToInt(Pricing.HireCost(ours) * (0.6f + 0.2f * (g.Relation - RivalGuilds.MinRelation) / (RivalGuilds.MaxRelation - RivalGuilds.MinRelation)));
            return TransferFee(g, theirs) - ourValue;
        }

        public static string TradeBlock(RivalGuild g, Mercenary ours, Mercenary theirs)
        {
            string block = SellBlock(g, ours);
            if (block != null) return block;
            if (IsCore(g, theirs)) return "길드의 핵심이라 내주지 않는다.";
            int diff = TradeDifference(g, ours, theirs);
            if (diff > 0 && Wallet.Gold < diff) return $"차액 {diff}G가 모자란다.";
            if (diff < 0 && g.Gold < -diff) return "그 길드가 차액을 낼 돈이 없다.";
            return null;
        }

        /// <summary>
        /// 1:1 맞교환: 우리 용병이 가고 그 용병이 온다(차액은 금화로). 온 용병은 보낸 용병의 남은 계약을 이어받고,
        /// 주급만 자기 시세로 바뀐다(협상 없이).
        /// </summary>
        public static bool Trade(RivalGuild g, Mercenary ours, Mercenary theirs)
        {
            if (TradeBlock(g, ours, theirs) != null) return false;
            int diff = TradeDifference(g, ours, theirs);
            if (diff > 0 && !Wallet.TrySpend(diff)) return false;
            if (diff < 0) Wallet.Add(-diff);
            g.Gold += diff;

            int days = Mathf.Max(ContractTuning.ShortContractDays, Contracts.RemainingDays(ours));
            var demand = ContractTerms.For(theirs, renewal: false);
            Release(ours);
            RivalGuilds.Leave(g, theirs);
            RivalGuilds.Join(g, ours);
            PlayerParty.Instance.RestoreAdd(theirs);
            Contracts.Sign(theirs, new ContractOffer { WeeklyWage = demand.WeeklyWage, LengthDays = days, SigningFee = 0, WinBonus = 0 });
            RivalGuilds.AddRelation(g, TradeRelation, $"{Josa.WaGwa(ours.Name)} {Josa.EulReul(theirs.Name)} 맞바꿨다");
            string money = diff > 0 ? $" (차액 -{diff}G)" : diff < 0 ? $" (차액 +{-diff}G)" : "";
            ToastLog.Show($"{Josa.WaGwa(g.Name)} 맞교환: {ours.Name} → {theirs.Name}{money}. 계약 {days}일·주급 {demand.WeeklyWage}G를 이어받습니다.", false);
            return true;
        }

        /// <summary>우리 길드에서 내보낸다(위약금 없음 — 상대 길드가 계약을 사 간다).</summary>
        private static void Release(Mercenary merc)
        {
            PlayerParty.Instance.Remove(merc);
            Contracts.Remove(merc);
        }

        // ---------- 연합 의뢰 ----------

        /// <summary>새 게시판: 우호 이상 길드가 있으면 Lv3 이상(4명·6명) 의뢰 하나를 연합 의뢰로(도전 퀘스트 먼저). 그 길드가 절반을 보낸다.</summary>
        public static void MakeJointQuests(IReadOnlyList<Quest> board, bool force = false)
        {
            if (board == null || (!force && RivalGuilds.Rng.NextDouble() >= 0.6)) return;
            var candidates = board.Where(q => q.RequiredDispatchSize >= 4 && !q.IsJoint).OrderByDescending(q => q.IsChallenge).ToList();
            if (candidates.Count == 0) return;
            var quest = candidates[0];
            int count = quest.RequiredDispatchSize / 2;
            var friends = RivalGuilds.All.Where(g => RivalGuilds.IsFriendly(g) && g.Roster.Count > count).ToList();
            if (friends.Count == 0) return;
            var ally = friends[RivalGuilds.Rng.Next(friends.Count)];
            quest.AllyGuildId = ally.Id;
            quest.AllyCount = count;
        }

        /// <summary>연합 의뢰 성공: 보상의 40%와 명성 절반은 그 길드 몫, 관계 +8. 우리 몫을 돌려준다.</summary>
        public static int ShareReward(Quest quest, int reward, int reputation)
        {
            var g = RivalGuilds.Get(quest.AllyGuildId ?? "");
            if (g == null) return reward;
            int share = Mathf.RoundToInt(reward * JointShare);
            g.Gold += share;
            g.Reputation += reputation / 2;
            RivalGuilds.AddRelation(g, JointWinRelation, $"우리 길드와 연합 의뢰 '{quest.Title}' 성공 (몫 {share}G)");
            return reward - share;
        }

        public static string JointLabel(Quest quest)
        {
            var g = quest.IsJoint ? RivalGuilds.Get(quest.AllyGuildId) : null;
            return g == null ? null : $"{g.Name} 연합 — 그 길드 용병 {quest.AllyCount}명이 함께 간다. 보상의 {Mathf.RoundToInt(JointShare * 100)}%는 그 길드 몫.";
        }
    }
}
