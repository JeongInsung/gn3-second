using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.Economy
{
    /// <summary>
    /// 용병 가방: 원정에서 전리품을 들고 올 칸 수(칸 = 아이템 1개). 파티 가방 = 그 원정에 간 대원 가방 칸의 합.
    /// 건설 재료는 가방과 따로 실어 와 칸을 안 쓰고(늘 챙긴다), 동전 주머니는 줍는 대로 금화라 칸을 안 쓴다(UsesSlot).
    /// 직접 전투는 뒤진 시체 창에서(BattleUIController), 자동 전투는 값 높은 것부터(ExpeditionBattle) 칸만큼만 가져온다.
    /// 모두 기본 가방(낡은 주머니)으로 시작하고, 의약품 상점에서 더 큰 가방을 사서 바꿔 준다(HospitalShop).
    /// 아이콘은 그림이 없어 기존 전리품 아이콘을 임시로 쓴다(Resources/Loot/loot_NN).
    /// </summary>
    public class BagItem
    {
        public int Tier { get; }       // 저장 번호(MercenarySave.bag) — 0 = 기본
        public string Name { get; }
        public int Slots { get; }
        public int Price { get; }
        public int RequiredRank { get; } // Guild.Ranks 번호
        private readonly int _iconIndex;
        private Sprite _icon;

        public BagItem(int tier, string name, int slots, int price, int requiredRank, int iconIndex)
        {
            Tier = tier;
            Name = name;
            Slots = slots;
            Price = price;
            RequiredRank = requiredRank;
            _iconIndex = iconIndex;
        }

        public Sprite Icon
        {
            get
            {
                if (_icon != null) return _icon;
                string path = $"Loot/loot_{_iconIndex:00}";
                _icon = Resources.Load<Sprite>(path);
                if (_icon == null)
                {
                    var texture = Resources.Load<Texture2D>(path);
                    if (texture != null) _icon = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                }
                return _icon;
            }
        }
    }

    public static class BagCatalog
    {
        public static readonly BagItem Basic = new BagItem(0, "낡은 주머니", 4, 0, 0, 70);

        public static readonly BagItem[] All =
        {
            Basic,
            new BagItem(1, "가죽 가방", 6, 150, 0, 1),
            new BagItem(2, "큰 배낭", 8, 400, 2, 22),
            new BagItem(3, "원정 배낭", 10, 900, 4, 76),
        };

        /// <summary>저장 번호 → 가방(모르는 번호면 기본).</summary>
        public static BagItem ForTier(int tier) => All.FirstOrDefault(b => b.Tier == tier) ?? Basic;

        /// <summary>파견대 가방 빈 칸 합(가방에 남은 전리품만큼 덜 줍는다).</summary>
        public static int CapacityOf(IEnumerable<Mercenaries.Mercenary> members) => members.Sum(m => m.BagFree);

        /// <summary>(아이템, 개수) → 칸 단위 목록(칸을 쓰는 것만, 등급 높은 것·값 높은 것 먼저).</summary>
        public static List<LootItem> Units(IEnumerable<(LootItem item, int count)> loot) =>
            loot.Where(l => UsesSlot(l.item))
                .OrderByDescending(l => l.item.HasGrade ? (int)l.item.Grade : -1).ThenByDescending(l => l.item.Price)
                .SelectMany(l => Enumerable.Repeat(l.item, Mathf.Max(0, l.count))).ToList();

        /// <summary>칸 목록을 대원 순서대로 각자 빈 칸 수만큼 나눈다(앞 대원 가방부터 채움). 넘치는 것은 마지막 대원 몫에 붙인다.</summary>
        public static List<List<LootItem>> Split(IReadOnlyList<LootItem> units, IReadOnlyList<Mercenaries.Mercenary> members)
        {
            var result = new List<List<LootItem>>();
            int start = 0;
            for (int i = 0; i < members.Count; i++)
            {
                int take = members[i].BagFree;
                if (i == members.Count - 1) take = Mathf.Max(take, units.Count - start);
                result.Add(units.Skip(start).Take(take).ToList());
                start += take;
            }
            return result;
        }

        /// <summary>가방 칸을 쓰는 아이템인가: 건설 재료(따로 실어 온다)와 동전 주머니(줍는 대로 금화)는 안 쓴다.</summary>
        public static bool UsesSlot(LootItem item) => item != null && !item.Material && item != LootCatalog.CoinPouch;

        /// <summary>칸을 쓰는 개수(UsesSlot인 것만).</summary>
        public static int SlotsUsed(IEnumerable<(LootItem item, int count)> loot) =>
            loot.Where(l => UsesSlot(l.item)).Sum(l => l.count);

        /// <summary>
        /// 칸 안에 들어가는 만큼만 값 높은 것부터 남긴다(묶음은 개수만큼 나눠 담는다). 칸을 안 쓰는 것(재료·동전 주머니)은 늘 남긴다.
        /// left: 못 담고 두고 온 개수.
        /// </summary>
        public static List<(LootItem item, int count)> Fit(IEnumerable<(LootItem item, int count)> loot, int capacity, out int left)
        {
            var result = new List<(LootItem item, int count)>();
            left = 0;
            int free = Mathf.Max(0, capacity);
            foreach (var (item, count) in loot.OrderByDescending(l => UsesSlot(l.item) ? l.item.Price : int.MaxValue))
            {
                if (!UsesSlot(item)) { result.Add((item, count)); continue; }
                int take = Mathf.Min(count, free);
                if (take > 0) result.Add((item, take));
                free -= take;
                left += count - take;
            }
            return result;
        }
    }
}
