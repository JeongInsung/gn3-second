using System.Collections.Generic;
using System.Linq;
using GN3.Economy;
using GN3.Quests;
using GN3.Save;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Mercenaries
{
    /// <summary>한 용병의 계약.</summary>
    public class Contract
    {
        public int WeeklyWage;
        public int EndDay;    // 이 날 자정에 끝난다
        public int WinBonus;  // 퀘스트 성공 때마다(살아 돌아오면)
        public int SignedDay;
    }

    /// <summary>
    /// 용병 계약 장부(용병 id → 계약). 주급은 계약서대로 낸다(MercenaryCondition.WeeklyWage).
    /// 매일 자정(MercenaryCondition.HandleNewDay → DailyCheck): 만료 14일 전 알림, 3일 전 중요 알림,
    /// 만료일이면 길드를 떠난다(파견 중이면 돌아온 뒤). 재계약은 협상 창(재계약 모드). 수치는 ContractTuning.
    /// </summary>
    public static class Contracts
    {
        private static readonly Dictionary<string, Contract> Book = new Dictionary<string, Contract>();

        /// <summary>계약을 맺거나 바꿨을 때(파티 창이 재계약 표시를 새로 그린다).</summary>
        public static event System.Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Book.Clear();
            OnChanged = null;
        }

        public static Contract Get(Mercenary merc) => merc != null && Book.TryGetValue(merc.Id, out var c) ? c : null;

        public static void Sign(Mercenary merc, ContractOffer offer)
        {
            Book[merc.Id] = new Contract
            {
                WeeklyWage = offer.WeeklyWage,
                EndDay = GameClock.CurrentDay + offer.LengthDays,
                WinBonus = offer.WinBonus,
                SignedDay = GameClock.CurrentDay,
            };
            OnChanged?.Invoke();
        }

        public static void Remove(Mercenary merc)
        {
            if (merc != null) Book.Remove(merc.Id);
        }

        /// <summary>계약이 없는 용병(예전 저장)에게 기본 계약: 예전 공식 주급, 오늘부터 60일, 보너스 없음.</summary>
        public static Contract Ensure(Mercenary merc)
        {
            var contract = Get(merc);
            if (contract != null) return contract;
            contract = new Contract
            {
                WeeklyWage = Mathf.Max(1, Mathf.RoundToInt(Pricing.HireCost(merc) * ContractTuning.WageRatio)),
                EndDay = GameClock.CurrentDay + ContractTuning.DefaultLengthDays,
                SignedDay = GameClock.CurrentDay,
            };
            Book[merc.Id] = contract;
            return contract;
        }

        public static int RemainingDays(Mercenary merc) => Mathf.Max(0, Ensure(merc).EndDay - GameClock.CurrentDay);

        /// <summary>지금 해고하면 내는 위약금: 남은 주 × 주급 × 50%(올림).</summary>
        public static int ReleaseCost(Mercenary merc)
        {
            var c = Ensure(merc);
            return Mathf.CeilToInt(RemainingDays(merc) / 7f * c.WeeklyWage * ContractTuning.ReleaseRatio);
        }

        /// <summary>"주급 22G · 3월 30일까지(12일 남음) · 보너스 10G"</summary>
        public static string Describe(Mercenary merc)
        {
            var c = Ensure(merc);
            string text = $"주급 {c.WeeklyWage}G · {GameCalendar.Format(c.EndDay)}까지({RemainingDays(merc)}일 남음)";
            if (c.WinBonus > 0) text += $" · 성공 보너스 {c.WinBonus}G";
            return text;
        }

        /// <summary>재계약 협상이 필요한 때(만료 NoticeDays일 이내).</summary>
        public static bool NeedsRenewal(Mercenary merc) => RemainingDays(merc) <= ContractTuning.NoticeDays;

        /// <summary>매일 자정: 만료 알림과 만료 처리. 파티에 없는 사람의 계약은 지운다.</summary>
        public static void DailyCheck()
        {
            var members = PlayerParty.Instance.Members.Where(m => m.IsAlive).ToList();
            foreach (var id in Book.Keys.Where(id => members.All(m => m.Id != id)).ToList()) Book.Remove(id);

            foreach (var merc in members)
            {
                int left = RemainingDays(merc);
                if (left <= 0)
                {
                    MercenaryCondition.EndContract(merc);
                    continue;
                }
                if (left == ContractTuning.NoticeDays || left == ContractTuning.UrgentNoticeDays)
                {
                    bool urgent = left == ContractTuning.UrgentNoticeDays;
                    string title = $"{merc.Name} 계약 만료 {left}일 전";
                    string body = $"{Josa.EunNeun(merc.Name)} {GameCalendar.Format(Ensure(merc).EndDay)}에 계약이 끝납니다.\n" +
                                  "파티 창이나 용병 정보에서 [재계약]을 눌러 협상하세요. 재계약하지 않으면 길드를 떠납니다.";
                    DailyLog.Add(title);
                    Mailbox.Post(MailKind.Alert, title, body, important: urgent);
                }
            }
            OnChanged?.Invoke(); // 남은 날이 줄었다(파티 창의 "계약 n일 남음"·재계약 버튼)
        }

        /// <summary>퀘스트 성공: 살아 돌아온 대원에게 계약서의 성공 보너스. 결과 편지에 넣을 한 줄(없으면 null).</summary>
        public static string PayWinBonuses(IEnumerable<Mercenary> survivors)
        {
            int paid = 0;
            var unpaid = new List<string>();
            foreach (var merc in survivors)
            {
                var c = Get(merc);
                if (c == null || c.WinBonus <= 0) continue;
                if (Wallet.TrySpend(c.WinBonus))
                {
                    paid += c.WinBonus;
                    merc.AddMorale(ContractTuning.BonusPaidMorale);
                }
                else
                {
                    unpaid.Add(merc.Name);
                    merc.AddMorale(-ContractTuning.BonusUnpaidMorale);
                }
            }
            if (paid == 0 && unpaid.Count == 0) return null;
            string line = paid > 0 ? $"성과 보너스 -{paid}G" : "";
            if (unpaid.Count > 0) line += (line.Length > 0 ? " · " : "") + $"골드 부족으로 보너스 미지급: {string.Join(", ", unpaid)} (사기 -{ContractTuning.BonusUnpaidMorale})";
            DailyLog.Add(line);
            return line;
        }

        // ---------- 저장 ----------

        public static List<ContractSave> SaveEntries() =>
            Book.Select(p => new ContractSave { id = p.Key, wage = p.Value.WeeklyWage, endDay = p.Value.EndDay, winBonus = p.Value.WinBonus, signedDay = p.Value.SignedDay }).ToList();

        /// <summary>불러오기(파티 복원 뒤). 예전 저장처럼 계약이 없는 용병은 기본 계약을 받는다.</summary>
        public static void Restore(IEnumerable<ContractSave> entries)
        {
            Book.Clear();
            if (entries != null)
                foreach (var e in entries)
                    Book[e.id] = new Contract { WeeklyWage = e.wage, EndDay = e.endDay, WinBonus = e.winBonus, SignedDay = e.signedDay };
            foreach (var merc in PlayerParty.Instance.Members) Ensure(merc);
        }
    }
}
