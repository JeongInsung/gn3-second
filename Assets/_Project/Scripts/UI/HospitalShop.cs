using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 의약품 상점 건물 패널의 상품 칸을 치료 아이템(HealingItemCatalog)으로 채운다. 사는 즉시 치료한다.
    /// 한 명용 아이템은 마을에 있는 용병 중 대상을 고르는 화면으로 바뀌고, 전원용은 다친 사람 모두를 치료한다.
    /// "창고로"를 누르면 쓰지 않고 길드 창고(GuildStorage)에 넣는다.
    /// 치료할 사람이 없거나 골드가 모자라면 돈을 받지 않고 알림만 띄운다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public class HospitalShop : MonoBehaviour
    {
        private const string ShopKeyword = "의약품";

        private BuildingPanel _panel;

        private void Awake() => BuildingPanel.Opened += HandleOpened;
        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(ShopKeyword)) return;
            _panel = panel;
            ShowItems();
            FirstTimeTips.Show(FirstTimeTips.Hospital, "의약품 상점",
                "약을 사서 바로 치료하거나 [창고로] 사 두었다가 길드 창고에서 쓸 수 있다.\n" +
                "가방도 여기서 바꿔 준다 — 가방 칸만큼 원정에서 전리품을 들고 온다.\n" +
                "주운 전리품은 가방에 남으니, 길드 창고의 [가방 비우기]로 꺼내 팔자.");
        }

        // ---------- 아이템 목록 ----------

        private void ShowItems()
        {
            ShopListUI.BeginList(_panel);
            foreach (var item in HealingItemCatalog.All)
            {
                var row = ShopListUI.CreateRow(_panel, "Item_" + item.Name);
                ShopListUI.CreateIcon(row.transform, item.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, item.Name, item.Effect);
                ShopListUI.CreatePrice(row.transform, item.Price);
                var picked = item;
                ShopListUI.CreateButton(row.transform, "구매", () =>
                {
                    if (picked.AllParty) BuyForEveryone(picked);
                    else ShowTargets(picked);
                });
                ShopListUI.CreateButton(row.transform, "창고로", () => BuyToStorage(picked));
            }

            // 가방: 원정에서 전리품을 들고 올 칸(BagCatalog). 사면 용병 한 명의 가방을 바꿔 준다.
            var bagHeader = ShopListUI.CreateRow(_panel, "BagHeader");
            ShopListUI.CreateTwoLineLabel(bagHeader.transform, "가방", "원정에서 전리품을 들고 올 칸 수 — 파견대 가방 칸을 합친 만큼만 챙겨 온다(건설 재료는 따로)");
            foreach (var bag in BagCatalog.All.Where(b => b != BagCatalog.Basic))
            {
                var row = ShopListUI.CreateRow(_panel, "Bag_" + bag.Name);
                ShopListUI.CreateIcon(row.transform, bag.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{bag.Name} · {bag.Slots}칸", $"용병 한 명의 가방을 바꿔 준다 (기본 {BagCatalog.Basic.Name} {BagCatalog.Basic.Slots}칸)");
                ShopListUI.CreatePrice(row.transform, bag.Price);
                var picked = bag;
                var button = ShopListUI.CreateButton(row.transform, "구매", () => ShowBagTargets(picked));
                string block = Guild.Rank < bag.RequiredRank ? $"{Guild.RankName(bag.RequiredRank)} 이상이 되어야 살 수 있습니다." : null;
                button.interactable = block == null;
                if (block != null) button.gameObject.AddComponent<TooltipTrigger>().Text = block;
            }
            ShopListUI.EndList(_panel);
        }

        /// <summary>가방을 바꿔 줄 용병 고르기: 지금 가방보다 큰 가방일 때만.</summary>
        private void ShowBagTargets(BagItem bag)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{bag.Name} {bag.Slots}칸 ({bag.Price}G) — 누구에게?", ShowItems);

            var mercs = ShopListUI.MercsInVillage().ToList();
            if (mercs.Count == 0) ShopListUI.CreateEmptyRow(_panel, "마을에 있는 용병이 없습니다.");

            foreach (var merc in mercs)
            {
                var row = ShopListUI.CreateRow(_panel, "BagTarget_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, merc.Name, $"가방: {merc.Bag.Name} ({merc.Bag.Slots}칸)");
                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "바꾸기", () => GiveBag(bag, target));
                bool bigger = bag.Slots > merc.Bag.Slots;
                button.interactable = bigger;
                if (!bigger) button.gameObject.AddComponent<TooltipTrigger>().Text = "이미 같거나 더 큰 가방을 메고 있습니다.";
            }

            ShopListUI.EndList(_panel);
        }

        private void GiveBag(BagItem bag, Mercenary merc)
        {
            if (bag.Slots <= merc.Bag.Slots)
            {
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 이미 같거나 더 큰 가방을 메고 있습니다.");
                return;
            }
            if (!Wallet.TrySpend(bag.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {bag.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            merc.SetBag(bag);
            ToastLog.Show($"{merc.Name}에게 {bag.Name}({bag.Slots}칸)을 메어 주었습니다 -{bag.Price}G");
            ShowItems();
        }

        /// <summary>바로 쓰지 않고 길드 창고에 넣는다(나중에 창고에서 사용). 창고가 가득 차면 돈을 받지 않는다.</summary>
        private void BuyToStorage(HealingItem item)
        {
            if (GuildStorage.IsFull)
            {
                ToastLog.Show($"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)");
                return;
            }
            if (!Wallet.TrySpend(item.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {item.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            GuildStorage.TryAdd(item);
            ToastLog.Show($"{Josa.EulReul(item.Name)} 창고에 보관 -{item.Price}G");
        }

        private void BuyForEveryone(HealingItem item)
        {
            var wounded = ShopListUI.MercsInVillage().Where(item.CanHeal).ToList();
            if (wounded.Count == 0)
            {
                ToastLog.Show("치료할 용병이 없습니다 (마을에 다친 용병 없음)");
                return;
            }
            if (!Wallet.TrySpend(item.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {item.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            foreach (var merc in wounded) item.Apply(merc);
            ToastLog.Show($"{item.Name}: {wounded.Count}명 회복 (-{item.Price}G)");
        }

        // ---------- 대상 고르기 ----------

        private void ShowTargets(HealingItem item)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{item.Name} ({item.Price}G) — 누구에게?", ShowItems);

            var mercs = ShopListUI.MercsInVillage().ToList();
            if (mercs.Count == 0) ShopListUI.CreateEmptyRow(_panel, "마을에 있는 용병이 없습니다.");

            foreach (var merc in mercs)
            {
                var row = ShopListUI.CreateRow(_panel, "Target_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, merc.Name, $"체력 {merc.CurrentHealth} / {merc.CurrentStats.MaxHealth}");
                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "치료", () => Treat(item, target));
                ShopListUI.SetBlocked(button, item.CanHeal(merc) ? null : "이 약으로 나을 곳이 없습니다 (체력 가득·해당 증상 없음).");
            }

            ShopListUI.EndList(_panel);
        }

        private void Treat(HealingItem item, Mercenary merc)
        {
            if (!item.CanHeal(merc))
            {
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 다친 곳이 없습니다.");
                return;
            }
            if (!Wallet.TrySpend(item.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {item.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            int healed = item.Apply(merc);
            ToastLog.Show($"{merc.Name}에게 {item.Name} 사용 (체력 +{healed}) -{item.Price}G");
            ShowTargets(item); // 대상 목록에 머문다(여러 명을 이어서 치료)
        }
    }
}
