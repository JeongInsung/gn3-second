using System;
using System.Collections.Generic;
using System.Linq;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 떠돌이 상인: 며칠(MinGapDays~MaxGapDays)마다 아침에 마을에 와서 그날 하루 머물며 건설 재료 몇 가지(StockKinds)를
    /// 비싸게(판값 × PriceFactor), 남은 수량만큼 판다. 오면 우편·알림이 오고, 마을 가운데쯤에 서 있는 상인을 누르면
    /// 상점 창(MerchantShopUI)이 열린다(VillagePartyPresenter). 다음 날 떠난다. 저장 파일에 같이 들어간다(SaveSystem).
    /// </summary>
    public static class TravelingMerchant
    {
        public const int PriceFactor = 3;
        private const int MinGapDays = 4;
        private const int MaxGapDays = 6;
        private const int FirstVisitDays = 3; // 새 게임은 사흘 뒤 첫 방문
        private const int StockKinds = 4;
        private const int MinStock = 3;
        private const int MaxStock = 6;

        private static int _hereDay = -1;   // 와 있는 날(-1이면 없음)
        private static int _nextDay = -1;   // 다음에 올 날(-1이면 아직 안 정함)
        private static readonly List<(LootItem item, int count)> _stock = new List<(LootItem, int)>();
        private static readonly System.Random _rng = new System.Random();

        /// <summary>왔다·떠났다·팔았다(상인 인형·상점 창이 다시 맞춘다).</summary>
        public static event Action OnChanged;

        public static bool IsHere => _hereDay >= 0 && _hereDay == GameClock.CurrentDay;
        public static int HereDay => _hereDay;
        public static IReadOnlyList<(LootItem item, int count)> Stock => _stock;

        public static int PriceOf(LootItem item) => item.Price * PriceFactor;

        /// <summary>하나 산다. 금화가 모자라거나 남은 게 없으면 false.</summary>
        public static bool Buy(LootItem item)
        {
            if (!IsHere) return false;
            int i = _stock.FindIndex(s => s.item == item);
            if (i < 0 || _stock[i].count <= 0 || !Wallet.TrySpend(PriceOf(item))) return false;
            _stock[i] = (item, _stock[i].count - 1);
            LootBag.Add(new[] { (item, 1) });
            ToastLog.Show($"떠돌이 상인에게서 {item.Name} 1개를 샀습니다 (−{PriceOf(item)}G)", false);
            OnChanged?.Invoke();
            return true;
        }

        private static void HandleNewDay()
        {
            int today = GameClock.CurrentDay;
            if (_hereDay >= 0 && today > _hereDay) Leave(today);
            if (_nextDay < 0) _nextDay = today + FirstVisitDays;
            if (_hereDay < 0 && today >= _nextDay) Arrive(today);
        }

        private static void Arrive(int today)
        {
            _hereDay = today;
            _stock.Clear();
            foreach (var item in LootCatalog.Materials.OrderBy(_ => _rng.Next()).Take(StockKinds))
                _stock.Add((item, _rng.Next(MinStock, MaxStock + 1)));
            string goods = string.Join(" · ", _stock.Select(s => s.item.Name));
            Mailbox.Post(MailKind.Event, "떠돌이 상인이 왔습니다", $"오늘 하루만 마을에 머뭅니다. 마을 가운데의 상인을 누르면 물건을 볼 수 있습니다.\n파는 것: {goods}");
            ToastLog.Show("떠돌이 상인이 마을에 왔습니다(오늘 하루). 상인을 눌러 건설 재료를 살 수 있습니다.", true);
            OnChanged?.Invoke();
        }

        private static void Leave(int today)
        {
            _hereDay = -1;
            _stock.Clear();
            _nextDay = today + _rng.Next(MinGapDays, MaxGapDays + 1);
            OnChanged?.Invoke();
        }

        /// <summary>테스트·이벤트용: 지금 바로 오게 한다.</summary>
        public static void ArriveNow() => Arrive(GameClock.CurrentDay);

        // ---------- 저장 ----------

        public static int SavedNextDay => _nextDay;

        public static List<Save.LootSave> SaveStock() =>
            _stock.Select(s => new Save.LootSave { name = s.item.Name, count = s.count }).ToList();

        public static void Restore(int hereDay, int nextDay, IEnumerable<Save.LootSave> stock)
        {
            _hereDay = hereDay;
            _nextDay = nextDay;
            _stock.Clear();
            if (stock != null)
                foreach (var s in stock)
                {
                    var item = LootCatalog.Find(s.name);
                    if (item != null) _stock.Add((item, Math.Max(0, s.count)));
                }
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _hereDay = -1;
            _nextDay = -1;
            _stock.Clear();
            OnChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += HandleNewDay;
    }
}
