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
            FirstTimeTips.Show(FirstTimeTips.Weapons, "대장간",
                "무기는 용병 한 명이 하나만 든다. 공격·방어·속도 보너스가 능력치에 더해진다.\n" +
                "직업 적성이 맞는 무기는 공격 보너스가 1.5배 — 목록의 \"○○ 적성\"을 보고 고르자.\n" +
                "새 무기를 쥐여 주면 들던 무기는 길드 창고로 간다(창고가 가득이면 버려진다).\n" +
                "몬스터 전리품·마정석으로만 만드는 제작 무기도 있다.");
        }

        // ---------- 무기 목록 ----------

        private void ShowWeapons()
        {
            ShopListUI.BeginList(_panel);
            foreach (var weapon in WeaponCatalog.All.Where(w => !w.CraftOnly))
            {
                var row = ShopListUI.CreateRow(_panel, "Weapon_" + weapon.Name);
                ShopListUI.CreateIcon(row.transform, weapon.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, weapon.Name, weapon.Summary);
                ShopListUI.CreatePrice(row.transform, weapon.Price);
                var picked = weapon;
                ShopListUI.CreateButton(row.transform, "구매", () => ShowTargets(picked));
                ShopListUI.CreateButton(row.transform, "창고로", () => BuyToStorage(picked));
            }
            ShowCrafting();
            ShowManaWeapons();
            ShopListUI.EndList(_panel);
        }

        // ---------- 제작(전리품 → 무기, 창고로) ----------

        private void ShowCrafting()
        {
            var header = ShopListUI.CreateRow(_panel, "CraftHeader");
            var title = ShopListUI.CreateText(header.transform, "제작 — 전리품으로 만들어 창고로", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;

            foreach (var recipe in Crafting.Recipes)
            {
                var weapon = recipe.Result;
                var row = ShopListUI.CreateRow(_panel, "Craft_" + weapon.Name);
                ShopListUI.CreateIcon(row.transform, weapon.Icon);
                // 줄에는 이름·재료만(길면 두 줄로 겹친다), 무기 능력치는 버튼 툴팁에
                string kind = weapon.CraftOnly ? " (제작 전용)" : "";
                ShopListUI.CreateTwoLineLabel(row.transform, $"{weapon.Name}{kind}", recipe.MaterialText);
                var picked = recipe;
                string block = Crafting.Block(recipe);
                var button = ShopListUI.CreateButton(row.transform, "제작", () => Craft(picked));
                button.interactable = block == null;
                button.gameObject.AddComponent<TooltipTrigger>().Text = $"{weapon.Name}: {weapon.Summary}\n" +
                    (block ?? $"재료와 {recipe.Gold}G를 써서 만들어 창고에 넣습니다.");
            }
        }

        // ---------- 마정 무기(마정석 색 = 등급) ----------

        private static int _manaColor; // 고른 색(0 푸른 ~ 4 붉은)

        /// <summary>색 버튼 5개(보유 수) + 고른 색의 직업별 무기 6개.</summary>
        private void ShowManaWeapons()
        {
            var header = ShopListUI.CreateRow(_panel, "ManaHeader");
            var title = ShopListUI.CreateText(header.transform, "마정 무기", 16, TextAnchor.MiddleLeft, UITheme.BodyText);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minWidth = 70f;
            for (int c = 0; c < LootCatalog.ManaColorNames.Length; c++)
            {
                int color = c;
                string mark = c == _manaColor ? "▶" : "";
                var tab = ShopListUI.CreateButton(header.transform, $"{mark}{LootCatalog.ManaColorNames[c]} {LootBag.CountManaColor(c)}", () =>
                {
                    _manaColor = color;
                    ShowWeapons();
                    ScrollToBottom();
                }, 78f);
                tab.gameObject.AddComponent<TooltipTrigger>().Text = $"{LootCatalog.ManaColorNames[c]} 마정석으로 만드는 무기(보유 {LootBag.CountManaColor(c)}개)";
            }

            foreach (var recipe in Crafting.ManaRecipesFor(_manaColor))
            {
                var weapon = recipe.Result;
                var row = ShopListUI.CreateRow(_panel, "Mana_" + weapon.Name);
                ShopListUI.CreateIcon(row.transform, weapon.Icon);
                ShopListUI.CreateTwoLineLabel(row.transform, $"{weapon.Name} ({WeaponItem.ClassLabel(weapon.Affinity.Value)})", recipe.MaterialText);
                var picked = recipe;
                string block = Crafting.Block(recipe);
                var button = ShopListUI.CreateButton(row.transform, "제작", () => { Craft(picked); ScrollToBottom(); });
                button.interactable = block == null;
                button.gameObject.AddComponent<TooltipTrigger>().Text = $"{weapon.Name}: {weapon.Summary}\n" +
                    (block ?? $"마정석과 {recipe.Gold}G를 써서 만들어 창고에 넣습니다.");
            }
        }

        /// <summary>마정 무기는 목록 맨 아래라, 색을 바꾸거나 만든 뒤 그 자리를 보이게 한다.</summary>
        private void ScrollToBottom()
        {
            var scroll = _panel.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            if (scroll == null) return;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        private void Craft(CraftRecipe recipe)
        {
            if (Crafting.Craft(recipe)) ToastLog.Show($"{Josa.EulReul(recipe.Result.Name)} 만들어 창고에 넣었습니다 (-{recipe.Gold}G).", false);
            else ToastLog.Show(Crafting.Block(recipe) ?? "만들 수 없습니다.", false);
            ShowWeapons();
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
                int before = merc.CurrentStats.Offense;
                int after = before - (merc.Weapon?.AttackFor(merc.Class.Kind) ?? 0) + weapon.AttackFor(merc.Class.Kind);
                string fit = weapon.Suits(merc.Class.Kind) ? " (적성)" : "";
                string lost = merc.Weapon != null && GuildStorage.IsFull ? " · 창고 가득 — 들던 무기는 버려짐" : ""; // 들던 무기는 창고로 간다(Equip)
                ShopListUI.CreateTwoLineLabel(row.transform,
                    $"{merc.Name} ({merc.Class.ClassName})  현재: {current}",
                    $"이 무기로: {merc.OffenseLabel} {before} → {after}{fit}{lost}");

                var target = merc;
                var button = ShopListUI.CreateButton(row.transform, "장착", () => Equip(weapon, target));
                ShopListUI.SetBlocked(button, merc.Weapon != weapon ? null : "이미 이 무기를 들고 있습니다.");
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
            if (!Wallet.TrySpend(weapon.Price, "상점 구매"))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {weapon.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            GuildStorage.TryAdd(weapon);
            ToastLog.Show($"{Josa.EulReul(weapon.Name)} 창고에 보관 -{weapon.Price}G");
        }

        private void Equip(WeaponItem weapon, Mercenary merc)
        {
            if (!Wallet.TrySpend(weapon.Price, "상점 구매"))
            {
                ToastLog.Show($"골드가 부족합니다 (필요 {weapon.Price}G, 보유 {Wallet.Gold}G)", false);
                return;
            }
            // 들던 무기는 창고로(가득 차 있으면 버린다 — 대상 목록에 미리 알린다)
            var old = merc.Weapon;
            string back = old == null ? "" : GuildStorage.TryAdd(old) ? $", {Josa.EunNeun(old.Name)} 창고로" : $", 창고가 가득 차 {Josa.EulReul(old.Name)} 버렸습니다";
            merc.Equip(weapon);
            ToastLog.Show($"{Josa.IGa(merc.Name)} {weapon.Name} 장착 ({weapon.DescribeFor(merc.Class.Kind)}) -{weapon.Price}G{back}");
            ShowWeapons();
        }
    }
}
