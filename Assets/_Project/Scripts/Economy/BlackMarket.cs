using System;
using System.Collections.Generic;
using System.Linq;
using GN3.UI;
using GN3.World;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 암거래 상인: 며칠(MinGapDays~MaxGapDays)마다 아침에 마을에 와서 그날 하루 머물며 전리품을 정가보다 비싸게 사 간다
    /// (등급이 높을수록 더 — Multiplier). 건설 재료(등급 없음)·동전 주머니는 안 산다. 올 때마다 들고 온 돈(Purse)만큼만 산다.
    /// 일반 판매(길드 창고, 정가 — LootBag.Sell)는 그대로. 마을 가운데 아래쪽에 서 있는 상인을 누르면 BlackMarketShopUI가 열린다
    /// (VillagePartyPresenter). 다음 날 떠난다. 저장 파일에 같이 들어간다(SaveSystem). TravelingMerchant와 같은 틀.
    /// </summary>
    public static class BlackMarket
    {
        private const int MinGapDays = 6;
        private const int MaxGapDays = 9;
        private const int FirstVisitDays = 5; // 새 게임은 닷새 뒤 첫 방문
        private const int MinPurse = 2000;
        private const int MaxPurse = 4000;

        private static int _hereDay = -1;   // 와 있는 날(-1이면 없음)
        private static int _nextDay = -1;   // 다음에 올 날(-1이면 아직 안 정함)
        private static int _purse;          // 이번 방문에 남은 돈
        private static readonly System.Random _rng = new System.Random();

        /// <summary>왔다·떠났다·샀다(상인 인형·상점 창이 다시 맞춘다).</summary>
        public static event Action OnChanged;

        public static bool IsHere => _hereDay >= 0 && _hereDay == GameClock.CurrentDay;
        public static int HereDay => _hereDay;
        public static int Purse => _purse;

        /// <summary>등급별 배율: 최하급 1.2 · 하급 1.35 · 중급 1.5 · 상급 1.75 · 최상급 2.</summary>
        public static float Multiplier(LootGrade grade) => grade switch
        {
            LootGrade.Lowest => 1.2f,
            LootGrade.Low => 1.35f,
            LootGrade.Mid => 1.5f,
            LootGrade.High => 1.75f,
            _ => 2f,
        };

        /// <summary>사 가는 것: 등급 있는 전리품(건설 재료 X)이고 동전 주머니(이미 금화)는 아님.</summary>
        public static bool Buys(LootItem item) => item != null && item.HasGrade && item != LootCatalog.CoinPouch;

        public static int PriceOf(LootItem item) => Mathf.RoundToInt(item.Price * Multiplier(item.Grade));

        /// <summary>n개까지 판다(남은 돈으로 살 수 있는 만큼). 받은 금화를 돌려준다.</summary>
        public static int Sell(LootItem item, int n)
        {
            if (!IsHere || !Buys(item)) return 0;
            int price = PriceOf(item);
            n = Math.Min(Math.Min(n, LootBag.Count(item)), price > 0 ? _purse / price : 0);
            if (n <= 0 || !LootBag.Consume(item, n)) return 0;
            int gold = price * n;
            _purse -= gold;
            Wallet.Add(gold);
            OnChanged?.Invoke();
            return gold;
        }

        /// <summary>사 가는 것을 값 높은 것부터 남은 돈 안에서 모두 판다. 받은 금화를 돌려준다.</summary>
        public static int SellAll()
        {
            int total = 0;
            foreach (var (item, count) in LootBag.Entries.Where(e => Buys(e.item)).OrderByDescending(e => PriceOf(e.item)).ToList())
                total += Sell(item, count);
            return total;
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
            _purse = _rng.Next(MinPurse / 100, MaxPurse / 100 + 1) * 100;
            Mailbox.Post(MailKind.Event, "암거래 상인이 왔습니다",
                $"오늘 하루만 마을에 머뭅니다. 전리품을 정가보다 비싸게 사 갑니다(등급이 높을수록 더, 최상급은 2배).\n가져온 돈: {_purse}G · 건설 재료는 사지 않습니다.");
            ToastLog.Show("암거래 상인이 마을에 왔습니다(오늘 하루). 상인을 눌러 전리품을 비싸게 팔 수 있습니다.", true);
            OnChanged?.Invoke();
        }

        private static void Leave(int today)
        {
            _hereDay = -1;
            _purse = 0;
            _nextDay = today + _rng.Next(MinGapDays, MaxGapDays + 1);
            OnChanged?.Invoke();
        }

        /// <summary>테스트·이벤트용: 지금 바로 오게 한다.</summary>
        public static void ArriveNow() => Arrive(GameClock.CurrentDay);

        // ---------- 저장 ----------

        public static int SavedNextDay => _nextDay;

        public static void Restore(int hereDay, int nextDay, int purse)
        {
            _hereDay = hereDay;
            _nextDay = nextDay;
            _purse = Math.Max(0, purse);
            OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _hereDay = -1;
            _nextDay = -1;
            _purse = 0;
            OnChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Subscribe() => GameClock.OnDayAdvanced += HandleNewDay;
    }
}
