using System.Collections.Generic;
using System.Linq;

namespace GN3.Economy
{
    /// <summary>
    /// 건설 재료(LootCatalog.Materials): 원정에서 몬스터가 떨어뜨린다(금화로 사서 채울 수는 없다).
    /// 재료는 전리품 가방(LootBag)에 같이 쌓이고 저장도 같이 된다. 건물마다 드는 재료는 BuildingCatalog.Entry.Materials.
    /// </summary>
    public static class BuildMaterials
    {
        /// <summary>새 게임에 들고 시작하는 재료(길드 천막 한 채 + 조금 남게).</summary>
        public static readonly (LootItem item, int count)[] StartingKit =
        {
            (LootCatalog.Cloth, 4), (LootCatalog.Rope, 3), (LootCatalog.Stake, 6), (LootCatalog.Log, 2),
        };

        public static bool Has(IEnumerable<(LootItem item, int count)> materials) =>
            materials == null || materials.All(m => LootBag.Count(m.item) >= m.count);

        /// <summary>재료를 쓴다. 하나라도 모자라면 아무것도 쓰지 않고 false.</summary>
        public static bool Consume(IReadOnlyList<(LootItem item, int count)> materials)
        {
            if (materials == null) return true;
            if (!Has(materials)) return false;
            foreach (var (item, count) in materials) LootBag.Consume(item, count);
            return true;
        }

        /// <summary>"천 2/3 · 밧줄 1/2" (보유/필요). 재료가 없으면 빈 문자열.</summary>
        public static string Text(IEnumerable<(LootItem item, int count)> materials) =>
            materials == null ? "" : string.Join(" · ", materials.Select(m => $"{m.item.Name} {LootBag.Count(m.item)}/{m.count}"));

        /// <summary>모자란 재료만 "천 2/3 · 밧줄 1/2".</summary>
        public static string MissingText(IEnumerable<(LootItem item, int count)> materials) =>
            materials == null ? "" : Text(materials.Where(m => LootBag.Count(m.item) < m.count));
    }
}
