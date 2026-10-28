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
        private bool _sellAllArmed; // "모두 팔기"를 한 번 눌러 확인을 기다리는 중(창을 다시 열거나 다른 것을 팔면 풀린다)
        private LootItem _armedMaterial; // 건설 재료 [모두]를 한 번 누른 줄

        private void Awake() => BuildingPanel.Opened += HandleOpened;
        private void OnDestroy() => BuildingPanel.Opened -= HandleOpened;

        private static bool _facilitiesNext; // 길드 창 [시설]로 열 때 — 창고 대신 시설 목록

        /// <summary>다음에 길드 건물 패널이 열리면 시설 목록을 보인다(GuildPanel [시설]).</summary>
        public static void RequestFacilities() => _facilitiesNext = true;

        private void HandleOpened(BuildingPanel panel, SelectableBuilding building)
        {
            if (building == null || !building.DisplayName.Contains(PanelKeyword)) return;
            _panel = panel;
            _sellAllArmed = false;
            _armedMaterial = null;
            if (_facilitiesNext)
            {
                _facilitiesNext = false;
                ShowFacilities();
            }
            else
            {
                ShowStorage();
                FirstTimeTips.Show(FirstTimeTips.Storage, "길드 창고",
                    "원정에서 주운 전리품은 용병 가방에 남는다 — [가방 비우기]로 창고에 넣어야 팔거나 제작·건설에 쓸 수 있다.\n" +
                    "가방이 차 있으면 다음 원정에서 그만큼 덜 줍는다.\n" +
                    "[모두 팔기]는 정가다. 암거래 상인이 마을에 오면 더 비싸게 판다.\n" +
                    "건설 재료는 [모두 팔기]에 섞이지 않는다(건설에 쓰인다).");
            }
        }

        // ---------- 길드 시설 ----------

        /// <summary>시설 다섯: 단계(●●○), 지금 효과 → 다음 효과, 비용(보유/필요), [증축].</summary>
        private void ShowFacilities()
        {
            _panel.Title.text = "길드 시설";
            if (_panel.Description != null) _panel.Description.text = "";
            ShopListUI.BeginList(_panel);
            foreach (var f in GuildFacilities.All)
            {
                int level = GuildFacilities.Level(f);
                var row = ShopListUI.CreateRow(_panel, "Facility_" + f);
                string dots = new string('●', level) + new string('○', GuildFacilities.MaxLevel - level);
                string title = $"{GuildFacilities.Name(f)}  {dots}  {GuildFacilities.Effect(f, level)}";
                string sub = level >= GuildFacilities.MaxLevel ? "최고 단계"
                    : $"다음: {GuildFacilities.Effect(f, level + 1)} · {GuildFacilities.CostText(level + 1)}";
                ShopListUI.CreateTwoLineLabel(row.transform, title, sub);
                var facility = f;
                string block = GuildFacilities.Block(f);
                var button = ShopListUI.CreateButton(row.transform, level >= GuildFacilities.MaxLevel ? "완료" : "증축", () => Upgrade(facility), 80f);
                button.interactable = block == null;
                button.gameObject.AddComponent<TooltipTrigger>().Text = block ?? $"{GuildFacilities.Name(f)} {level + 1}단계로 증축합니다.";
            }
            ShopListUI.EndList(_panel);
        }

        private void Upgrade(Facility f)
        {
            if (GuildFacilities.TryUpgrade(f))
                ToastLog.Show($"{Josa.EulReul(GuildFacilities.Name(f))} {GuildFacilities.Level(f)}단계로 증축했습니다 — {GuildFacilities.Effect(f, GuildFacilities.Level(f))}", false);
            ShowFacilities();
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

            ShowLoot();
            ShowMaterials();
            ShopListUI.EndList(_panel);
        }

        // ---------- 전리품 팔기 ----------

        /// <summary>전리품 가방(창고 칸을 쓰지 않음): 머리줄에 모두 팔기, 종류마다 1개 팔기·모두.</summary>
        private void ShowLoot()
        {
            var header = ShopListUI.CreateRow(_panel, "LootHeader");
            var title = ShopListUI.CreateText(header.transform,
                LootBag.KindCount > 0
                    ? $"전리품 ({LootBag.KindCount}종 · 모두 팔면 {LootBag.TotalValue}G" + (LootBag.KeptIngredientKinds > 0 ? $" · 제작 재료 {LootBag.KeptIngredientKinds}종은 남김)" : ")")
                    : "전리품",
                16, TextAnchor.MiddleLeft, UITheme.BodyText);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            // 마을에 있는 용병 가방을 한 번에 비워 창고로(전리품은 원정이 끝나도 가방에 남는다)
            var carriers = ShopListUI.MercsInVillage().Where(m => m.BagContents.Count > 0).ToList();
            int carried = carriers.Sum(m => m.BagContents.Count);
            var stashAll = ShopListUI.CreateButton(header.transform, carried > 0 ? $"가방 비우기 ({carried})" : "가방 비우기", () =>
            {
                _sellAllArmed = false;
                int total = carriers.Sum(LootBag.StashFrom);
                if (total > 0) ToastLog.Show($"가방 {carriers.Count}명에서 전리품 {total}개를 창고에 넣었습니다");
                ShowStorage();
            }, 150f);
            stashAll.interactable = carried > 0;
            stashAll.gameObject.AddComponent<TooltipTrigger>().Text = "마을에 있는 용병들의 가방을 모두 비워 창고에 넣는다 (파견 중인 용병은 빼고)";

            // 모두 팔기: 실수로 귀한 것까지 팔지 않게 두 번 누른다(첫 클릭은 받을 금액을 보여 준다)
            var sellAll = ShopListUI.CreateButton(header.transform, _sellAllArmed ? $"정말? +{LootBag.TotalValue}G" : "모두 팔기", () =>
            {
                if (!_sellAllArmed)
                {
                    _sellAllArmed = true;
                    _armedMaterial = null;
                    ShowStorage();
                    return;
                }
                _sellAllArmed = false;
                int gold = LootBag.SellAll();
                if (gold > 0) ToastLog.Show($"전리품을 모두 팔았습니다 (+{gold}G)");
                ShowStorage();
            }, 140f);
            sellAll.interactable = LootBag.KindCount > 0;
            if (BlackMarket.IsHere && LootBag.KindCount > 0) // 일반 판매는 정가 — 와 있는 날엔 더 비싸게 팔 곳을 알려 준다
                ShopListUI.CreateEmptyRow(_panel, "암거래 상인이 마을에 와 있습니다 — 상인에게 팔면 더 비싸게 받습니다");

            if (LootBag.KindCount == 0)
            {
                ShopListUI.CreateEmptyRow(_panel, "몬스터를 쓰러뜨리면 전리품이 쌓입니다.");
                return;
            }

            // 등급 높은 것부터, 같은 등급은 값 높은 것부터
            var entries = LootBag.Entries.Where(e => !e.item.Material)
                .OrderByDescending(e => e.item.Grade).ThenByDescending(e => e.item.Price).ToList();
            foreach (var (loot, count) in entries)
            {
                var row = ShopListUI.CreateRow(_panel, "Loot_" + loot.Name);
                ShopListUI.CreateIcon(row.transform, loot.Icon);
                var gradeOutline = row.transform.GetChild(row.transform.childCount - 1).gameObject.AddComponent<UnityEngine.UI.Outline>(); // 아이콘 둘레 등급 색
                gradeOutline.effectColor = LootGrades.ColorOf(loot.Grade);
                gradeOutline.effectDistance = new Vector2(1.5f, -1.5f);
                string uses = Crafting.UsesOf(loot); // 제작 재료면 [모두 팔기]에서 빠진다 — 줄에 표시하고 쓰이는 곳은 툴팁
                ShopListUI.CreateTwoLineLabel(row.transform, $"{loot.Name} ×{count}  {loot.GradeTag}", // 이 글자는 색 태그를 그리지 않는다
                    $"개당 {loot.Price}G · 모두 {loot.Price * count}G" + (uses != null ? " · 제작 재료(모두 팔기에서 남김)" : ""));
                if (uses != null) row.AddComponent<TooltipTrigger>().Text = uses;
                ShopListUI.CreateButton(row.transform, "1개 팔기", () => Sell(loot, 1), 90f);
                ShopListUI.CreateButton(row.transform, "모두", () => Sell(loot, LootBag.Count(loot)), 70f);
            }
        }

        /// <summary>건설 재료(전리품 가방에 같이 쌓이지만 "모두 팔기"에는 안 섞인다): 종류마다 1개 팔기.</summary>
        private void ShowMaterials()
        {
            var materials = LootBag.Entries.Where(e => e.item.Material).ToList();
            if (materials.Count == 0) return;
            var header = ShopListUI.CreateRow(_panel, "MaterialHeader");
            var title = ShopListUI.CreateText(header.transform, $"건설 재료 ({materials.Count}종 · [건설]에 씁니다)", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

            foreach (var (item, count) in materials)
            {
                var row = ShopListUI.CreateRow(_panel, "Material_" + item.Name);
                ShopListUI.CreateIcon(row.transform, item.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{item.Name} ×{count}", $"개당 {item.Price}G에 팝니다");
                ShopListUI.CreateButton(row.transform, "1개 팔기", () => Sell(item, 1), 90f);
                // 건설에 쓰는 재료라 한 번에 다 팔지 않게 두 번 누른다
                var material = item;
                bool armed = _armedMaterial == material;
                ShopListUI.CreateButton(row.transform, armed ? "정말?" : "모두", () =>
                {
                    if (!armed) { _armedMaterial = material; _sellAllArmed = false; ShowStorage(); return; }
                    Sell(material, LootBag.Count(material));
                }, 70f);
            }
        }

        private void Sell(LootItem loot, int n)
        {
            _sellAllArmed = false;
            _armedMaterial = null;
            int sold = System.Math.Min(n, LootBag.Count(loot));
            int gold = LootBag.Sell(loot, n);
            if (gold > 0) ToastLog.Show($"{loot.Name} {sold}개를 팔았습니다 (+{gold}G)");
            ShowStorage();
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
                int before = merc.CurrentStats.Offense;
                int after = before - (merc.Weapon?.AttackFor(merc.Class.Kind) ?? 0) + weapon.AttackFor(merc.Class.Kind);
                string fit = weapon.Suits(merc.Class.Kind) ? " (적성)" : "";
                ShopListUI.CreateTwoLineLabel(row.transform,
                    $"{merc.Name} ({merc.Class.ClassName})  현재: {current}",
                    $"이 무기로: {merc.OffenseLabel} {before} → {after}{fit}");

                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "장착", () => Equip(weapon, target));
                ShopListUI.SetBlocked(button, merc.Weapon != weapon ? null : "이미 이 무기를 들고 있습니다.");
            }

            ShopListUI.EndList(_panel);
        }

        private void Equip(WeaponItem weapon, Mercenary merc)
        {
            var old = merc.Weapon;
            GuildStorage.Swap(weapon, old); // 꺼낸 자리에 들고 있던 무기를 넣는다
            merc.Equip(weapon);
            string back = old != null ? $", {Josa.EunNeun(old.Name)} 창고로" : "";
            ToastLog.Show($"{Josa.IGa(merc.Name)} {weapon.Name} 장착 ({weapon.DescribeFor(merc.Class.Kind)}){back}");
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
                ShopListUI.SetBlocked(button, GuildStorage.IsFull ? $"창고가 가득 찼습니다 ({GuildStorage.Count} / {GuildStorage.Capacity}칸)." : null);
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
            ToastLog.Show($"{merc.Name}의 {Josa.EulReul(weapon.Name)} 창고에 맡겼습니다");
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
                ShopListUI.SetBlocked(button, item.CanHeal(merc) ? null : "이 약으로 나을 곳이 없습니다 (체력 가득·해당 증상 없음).");
            }

            ShopListUI.EndList(_panel);
        }

        private void Use(HealingItem item, Mercenary merc)
        {
            if (!item.CanHeal(merc))
            {
                ToastLog.Show($"{Josa.EunNeun(merc.Name)} 다친 곳이 없습니다.");
                return;
            }
            if (!GuildStorage.Remove(item)) return;
            int healed = item.Apply(merc);
            ToastLog.Show($"{merc.Name}에게 {item.Name} 사용 (체력 +{healed}, 창고)");
            // 같은 약이 남았으면 대상 목록에 머문다(여러 명을 이어서 치료)
            if (GuildStorage.Items.Contains(item)) ShowUseTargets(item);
            else ShowStorage();
        }
    }
}
