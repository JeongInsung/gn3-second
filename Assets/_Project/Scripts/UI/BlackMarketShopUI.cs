using System.Linq;
using GN3.Economy;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 암거래 상인 창(BlackMarket): 머리줄(안내 · 모두 팔기) → 가진 전리품 줄마다 아이콘(등급 색 테두리) · 이름 ×n 등급 · "정가 → 암거래 값" · [1개 팔기] [모두].
    /// 등급 높은 것부터. 상인의 남은 돈이 모자라면 막는다. 판은 건물 패널(BuildingPanel) 틀을 하나 더 만들어 쓴다(MerchantShopUI와 같은 방식).
    /// MainMenuBootstrapper가 만들고, 마을의 암거래 상인을 누르면 연다.
    /// </summary>
    public static class BlackMarketShopUI
    {
        private static BuildingPanel _panel;

        public static void Create(Transform parent)
        {
            _panel = BuildingPanel.Create(parent);
            _panel.gameObject.name = "BlackMarketPanel";
            BlackMarket.OnChanged -= RefreshIfOpen;
            BlackMarket.OnChanged += RefreshIfOpen;
        }

        public static void Open()
        {
            if (_panel == null) return;
            if (!BlackMarket.IsHere)
            {
                ToastLog.Show("암거래 상인은 이미 떠났습니다.", false);
                return;
            }
            Fill();
        }

        private static void RefreshIfOpen()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy && BlackMarket.IsHere) Fill();
            else if (_panel != null && !BlackMarket.IsHere) _panel.Close();
        }

        private static void Fill()
        {
            _panel.Show("암거래 상인", $"상인의 남은 돈 {BlackMarket.Purse}G · 보유 {Wallet.Gold}G");
            ShopListUI.BeginList(_panel);

            var entries = LootBag.Entries.Where(e => BlackMarket.Buys(e.item))
                .OrderByDescending(e => e.item.Grade).ThenByDescending(e => e.item.Price).ToList();

            var header = ShopListUI.CreateRow(_panel, "BlackMarketHeader");
            ShopListUI.CreateTwoLineLabel(header.transform, "정가보다 비싸게 사 간다",
                "등급이 높을수록 더: 최하급 ×1.2 · 하급 ×1.35 · 중급 ×1.5 · 상급 ×1.75 · 최상급 ×2 (건설 재료는 안 산다)");
            var sellAll = ShopListUI.CreateButton(header.transform, "모두 팔기", () =>
            {
                int gold = BlackMarket.SellAll();
                if (gold > 0) ToastLog.Show($"암거래 상인에게 전리품을 팔았습니다 (+{gold}G)");
            }, 120f);
            sellAll.interactable = entries.Any(e => BlackMarket.PriceOf(e.item) <= BlackMarket.Purse);
            if (!sellAll.interactable)
                sellAll.gameObject.AddComponent<TooltipTrigger>().Text = entries.Count == 0 ? "팔 전리품이 없습니다." : "상인의 남은 돈이 모자랍니다.";

            if (entries.Count == 0) ShopListUI.CreateEmptyRow(_panel, "팔 전리품이 없습니다. 몬스터를 쓰러뜨려 모아 오세요.");

            foreach (var (loot, count) in entries)
            {
                var row = ShopListUI.CreateRow(_panel, "BlackMarket_" + loot.Name);
                ShopListUI.CreateIcon(row.transform, loot.Icon);
                var outline = row.transform.GetChild(row.transform.childCount - 1).gameObject.AddComponent<UnityEngine.UI.Outline>(); // 아이콘 둘레 등급 색
                outline.effectColor = LootGrades.ColorOf(loot.Grade);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                int price = BlackMarket.PriceOf(loot);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{loot.Name} ×{count}  {loot.GradeTag}",
                    $"정가 {loot.Price}G → {price}G (×{BlackMarket.Multiplier(loot.Grade):0.##})");
                var target = loot;
                string block = price > BlackMarket.Purse ? $"상인의 남은 돈이 모자랍니다 (남은 {BlackMarket.Purse}G)" : null;
                var one = ShopListUI.CreateButton(row.transform, "1개 팔기", () => Sell(target, 1), 90f);
                var all = ShopListUI.CreateButton(row.transform, "모두", () => Sell(target, LootBag.Count(target)), 70f);
                one.interactable = all.interactable = block == null;
                if (block != null)
                {
                    one.gameObject.AddComponent<TooltipTrigger>().Text = block;
                    all.gameObject.AddComponent<TooltipTrigger>().Text = block;
                }
            }
            ShopListUI.EndList(_panel);
        }

        private static void Sell(LootItem loot, int n)
        {
            int before = LootBag.Count(loot);
            int gold = BlackMarket.Sell(loot, n);
            if (gold > 0) ToastLog.Show($"{loot.Name} {before - LootBag.Count(loot)}개를 암거래 상인에게 팔았습니다 (+{gold}G)");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _panel = null;
    }
}
