using System;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>플레이어 골드. 용병 고용에 쓰고 퀘스트 보상으로 번다.</summary>
    public static class Wallet
    {
        public const int StartingGold = 300;

        public static int Gold { get; private set; } = StartingGold;

        /// <summary>골드가 바뀔 때마다 변화량(+벌이 / -지출)과 함께 불린다.</summary>
        public static event Action<int> OnChanged;

        /// <summary>벌거나 쓸 때마다(불러오기 제외) 변화량과 이유 — 금고 장부(GoldLedger)가 받는다.</summary>
        public static event Action<int, string> OnLedger;

        public const string OtherReason = "기타";

        public static void Add(int amount, string reason = OtherReason)
        {
            if (amount <= 0) return;
            Gold += amount;
            OnChanged?.Invoke(amount);
            OnLedger?.Invoke(amount, reason);
        }

        /// <summary>저장 파일에서 불러올 때 골드를 그대로 맞춘다.</summary>
        public static void Restore(int gold)
        {
            int delta = Math.Max(0, gold) - Gold;
            Gold = Math.Max(0, gold);
            OnChanged?.Invoke(delta);
        }

        public static bool TrySpend(int amount, string reason = OtherReason)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            OnChanged?.Invoke(-amount);
            if (amount > 0) OnLedger?.Invoke(-amount, reason);
            return true;
        }

        // Enter Play Mode Options에서 Domain Reload가 꺼져 있어도 Play 진입마다 초기화(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Gold = StartingGold;
            OnChanged = null;
            OnLedger = null;
        }
    }
}
