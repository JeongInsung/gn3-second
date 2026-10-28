using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 금고 장부: 골드가 들고 난 것을 날짜·시각·이유와 함께 적는다(Wallet.OnLedger). 최근 KeepDays일만 둔다.
    /// 장부 창(GoldLedgerWindow)과 일일 보고서(Mailbox.PostReport)가 읽는다. 저장은 SaveData.ledger.
    /// </summary>
    public static class GoldLedger
    {
        public const int KeepDays = 14;

        public struct Entry
        {
            public int Day;
            public int Hour;
            public int Amount; // +수입 / -지출
            public string Reason;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => _entries;

        /// <summary>그 날 거래(시간 순).</summary>
        public static List<Entry> OnDay(int day) => _entries.Where(e => e.Day == day).ToList();

        /// <summary>그 기간(fromDay~toDay 포함)의 이유별 합계: 수입·지출(지출은 양수로).</summary>
        public static List<(string reason, int income, int expense)> Totals(int fromDay, int toDay) =>
            _entries.Where(e => e.Day >= fromDay && e.Day <= toDay)
                .GroupBy(e => e.Reason)
                .Select(g => (g.Key, g.Where(e => e.Amount > 0).Sum(e => e.Amount), -g.Where(e => e.Amount < 0).Sum(e => e.Amount)))
                .ToList();

        public static int FirstDay => _entries.Count > 0 ? _entries[0].Day : GameClock.CurrentDay;

        private static void Record(int amount, string reason)
        {
            if (amount == 0) return;
            _entries.Add(new Entry
            {
                Day = GameClock.CurrentDay,
                Hour = Mathf.FloorToInt(GameClock.CurrentHour),
                Amount = amount,
                Reason = string.IsNullOrEmpty(reason) ? Wallet.OtherReason : reason,
            });
            int oldest = GameClock.CurrentDay - KeepDays + 1;
            _entries.RemoveAll(e => e.Day < oldest);
        }

        /// <summary>저장 파일에서 불러온 장부로 바꾼다.</summary>
        public static void Restore(IEnumerable<Entry> entries)
        {
            _entries.Clear();
            if (entries != null) _entries.AddRange(entries);
        }

        // Domain Reload가 꺼져 있어도 Play 진입마다 비우고 다시 구독한다(Wallet이 SubsystemRegistration에서 이벤트를 비운 뒤).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetForPlaySession()
        {
            _entries.Clear();
            Wallet.OnLedger -= Record;
            Wallet.OnLedger += Record;
        }
    }
}
