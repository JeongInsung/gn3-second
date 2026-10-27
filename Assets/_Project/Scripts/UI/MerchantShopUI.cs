using GN3.Economy;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 떠돌이 상인 상점 창(TravelingMerchant): 오늘 파는 건설 재료 줄마다 그림 · 이름(보유) · 남은 수량·값 · [사기].
    /// 판은 건물 패널(BuildingPanel)과 같은 틀을 하나 더 만들어 쓴다. MainMenuBootstrapper가 만들고, 마을의 상인을 누르면 연다.
    /// </summary>
    public static class MerchantShopUI
    {
        private static BuildingPanel _panel;

        public static void Create(Transform parent)
        {
            _panel = BuildingPanel.Create(parent);
            _panel.gameObject.name = "MerchantShopPanel";
            TravelingMerchant.OnChanged -= RefreshIfOpen;
            TravelingMerchant.OnChanged += RefreshIfOpen;
        }

        public static void Open()
        {
            if (_panel == null) return;
            if (!TravelingMerchant.IsHere)
            {
                ToastLog.Show("떠돌이 상인은 이미 떠났습니다.", false);
                return;
            }
            Fill();
        }

        private static void RefreshIfOpen()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy && TravelingMerchant.IsHere) Fill();
            else if (_panel != null && !TravelingMerchant.IsHere) _panel.Close();
        }

        private static void Fill()
        {
            _panel.Show("떠돌이 상인", $"보유 {Wallet.Gold}G");
            ShopListUI.BeginList(_panel);
            foreach (var (item, count) in TravelingMerchant.Stock)
            {
                var row = ShopListUI.CreateRow(_panel, "Merchant_" + item.Name);
                if (item.Icon != null) ShopListUI.CreateIcon(row.transform, item.Icon);
                int price = TravelingMerchant.PriceOf(item);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{item.Name}  (보유 {LootBag.Count(item)})", $"남은 {count}개 · 개당 {price}G");
                var target = item;
                var button = ShopListUI.CreateButton(row.transform, "사기", () => TravelingMerchant.Buy(target), 80f);
                string block = count <= 0 ? "다 팔렸습니다." : Wallet.Gold < price ? $"금화 {price}G가 필요합니다." : null;
                button.interactable = block == null;
                button.gameObject.AddComponent<TooltipTrigger>().Text = block ?? $"{item.Name} 1개를 {price}G에 삽니다.";
            }
            ShopListUI.EndList(_panel);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _panel = null;
    }
}
