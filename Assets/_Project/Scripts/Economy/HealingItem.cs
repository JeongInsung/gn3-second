using System.Collections.Generic;
using GN3.Mercenaries;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 의약품 상점에서 파는 치료 아이템. 사는 즉시 그 자리에서 치료한다(인벤토리 없음).
    /// 회복량은 최대 체력 대비 비율(1 = 완전 회복). 아이콘은 Resources/HealingItems/item_NN.
    /// </summary>
    public class HealingItem
    {
        public string Name { get; }
        public int Price { get; }
        public float HealRatio { get; }
        public bool AllParty { get; }
        public string IconPath { get; }

        private Sprite _icon;
        public Sprite Icon => _icon != null ? _icon : _icon = Resources.Load<Sprite>(IconPath);

        public string Effect =>
            (AllParty ? "마을의 다친 용병 전원 " : "용병 한 명 ")
            + (HealRatio >= 1f ? "체력 완전 회복" : $"체력 +{Mathf.RoundToInt(HealRatio * 100)}%");

        public HealingItem(string name, int price, float healRatio, bool allParty, int iconNumber)
        {
            Name = name;
            Price = price;
            HealRatio = healRatio;
            AllParty = allParty;
            IconPath = $"HealingItems/item_{iconNumber:00}";
        }

        public bool CanHeal(Mercenary mercenary) =>
            mercenary.IsAlive && mercenary.CurrentHealth < mercenary.CurrentStats.MaxHealth;

        /// <summary>치료하고 실제로 회복한 양을 돌려준다.</summary>
        public int Apply(Mercenary mercenary)
        {
            int max = mercenary.CurrentStats.MaxHealth;
            int before = mercenary.CurrentHealth;
            mercenary.SetHealth(before + Mathf.CeilToInt(max * HealRatio));
            return mercenary.CurrentHealth - before;
        }
    }

    public static class HealingItemCatalog
    {
        /// <summary>시트 순서(왼쪽 위부터 4열×3줄)와 아이콘 번호가 같다.</summary>
        public static readonly IReadOnlyList<HealingItem> All = new List<HealingItem>
        {
            new HealingItem("약초 다발",     20, 0.20f, false, 1),
            new HealingItem("라벤더 다발",   45, 0.10f, true,  2),
            new HealingItem("카모마일 다발", 35, 0.30f, false, 3),
            new HealingItem("인삼 뿌리",    120, 1.00f, false, 4),
            new HealingItem("붉은 치유 물약", 60, 0.50f, false, 5),
            new HealingItem("푸른 정화수",   50, 0.40f, false, 6),
            new HealingItem("초록 해독약",   30, 0.25f, false, 7),
            new HealingItem("황금 영약",    300, 1.00f, true,  8),
            new HealingItem("약초 연고",     40, 0.35f, false, 9),
            new HealingItem("붕대",          10, 0.15f, false, 10),
            new HealingItem("약초 주머니",   80, 0.20f, true,  11),
            new HealingItem("조제 약사발",  110, 0.30f, true,  12),
        };
    }
}
