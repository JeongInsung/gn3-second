using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 대장간 건물 패널의 상품 칸을 무기(WeaponCatalog)로 채운다. 무기를 사서 마을에 있는 용병 한 명에게 쥐여 준다
    /// (들고 있던 무기는 버림). 대상 화면에서 용병마다 "공격 15 → 21(적성)"처럼 바뀔 능력치를 보여 준다.
    /// "창고로"를 누르면 장착하지 않고 길드 창고(GuildStorage)에 넣는다.
    /// 골드가 모자라거나 마을에 용병이 없으면 돈을 받지 않고 알림만 띄운다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public class WeaponShop : MonoBehaviour
    {
        private const string ShopKeyword = "대장간";

        private BuildingPanel _panel;

        private void Awake() => BuildingPanel.Opened += HandleOpened;
        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(ShopKeyword)) return;
            _panel = panel;
            ShowWeapons();
        }

        // ---------- 무기 목록 ----------

        private void ShowWeapons()
        {
            ShopListUI.BeginList(_panel);
            foreach (var weapon in WeaponCatalog.All)
            {
                var row = ShopListUI.CreateRow(_panel, "Weapon_" + weapon.Name);
                ShopListUI.CreateIcon(row.transform, weapon.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, weapon.Name, weapon.Summary);
                ShopListUI.CreatePrice(row.transform, weapon.Price);
                var picked = weapon;
                ShopListUI.CreateButton(row.transform, "구매", () => ShowTargets(picked));
                ShopListUI.CreateButton(row.transform, "창고로", () => BuyToStorage(picked));
            }
            ShopListUI.EndList(_panel);
        }

        // ---------- 누구에게 쥐여 줄지 ----------

        private void ShowTargets(WeaponItem weapon)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{weapon.Name} ({weapon.Price}G) — 누구에게?", ShowWeapons);

            var mercs = ShopListUI.MercsInVillage().ToList();
            if (mercs.Count == 0) ShopListUI.CreateEmptyRow(_panel, "마을에 있는 용병이 없습니다.");

            foreach (var merc in mercs)
            {
                var row = ShopListUI.CreateRow(_panel, "Target_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);

                string current = merc.Weapon != null ? merc.Weapon.Name : "맨손";
                int before = merc.CurrentStats.Attack;
                int after = before - (merc.Weapon?.AttackFor(merc.Class.Kind) ?? 0) + weapon.AttackFor(merc.Class.Kind);
                string fit = weapon.Suits(merc.Class.Kind) ? " (적성)" : "";
                ShopListUI.CreateTwoLineLabel(row.transform,
                    $"{merc.Name} ({merc.Class.ClassName})  현재: {current}",
                    $"이 무기로: 공격 {before} → {after}{fit}");

                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "장착", () => Equip(weapon, target));
                button.interactable = merc.Weapon != weapon;
            }

            ShopListUI.EndList(_panel);
        }

        /// <summary>장착하지 않고 길드 창고에 넣는다. 창고가 가득 차면 돈을 받지 않는다.</summary>
        private void BuyToStorage(WeaponItem weapon)
        {
            if (GuildStorage.IsFull)
            {
                ToastLog.Show($"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)");
                return;
            }
            if (!Wallet.TrySpend(weapon.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {weapon.Price}G, 보유 {Wallet.Gold}G)");
                return;
            }
            GuildStorage.TryAdd(weapon);
            ToastLog.Show($"{weapon.Name}을(를) 창고에 보관 -{weapon.Price}G");
        }

        private void Equip(WeaponItem weapon, Mercenary merc)
        {
            if (!Wallet.TrySpend(weapon.Price))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {weapon.Price}G, 보유 {Wallet.Gold}G)");
                return;
            }
            merc.Equip(weapon);
            ToastLog.Show($"{merc.Name}이(가) {weapon.Name} 장착 ({weapon.DescribeFor(merc.Class.Kind)}) -{weapon.Price}G");
            ShowWeapons();
        }
    }
}
