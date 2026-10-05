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

        public static void Add(int amount)
        {
            if (amount <= 0) return;
            Gold += amount;
            OnChanged?.Invoke(amount);
        }

        public static bool TrySpend(int amount)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            OnChanged?.Invoke(-amount);
            return true;
        }

        // Enter Play Mode Options에서 Domain Reload가 꺼져 있어도 Play 진입마다 초기화(GameClock과 같은 방식).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            Gold = StartingGold;
            OnChanged = null;
        }
    }
}
