using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>근처 길드와 주고받은 용병 한 명. Inbound = 빌려 온 손님(우리 파티에 있다), 아니면 빌려 준 우리 용병(그 길드에 가 있다).</summary>
    public class Loan
    {
        public Mercenary Merc;
        public string GuildId;
        public bool Inbound;
        public int EndDay;
        public int DailyFee;
        public bool Joint;      // 연합 의뢰 손님(대여료 없음, 그 원정이 끝나면 돌아간다)
        public string QuestId;  // 연합 의뢰 퀘스트(그 퀘스트에만 보낼 수 있다)
        public int RosterEnd;   // 빌려 온 용병의 원래 계약 끝나는 날(돌려보낼 때 되돌린다)
    }

    /// <summary>
    /// 용병 대여(근처 길드 창)와 연합 의뢰 손님. 자정마다 대여료를 주고받고 기한이 되면 돌려보낸다.
    ///  - 빌려 오기(우호 이상): 7일, 하루 대여료. 정원을 넘어도 합류하고 계약·주급은 없다. 죽으면 보상금 + 관계 크게 하락.
    ///  - 빌려 주기(경쟁 이상): 우리 용병이 7일 그 길드에 가 있다(그동안 자리는 비워 둔다). 대여료를 받고 돌아올 때 조금 자란다.
    ///  - 연합 의뢰: 수락하면 그 길드 용병이 손님으로 합류하고, 그 원정이 끝나면 돌아간다.
    /// 파티 정원 = 길드 단계 정원 + 손님 − 빌려 준 사람(Guild.ApplyPartySize).
    /// </summary>
    public static class Loans
    {
        public const int LoanDays = 7;
        public const float BorrowFeeRatio = 0.03f, LendFeeRatio = 0.025f; // 하루 대여료 = 시세 × 이 비율
        public const int DeathRelation = -20, JointDeathRelation = -10;

        private static readonly List<Loan> _all = new List<Loan>();
        public static IReadOnlyList<Loan> All => _all;

        public static Loan GuestLoan(Mercenary merc) => merc == null ? null : _all.FirstOrDefault(l => l.Inbound && l.Merc.Id == merc.Id);
        public static bool IsGuest(Mercenary merc) => GuestLoan(merc) != null;
        public static bool IsLentOutId(string id) => _all.Any(l => !l.Inbound && l.Merc.Id == id);
        public static IEnumerable<Loan> LentOut => _all.Where(l => !l.Inbound);
        public static int CapacityAdjust => _all.Count(l => l.Inbound) - _all.Count(l => !l.Inbound);

        /// <summary>이 손님을 이 퀘스트에 보낼 수 있는가(연합 손님은 자기 퀘스트에만).</summary>
        public static bool CanDispatchFor(Mercenary merc, Quest quest)
        {
            var loan = GuestLoan(merc);
            return loan == null || !loan.Joint || (quest != null && loan.QuestId == quest.Id);
        }

        /// <summary>손님 표시 "(대여 3일)" · "(연합)".</summary>
        public static string GuestTag(Mercenary merc)
        {
            var loan = GuestLoan(merc);
            if (loan == null) return null;
            if (loan.Joint) return "연합";
            return $"대여 {Mathf.Max(0, loan.EndDay - GameClock.CurrentDay)}일";
        }

        public static bool InVillage(Mercenary merc) =>
            merc.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(merc) && !TrainingHall.IsTraining(merc) && !Hospital.IsAdmitted(merc) && RestVenues.Find(merc) == null;

        public static int BorrowFee(RivalGuild g, Mercenary merc) =>
            Mathf.Max(3, Mathf.RoundToInt(Pricing.HireCost(merc) * BorrowFeeRatio * (RivalGuilds.IsAllied(g) ? 0.8f : 1f)));

        public static int LendFee(RivalGuild g, Mercenary merc) =>
            Mathf.Max(3, Mathf.RoundToInt(Pricing.HireCost(merc) * LendFeeRatio * (g.Temper == RivalTemper.Wealthy ? 1.2f : 1f)));

        /// <summary>빌려 올 수 없으면 이유(창 툴팁), 되면 null.</summary>
        public static string BorrowBlock(RivalGuild g, Mercenary merc)
        {
            if (!RivalGuilds.IsFriendly(g)) return "우호 관계(15 이상)여야 용병을 빌려준다.";
            if (g.Roster.Count <= 2) return "남는 용병이 없다.";
            if (Wallet.Gold < BorrowFee(g, merc) * LoanDays) return $"7일 대여료 {BorrowFee(g, merc) * LoanDays}G가 있어야 한다.";
            return null;
        }

        /// <summary>빌려 줄 수 없으면 이유, 되면 null.</summary>
        public static string LendBlock(RivalGuild g, Mercenary merc)
        {
            if (g.Relation <= -15) return "경쟁·적대 관계라 우리 용병을 받지 않는다.";
            if (IsGuest(merc)) return "빌려 온 손님은 다시 빌려줄 수 없다.";
            if (!InVillage(merc)) return "마을에 있는 용병만 빌려줄 수 있다.";
            if (g.Gold < LendFee(g, merc) * LoanDays) return "그 길드가 대여료를 낼 돈이 없다.";
            return null;
        }

        public static bool Borrow(RivalGuild g, Mercenary merc)
        {
            if (BorrowBlock(g, merc) != null || !g.Roster.Contains(merc)) return false;
            int end = g.RosterEnd.TryGetValue(merc.Id, out int e) ? e : GameClock.CurrentDay + 180;
            RivalGuilds.Leave(g, merc);
            _all.Add(new Loan { Merc = merc, GuildId = g.Id, Inbound = true, EndDay = GameClock.CurrentDay + LoanDays, DailyFee = BorrowFee(g, merc), RosterEnd = end });
            PlayerParty.Instance.RestoreAdd(merc);
            Guild.ApplyPartySize();
            RivalGuilds.AddRelation(g, 2, $"우리 길드가 {Josa.EulReul(merc.Name)} {LoanDays}일 빌려 갔다");
            ToastLog.Show($"{g.Name}에서 {Josa.EulReul(merc.Name)} {LoanDays}일 빌렸습니다 (하루 {BorrowFee(g, merc)}G).", false);
            return true;
        }

        public static bool Lend(RivalGuild g, Mercenary merc)
        {
            if (LendBlock(g, merc) != null) return false;
            PlayerParty.Instance.Remove(merc); // 계약은 그대로(Contracts.DailyCheck가 빌려 준 사람 계약은 지우지 않는다)
            _all.Add(new Loan { Merc = merc, GuildId = g.Id, Inbound = false, EndDay = GameClock.CurrentDay + LoanDays, DailyFee = LendFee(g, merc) });
            Guild.ApplyPartySize();
            RivalGuilds.AddRelation(g, 4, $"우리 길드의 {Josa.EulReul(merc.Name)} {LoanDays}일 빌렸다");
            ToastLog.Show($"{Josa.EulReul(merc.Name)} {g.Name}에 {LoanDays}일 빌려주었습니다 (하루 {LendFee(g, merc)}G).", false);
            return true;
        }

        /// <summary>연합 의뢰 수락: 그 길드에서 count명(센 순)이 손님으로 합류한다. 합류한 손님을 돌려준다.</summary>
        public static List<Mercenary> JoinQuest(Quest quest)
        {
            var result = new List<Mercenary>();
            var g = RivalGuilds.Get(quest.AllyGuildId ?? "");
            if (g == null) return result;
            result.AddRange(_all.Where(l => l.Joint && l.QuestId == quest.Id).Select(l => l.Merc)); // 이미 합류(다시 편성)
            if (result.Count > 0) return result;
            foreach (var merc in g.Roster.OrderByDescending(m => m.CombatPower).Take(quest.AllyCount).ToList())
            {
                int end = g.RosterEnd.TryGetValue(merc.Id, out int e) ? e : GameClock.CurrentDay + 180;
                RivalGuilds.Leave(g, merc);
                _all.Add(new Loan { Merc = merc, GuildId = g.Id, Inbound = true, Joint = true, QuestId = quest.Id, EndDay = GameClock.CurrentDay + 1, RosterEnd = end });
                PlayerParty.Instance.RestoreAdd(merc);
                result.Add(merc);
            }
            Guild.ApplyPartySize();
            if (result.Count > 0)
                ToastLog.Show($"연합 의뢰: {g.Name} 용병 {result.Count}명({string.Join(", ", result.Select(m => m.Name))})이 합류했습니다.", false);
            return result;
        }

        // ---------- 하루 ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += DailyTick;

        public static void DailyTick()
        {
            if (_all.Count == 0) return;
            int today = GameClock.CurrentDay;
            var party = PlayerParty.Instance;
            foreach (var loan in _all.ToList())
            {
                var g = RivalGuilds.Get(loan.GuildId);
                if (g == null) { _all.Remove(loan); continue; }
                if (loan.Inbound)
                {
                    if (!party.Members.Contains(loan.Merc) || !loan.Merc.IsAlive)
                    {
                        Lost(loan, g);
                        continue;
                    }
                    if (!loan.Joint)
                    {
                        if (Wallet.TrySpend(loan.DailyFee, "대여료")) g.Gold += loan.DailyFee;
                        else
                        {
                            RivalGuilds.AddRelation(g, -3, $"대여료를 내지 못해 {Josa.EulReul(loan.Merc.Name)} 돌려받았다");
                            loan.EndDay = today;
                        }
                    }
                    if (today >= loan.EndDay && InVillage(loan.Merc)) ReturnGuest(loan, g);
                }
                else
                {
                    int fee = Mathf.Min(loan.DailyFee, g.Gold);
                    g.Gold -= fee;
                    Wallet.Add(fee, "대여료");
                    if (today >= loan.EndDay) ComeBack(loan, g);
                }
            }
            Guild.ApplyPartySize();
        }

        private static void ReturnGuest(Loan loan, RivalGuild g)
        {
            _all.Remove(loan);
            party().Remove(loan.Merc);
            g.Roster.Add(loan.Merc);
            g.RosterEnd[loan.Merc.Id] = loan.RosterEnd;
            if (loan.Joint) RivalGuilds.AddNews(g, $"연합 의뢰를 마친 {Josa.IGa(loan.Merc.Name)} 돌아왔다");
            ToastLog.Show($"{Josa.IGa(loan.Merc.Name)} {g.Name} 소속으로 돌아갔습니다.", false);
        }

        /// <summary>손님이 죽었다(또는 사라졌다): 일반 대여는 보상금(시세) + 관계 -20, 연합 손님은 관계 -10.</summary>
        private static void Lost(Loan loan, RivalGuild g)
        {
            _all.Remove(loan);
            if (loan.Joint)
            {
                RivalGuilds.AddRelation(g, JointDeathRelation, $"연합 의뢰에서 {Josa.EulReul(loan.Merc.Name)} 잃었다");
                return;
            }
            int compensation = Mathf.Min(Pricing.HireCost(loan.Merc), Wallet.Gold);
            Wallet.TrySpend(compensation, "사망 보상금");
            g.Gold += compensation;
            RivalGuilds.AddRelation(g, DeathRelation, $"빌려 간 {Josa.EulReul(loan.Merc.Name)} 잃었다 (보상금 {compensation}G)");
            ToastLog.Show($"빌려 온 {Josa.EulReul(loan.Merc.Name)} 잃어 {g.Name}에 보상금 {compensation}G를 냈습니다.");
        }

        /// <summary>빌려 준 우리 용병이 돌아온다: 성장 1~2회, 가끔 다쳐서.</summary>
        private static void ComeBack(Loan loan, RivalGuild g)
        {
            _all.Remove(loan);
            var merc = loan.Merc;
            merc.GrantStartingGrowth(RivalGuilds.Rng.Next(1, 3), RivalGuilds.Rng); // 체력도 가득 찬다
            string hurt = "";
            if (RivalGuilds.Rng.NextDouble() < 0.25)
            {
                merc.SetHealth(Mathf.RoundToInt(merc.CurrentStats.MaxHealth * 0.6f));
                hurt = " 조금 다쳐서";
            }
            party().RestoreAdd(merc);
            ToastLog.Show($"{g.Name}에 빌려준 {Josa.IGa(merc.Name)}{hurt} 돌아왔습니다. 그곳 의뢰를 거들며 조금 강해졌습니다.");
        }

        private static PlayerParty party() => PlayerParty.Instance;

        /// <summary>떠나려는 손님(사기 바닥 등)은 길드를 떠나는 대신 원래 길드로 돌아간다.</summary>
        public static bool TryReturnLeaving(Mercenary merc)
        {
            var loan = GuestLoan(merc);
            var g = loan != null ? RivalGuilds.Get(loan.GuildId) : null;
            if (g == null) return false;
            ReturnGuest(loan, g);
            Guild.ApplyPartySize();
            return true;
        }

        // ---------- 저장 ----------

        public static void Restore(IEnumerable<Loan> loans)
        {
            _all.Clear();
            if (loans != null) _all.AddRange(loans.Where(l => l.Merc != null));
            // 계약 복원(Contracts.Restore)이 먼저라 손님도 기본 계약을 받았다 — 손님은 계약이 없다
            foreach (var l in _all.Where(l => l.Inbound)) Contracts.Remove(l.Merc);
            Guild.ApplyPartySize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _all.Clear();
    }
}
