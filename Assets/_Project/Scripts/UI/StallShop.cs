using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 초반 임시 건물 두 채의 건물 패널을 채운다(2026-10-27).
    ///  행상 좌판: 기초 치료품 몇 가지만 잡화점 값의 PriceMultiplier배로 판다. 잡화점을 지으면 장사를 접는다(좌판은 남는다).
    ///  간이 의무 천막: 지금 효과(자연 회복 ×2인지, 병원이 있어 쉬는지)와 마을의 다친 용병 수를 보여 준다. 효과는 Ailments.AidTentActive.
    /// 사는 방식은 HospitalShop과 같다(바로 쓰기 · 창고로). MainMenuBootstrapper가 만든다.
    /// </summary>
    public class StallShop : MonoBehaviour
    {
        public const string StallName = "행상 좌판";
        public const string AidTentName = "간이 의무 천막";
        public const float PriceMultiplier = 1.5f;
        private const string StoreId = "store";

        /// <summary>좌판에서 파는 기초 치료품(HealingItemCatalog 이름).</summary>
        private static readonly string[] Goods = { "붕대", "약초 다발", "초록 해독약" };

        private BuildingPanel _panel;

        private void Awake() => BuildingPanel.Opened += HandleOpened;
        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null) return;
            _panel = panel;
            if (building.DisplayName.Contains(StallName)) ShowItems();
            else if (building.DisplayName.Contains(AidTentName)) ShowAidTent();
        }

        public static int PriceOf(HealingItem item) => Mathf.RoundToInt(item.Price * PriceMultiplier);

        // ---------- 행상 좌판 ----------

        private void ShowItems()
        {
            ShopListUI.BeginList(_panel);
            if (VillageLayout.HasBuilding(StoreId))
            {
                ShopListUI.CreateEmptyRow(_panel, "잡화점이 생겨 좌판은 문을 닫았다 — 치료품은 잡화점에서 더 싸게 살 수 있습니다.");
                ShopListUI.EndList(_panel);
                return;
            }
            foreach (var item in HealingItemCatalog.All.Where(i => Goods.Contains(i.Name)))
            {
                var row = ShopListUI.CreateRow(_panel, "Stall_" + item.Name);
                ShopListUI.CreateIcon(row.transform, item.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, item.Name, $"{item.Effect} · 잡화점 {item.Price}G");
                ShopListUI.CreatePrice(row.transform, PriceOf(item));
                var picked = item;
                ShopListUI.CreateButton(row.transform, "구매", () =>
                {
                    if (picked.AllParty) BuyForEveryone(picked);
                    else ShowTargets(picked);
                });
                ShopListUI.CreateButton(row.transform, "창고로", () => BuyToStorage(picked));
            }
            ShopListUI.EndList(_panel);
        }

        private void BuyToStorage(HealingItem item)
        {
            int price = PriceOf(item);
            if (GuildStorage.IsFull)
            {
                ToastLog.Show($"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)");
                return;
            }
            if (!Wallet.TrySpend(price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            GuildStorage.TryAdd(item);
            ToastLog.Show($"{Josa.EulReul(item.Name)} 창고에 보관 -{price}G");
        }

        private void BuyForEveryone(HealingItem item)
        {
            int price = PriceOf(item);
            var wounded = ShopListUI.MercsInVillage().Where(item.CanHeal).ToList();
            if (wounded.Count == 0)
            {
                ToastLog.Show("치료할 용병이 없습니다 (마을에 다친 용병 없음)");
                return;
            }
            if (!Wallet.TrySpend(price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            foreach (var merc in wounded) item.Apply(merc);
            ToastLog.Show($"{item.Name}: {wounded.Count}명 회복 (-{price}G)");
        }

        private void ShowTargets(HealingItem item)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{item.Name} ({PriceOf(item)}G) — 누구에게?", ShowItems);

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
            int price = PriceOf(item);
            if (!item.CanHeal(merc))
            {
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 다친 곳이 없습니다.");
                return;
            }
            if (!Wallet.TrySpend(price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            int healed = item.Apply(merc);
            ToastLog.Show($"{merc.Name}에게 {item.Name} 사용 (체력 +{healed}) -{price}G");
            ShowTargets(item); // 대상 목록에 머문다(여러 명을 이어서 치료)
        }

        // ---------- 간이 의무 천막 ----------

        private void ShowAidTent()
        {
            ShopListUI.BeginList(_panel);
            var state = ShopListUI.CreateRow(_panel, "AidTentState");
            if (Ailments.AidTentActive)
                ShopListUI.CreateTwoLineLabel(state.transform, $"자연 회복 ×{Ailments.AidTentMultiplier:0}",
                    $"마을에서 쉬는 다친·병든 용병이 시간당 {Ailments.NaturalRatePerHour * 100f:0}%씩 낫습니다 (종류마다 배율)");
            else
                ShopListUI.CreateTwoLineLabel(state.transform, "쉬는 중", "병원이 생겨 환자는 병원에서 치료받습니다");

            var patients = ShopListUI.MercsInVillage().Where(m => m.HasAilment && !Hospital.IsAdmitted(m)).ToList();
            if (patients.Count == 0) ShopListUI.CreateEmptyRow(_panel, "지금 마을에 다친 용병이 없습니다.");
            foreach (var merc in patients)
            {
                var row = ShopListUI.CreateRow(_panel, "Patient_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, merc.Name,
                    string.Join(" · ", merc.Ailments.Select(a => $"{a.Def.displayName} {Mathf.RoundToInt(a.Progress * 100f)}%")));
            }
            ShopListUI.EndList(_panel);
        }
    }
}
