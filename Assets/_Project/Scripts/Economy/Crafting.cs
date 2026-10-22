using System.Collections.Generic;
using System.Linq;

namespace GN3.Economy
{
    /// <summary>대장간 제작법: 전리품 몇 개 + 금화 → 무기(창고로).</summary>
    public class CraftRecipe
    {
        public WeaponItem Result { get; }
        public IReadOnlyList<(LootItem item, int count)> Materials { get; }
        public int Gold { get; }
        /// <summary>마정석(색 0 푸른 ~ 4 붉은, 개수). 몬스터 상관없이 색만 맞으면 된다. 없으면 count 0.</summary>
        public (int color, int count) Stones { get; }

        public CraftRecipe(WeaponItem result, int gold, params (LootItem item, int count)[] materials)
        {
            Result = result;
            Gold = gold;
            Materials = materials;
        }

        /// <summary>마정 무기: 마정석 + 금화.</summary>
        public CraftRecipe(WeaponItem result, int gold, (int color, int count) stones)
        {
            Result = result;
            Gold = gold;
            Materials = new (LootItem, int)[0];
            Stones = stones;
        }

        public bool HasMaterials => Materials.All(m => LootBag.Count(m.item) >= m.count)
                                    && (Stones.count <= 0 || LootBag.CountManaColor(Stones.color) >= Stones.count);

        /// <summary>"낡은 검 2/3 · 가죽 팔보호대 1/2 · 40G" / "푸른 마정석 2/3 · 50G" (보유/필요)</summary>
        public string MaterialText
        {
            get
            {
                var parts = Materials.Select(m => $"{m.item.Name} {LootBag.Count(m.item)}/{m.count}").ToList();
                if (Stones.count > 0) parts.Add($"{LootCatalog.ManaColorNames[Stones.color]} 마정석 {LootBag.CountManaColor(Stones.color)}/{Stones.count}");
                parts.Add($"{Gold}G");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>
    /// 대장간 제작(WeaponShop의 "제작" 구역). 상점 무기를 전리품으로 싸게 만들거나, 드문 전리품으로 제작 전용 무기를 만든다.
    /// 마정석은 마정 무기(직업별 × 색 5등급)를 만드는 데 쓴다(ManaRecipes).
    /// 만든 무기는 길드 창고로 간다(창고가 가득 차면 막는다). 재료는 LootBag에서 빠진다.
    /// </summary>
    public static class Crafting
    {
        public static readonly CraftRecipe[] Recipes =
        {
            new CraftRecipe(WeaponCatalog.Find("롱소드"), 40, (LootCatalog.OldSword, 3), (LootCatalog.LeatherBracer, 2)),
            new CraftRecipe(WeaponCatalog.Find("단검"), 20, (LootCatalog.RiderDagger, 3)),
            new CraftRecipe(WeaponCatalog.Find("창"), 45, (LootCatalog.WoodenShield, 2), (LootCatalog.Fang, 3)),
            new CraftRecipe(WeaponCatalog.Find("장궁"), 50, (LootCatalog.ShortBow, 2), (LootCatalog.ArcherArrows, 3)),
            new CraftRecipe(WeaponCatalog.Find("수정 지팡이"), 60, (LootCatalog.SporeSac, 3), (LootCatalog.LifeSeed, 1)),
            new CraftRecipe(WeaponCatalog.WolfKingDagger, 80, (LootCatalog.ManaFang, 1), (LootCatalog.WolfBridle, 1), (LootCatalog.Fang, 4)),
            new CraftRecipe(WeaponCatalog.MyceliumStaff, 100, (LootCatalog.MyceliumCrystal, 2), (LootCatalog.Venom, 3)),
            new CraftRecipe(WeaponCatalog.EntHeartBlade, 150, (LootCatalog.EntKingHeart, 1), (LootCatalog.RootThorn, 3), (LootCatalog.MossyBark, 2)),
        };

        // 마정 무기 제작법: 색마다 마정석 개수·금화(푸른 3개 50G … 붉은 1개 500G)
        public static readonly int[] ManaStoneCounts = { 3, 3, 2, 1, 1 };
        public static readonly int[] ManaGold = { 50, 100, 200, 300, 500 };

        /// <summary>마정 무기 30개의 제작법: [줄 × 5 + 색] (WeaponCatalog.ManaWeapons와 같은 순서).</summary>
        public static readonly CraftRecipe[] ManaRecipes = Enumerable.Range(0, WeaponCatalog.ManaWeapons.Length)
            .Select(i => new CraftRecipe(WeaponCatalog.ManaWeapons[i], ManaGold[i % 5], (i % 5, ManaStoneCounts[i % 5])))
            .ToArray();

        /// <summary>이 색 마정석으로 만드는 직업별 무기 6개.</summary>
        public static IEnumerable<CraftRecipe> ManaRecipesFor(int color) =>
            Enumerable.Range(0, WeaponCatalog.ManaLineCount).Select(line => ManaRecipes[line * 5 + color]);

        /// <summary>만들 수 없으면 이유, 되면 null.</summary>
        public static string Block(CraftRecipe recipe)
        {
            if (!recipe.HasMaterials) return "재료가 모자랍니다.";
            if (Wallet.Gold < recipe.Gold) return $"금화 {recipe.Gold}G가 필요합니다.";
            if (GuildStorage.IsFull) return $"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸).";
            return null;
        }

        /// <summary>재료·금화를 쓰고 무기를 창고에 넣는다.</summary>
        public static bool Craft(CraftRecipe recipe)
        {
            if (Block(recipe) != null || !Wallet.TrySpend(recipe.Gold)) return false;
            foreach (var (item, count) in recipe.Materials) LootBag.Consume(item, count);
            if (recipe.Stones.count > 0) LootBag.ConsumeManaColor(recipe.Stones.color, recipe.Stones.count);
            GuildStorage.TryAdd(recipe.Result);
            return true;
        }
    }
}
