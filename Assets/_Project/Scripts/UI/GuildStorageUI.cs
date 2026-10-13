using System.Linq;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 길드 창고 화면: 길드 창의 "창고" 버튼이 건물 패널을 "길드 창고"로 열면 상품 칸을 창고 목록으로 채운다.
    /// 무기는 마을 용병에게 장착(들고 있던 무기는 창고로), 약은 다친 용병에게 사용, 용병 무기는 맡길 수 있다.
    /// 같은 이름은 한 줄로 묶어 "×개수"로 보인다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public class GuildStorageUI : MonoBehaviour
    {
        private const string PanelKeyword = "창고";

        private BuildingPanel _panel;

        private void Awake() => BuildingPanel.Opened += HandleOpened;
        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(PanelKeyword)) return;
            _panel = panel;
            ShowStorage();
        }

        // ---------- 창고 목록 ----------

        private void ShowStorage()
        {
            ShopListUI.BeginList(_panel);

            var header = ShopListUI.CreateRow(_panel, "StorageHeader");
            var count = ShopListUI.CreateText(header.transform, $"보관 {GuildStorage.Count} / {GuildStorage.Capacity}칸", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            count.fontStyle = FontStyle.Bold;
            count.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            ShopListUI.CreateButton(header.transform, "무기 맡기기", ShowDepositTargets, 120f);

            if (GuildStorage.Count == 0) ShopListUI.CreateEmptyRow(_panel, "창고가 비어 있습니다.");

            foreach (var group in GuildStorage.Weapons.GroupBy(w => w))
            {
                var weapon = group.Key;
                var row = ShopListUI.CreateRow(_panel, "Stored_" + weapon.Name);
                ShopListUI.CreateIcon(row.transform, weapon.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{weapon.Name} ×{group.Count()}", weapon.Summary);
                ShopListUI.CreateButton(row.transform, "장착", () => ShowEquipTargets(weapon));
            }

            foreach (var group in GuildStorage.Items.GroupBy(i => i))
            {
                var item = group.Key;
                var row = ShopListUI.CreateRow(_panel, "Stored_" + item.Name);
                ShopListUI.CreateIcon(row.transform, item.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{item.Name} ×{group.Count()}", item.Effect);
                ShopListUI.CreateButton(row.transform, "사용", () =>
                {
                    if (item.AllParty) UseForEveryone(item);
                    else ShowUseTargets(item);
                });
            }

            ShopListUI.EndList(_panel);
        }

        // ---------- 무기 장착 ----------

        private void ShowEquipTargets(WeaponItem weapon)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{weapon.Name} — 누구에게?", ShowStorage);

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

        private void Equip(WeaponItem weapon, Mercenary merc)
        {
            var old = merc.Weapon;
            GuildStorage.Swap(weapon, old); // 꺼낸 자리에 들고 있던 무기를 넣는다
            merc.Equip(weapon);
            string back = old != null ? $", {old.Name}은(는) 창고로" : "";
            ToastLog.Show($"{merc.Name}이(가) {weapon.Name} 장착 ({weapon.DescribeFor(merc.Class.Kind)}){back}");
            ShowStorage();
        }

        // ---------- 무기 맡기기 ----------

        private void ShowDepositTargets()
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"무기 맡기기 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)", ShowStorage);

            var mercs = ShopListUI.MercsInVillage().Where(m => m.Weapon != null).ToList();
            if (mercs.Count == 0) ShopListUI.CreateEmptyRow(_panel, "무기를 든 마을 용병이 없습니다.");

            foreach (var merc in mercs)
            {
                var row = ShopListUI.CreateRow(_panel, "Deposit_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{merc.Name} ({merc.Class.ClassName})", $"들고 있는 무기: {merc.Weapon.Name}");
                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "맡기기", () => Deposit(target));
                button.interactable = !GuildStorage.IsFull;
            }

            ShopListUI.EndList(_panel);
        }

        private void Deposit(Mercenary merc)
        {
            var weapon = merc.Weapon;
            if (weapon == null) return;
            if (!GuildStorage.TryAdd(weapon))
            {
                ToastLog.Show($"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)");
                return;
            }
            merc.Equip(null);
            ToastLog.Show($"{merc.Name}의 {weapon.Name}을(를) 창고에 맡겼습니다");
            ShowStorage();
        }

        // ---------- 약 사용 ----------

        private void UseForEveryone(HealingItem item)
        {
            var wounded = ShopListUI.MercsInVillage().Where(item.CanHeal).ToList();
            if (wounded.Count == 0)
            {
                ToastLog.Show("치료할 용병이 없습니다 (마을에 다친 용병 없음)");
                return;
            }
            GuildStorage.Remove(item);
            foreach (var merc in wounded) item.Apply(merc);
            ToastLog.Show($"{item.Name}: {wounded.Count}명 회복 (창고)");
            ShowStorage();
        }

        private void ShowUseTargets(HealingItem item)
        {
            ShopListUI.BeginList(_panel);
            ShopListUI.CreateHeader(_panel, $"{item.Name} — 누구에게?", ShowStorage);

            var mercs = ShopListUI.MercsInVillage().ToList();
            if (mercs.Count == 0) ShopListUI.CreateEmptyRow(_panel, "마을에 있는 용병이 없습니다.");

            foreach (var merc in mercs)
            {
                var row = ShopListUI.CreateRow(_panel, "Target_" + merc.Name);
                CharacterPortraitUI.Create(row.transform, merc.Appearance, 40);
                ShopListUI.CreateTwoLineLabel(row.transform, merc.Name, $"체력 {merc.CurrentHealth} / {merc.CurrentStats.MaxHealth}");
                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "사용", () => Use(item, target));
                button.interactable = item.CanHeal(merc);
            }

            ShopListUI.EndList(_panel);
        }

        private void Use(HealingItem item, Mercenary merc)
        {
            if (!item.CanHeal(merc))
            {
                ToastLog.Show($"{merc.Name}은(는) 다친 곳이 없습니다.");
                return;
            }
            if (!GuildStorage.Remove(item)) return;
            int healed = item.Apply(merc);
            ToastLog.Show($"{merc.Name}에게 {item.Name} 사용 (체력 +{healed}, 창고)");
            ShowStorage();
        }
    }
}
