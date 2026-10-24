using System.Linq;
using GN3.Economy;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 왼쪽 위 [건설] → 길드 등급별 머리줄 아래 지을 수 있는 건물 목록(그림·이름·비용·설명·재료, [짓기]). 지금 등급까지만 보인다(다음 등급 것은 숨기고 맨 아래 안내 한 줄).
    /// 길드 단계·금화·재료가 모자라면 버튼이 잠기고
    /// 툴팁에 이유가 뜬다. [짓기]를 누르면 창이 닫히고 마을 위에서 자리를 고른다(BuildPlacement).
    /// 재료는 원정 전리품으로만 얻는다(BuildMaterials). 창 머리(X 옆) [옮기기]는 지은 건물 옮기기 모드(BuildingMover).
    /// 목록 맨 위 [건물]·[장식품] 탭: [장식품]은 BuildingCatalog.Decorations를 같은 등급별로 보이고 [놓기]로 몇 개든 놓는다.
    /// 판은 건물 패널(BuildingPanel)과 같은 틀을 하나 더 만들어 쓴다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public static class BuildMenuPanel
    {
        private const string PanelName = "BuildMenuPanel";
        private const float BuildRowHeight = 64f; // 이름 + 짧은 설명·재료 한 줄
        private const float RankRowHeight = 32f;

        private static readonly Vector2 MoveButtonOffset = new Vector2(-64f, -17f); // X 버튼(40, 오른쪽 위 -14) 왼쪽

        private static BuildingPanel _panel;
        private static UnityEngine.UI.Button _moveButton;

        public static void Create(Transform parent)
        {
            _panel = BuildingPanel.Create(parent);
            _panel.gameObject.name = PanelName; // 튜토리얼이 이 이름으로 가리킨다
            // 줄마다가 아니라 창 머리에 따로: 모드만 켜고 마을에서 옮길 건물을 고른다
            _moveButton = BuildingMoveButton.CreateButton(_panel.transform.Find("Box"), MoveButtonOffset, () =>
            {
                _panel.Close();
                BuildingMover.Enter();
            });
        }

        /// <summary>지은 건물이 없으면 [옮기기]를 잠근다.</summary>
        private static void RefreshMoveButton()
        {
            if (_moveButton == null) return;
            bool any = VillageLayoutApplier.Slots.Any(s => s.Built && s.Renderer != null && s.Renderer.gameObject.activeInHierarchy);
            _moveButton.interactable = any;
            var tip = _moveButton.GetComponent<TooltipTrigger>() ?? _moveButton.gameObject.AddComponent<TooltipTrigger>();
            tip.Text = any ? "지은 건물을 다른 자리로 옮깁니다(무료). 화면 아래 [옮기기 취소]로 되돌릴 수 있습니다." : "지은 건물이 없습니다.";
        }

        public static void ShowGlobal()
        {
            if (_panel == null) return;
            BuildingMover.Finish(true); // 옮기기 모드였으면 옮긴 그대로 끝낸다
            BuildPlacement.Cancel();
            Fill();
        }

        private static void Fill()
        {
            _panel.Show("건설", $"금화와 재료를 들여 마을에 {(_decorTab ? "장식품을 놓습니다" : "건물을 짓습니다")}. 재료는 원정에서 몬스터가 떨어뜨립니다(길드 창고에서 확인). (보유 {Wallet.Gold}G)");
            ShopListUI.BeginList(_panel);
            RefreshMoveButton();

            // [건물]·[장식품] 탭 — 지금 탭 버튼은 잠가 둔다
            var tabs = ShopListUI.CreateRow(_panel, "Tabs");
            CreateTab(tabs.transform, "건물", false);
            CreateTab(tabs.transform, "장식품", true);

            // 길드 등급별로, 지금 등급까지만: 그 단계에 열리는 것을 머리줄 아래에(다음 등급 것은 숨기고 안내 한 줄만)
            for (int rank = 0; rank <= Guild.Rank; rank++)
            {
                var entries = EntriesFor(rank);
                if (entries.Count == 0) continue;
                CreateRankHeader(rank);
                foreach (var entry in entries)
                    if (_decorTab) CreateDecorRow(entry);
                    else CreateBuildingRow(entry);
            }
            CreateNextRankHint();
            ShopListUI.EndList(_panel);
        }

        /// <summary>지금 탭에서 그 단계에 열리는 건물(마을 프리팹에 그림이 있는 것만) 또는 장식품(분류 순).</summary>
        private static System.Collections.Generic.List<BuildingCatalog.Entry> EntriesFor(int rank)
        {
            if (_decorTab)
                return BuildingCatalog.Decorations.Where(e => e.RequiredRank == rank)
                    .OrderBy(e => System.Array.IndexOf(BuildingCatalog.DecorCategories, e.Category)).ToList();
            return BuildingCatalog.Buildable.Where(e => e.RequiredRank == rank && VillageLayoutApplier.Slots.Any(s => s.Entry == e)).ToList();
        }

        private static void CreateRankHeader(int rank)
        {
            var header = ShopListUI.CreateRow(_panel, "Rank_" + rank);
            var layout = header.GetComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = layout.preferredHeight = RankRowHeight;
            var text = ShopListUI.CreateText(header.transform, Guild.RankName(rank), 16, TextAnchor.MiddleLeft, UITheme.TitleText);
            text.fontStyle = FontStyle.Bold;
        }

        /// <summary>
        /// 맨 아래 흐린 안내: 이 탭에 새것이 열리는 다음 등급("명성 50을 모아 돌빛 용병단이 되면 새 건물이 열립니다").
        /// 무엇이 열리는지는 밝히지 않는다. 길드 홀이 있어야 오르는 등급이면 길드 홀을 짓도록 안내한다.
        /// </summary>
        private static void CreateNextRankHint()
        {
            int next = -1;
            for (int rank = Guild.Rank + 1; rank < Guild.Ranks.Length && next < 0; rank++)
                if (EntriesFor(rank).Count > 0) next = rank;
            if (next < 0) return;
            string what = _decorTab ? "새 장식품" : "새 건물";
            string hint = !Guild.IsGuild && next > Guild.MercenaryBandMaxRank
                ? $"길드 홀을 지어 정식 길드가 되고 {Guild.RankName(next)}(명성 {Guild.Ranks[next].Required})에 오르면 {what}이 열립니다."
                : $"명성 {Guild.Ranks[next].Required}을 모아 {Josa.IGa(Guild.RankName(next))} 되면 {what}이 열립니다.";
            var row = ShopListUI.CreateRow(_panel, "NextRankHint");
            var layout = row.GetComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = layout.preferredHeight = RankRowHeight;
            ShopListUI.CreateText(row.transform, hint, 14, TextAnchor.MiddleLeft, UITheme.MutedText)
                .gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
        }

        private static bool _decorTab; // 마지막에 본 탭(창을 다시 열면 그대로)

        private static void CreateTab(Transform parent, string label, bool decor)
        {
            var button = ShopListUI.CreateButton(parent, label, () =>
            {
                _decorTab = decor;
                Fill();
            }, 140f);
            var text = button.GetComponentInChildren<UnityEngine.UI.Text>();
            if (_decorTab != decor)
            {
                text.color = UITheme.MutedText; // 다른 탭은 흐린 글씨
                return;
            }
            // 지금 탭: 누를 수 없게 하되 흐려지지 않고 금색 굵은 글씨로 밝게
            button.interactable = false;
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            text.color = UITheme.TitleText;
            text.fontStyle = FontStyle.Bold;
        }

        /// <summary>장식 줄: 그림 · 이름·비용·놓은 수 · 설명·재료 · [놓기].</summary>
        private static void CreateDecorRow(BuildingCatalog.Entry entry)
        {
            var row = ShopListUI.CreateRow(_panel, "Decor_" + entry.Id);
            var rowLayout = row.GetComponent<UnityEngine.UI.LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = BuildRowHeight;
            var icon = VillageLayoutApplier.DecorIcon(entry);
            if (icon != null) ShopListUI.CreateIcon(row.transform, icon);

            int placed = VillageLayout.Built.Keys.Count(k => BuildingCatalog.DecorForKey(k) == entry);
            string rowTitle = $"{entry.Name} · {entry.Cost}G" + (placed > 0 ? $"  (놓은 수 {placed})" : "");
            ShopListUI.CreateTwoLineLabel(row.transform, rowTitle, Subtitle(entry, true));

            string block = icon == null ? "그림이 준비되지 않았습니다." : BuildingCatalog.Block(entry, true);
            var target = entry;
            var button = ShopListUI.CreateButton(row.transform, "놓기", () =>
            {
                _panel.Close();
                BuildPlacement.Begin(target);
            }, 80f);
            button.interactable = block == null;
            button.gameObject.AddComponent<TooltipTrigger>().Text = block ?? $"{Josa.EulReul(entry.Name)} 놓을 자리를 고릅니다. 하나 놓으면 같은 것을 계속 놓을 수 있습니다.";
        }

        /// <summary>건물 줄: 그림 · 이름·비용(주택은 지은 수) · 설명·재료 · [짓기]/[완료].</summary>
        private static void CreateBuildingRow(BuildingCatalog.Entry entry)
        {
            var slots = VillageLayoutApplier.Slots.Where(s => s.Entry == entry).ToList();
            var spot = slots.FirstOrDefault(s => !s.Built);
            var row = ShopListUI.CreateRow(_panel, "Build_" + entry.Id);
            var rowLayout = row.GetComponent<UnityEngine.UI.LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = BuildRowHeight;
            var icon = (spot ?? slots[0]).Renderer;
            if (icon != null && icon.sprite != null) ShopListUI.CreateIcon(row.transform, icon.sprite);

            string title = $"{entry.Name} · {entry.Cost}G";
            if (entry.House) title += $"  ({slots.Count(s => s.Built)}/{slots.Count}채)";
            ShopListUI.CreateTwoLineLabel(row.transform, title, Subtitle(entry, spot != null));

            string block = BuildingCatalog.Block(entry, spot != null);
            var target = entry;
            var button = ShopListUI.CreateButton(row.transform, spot == null ? "완료" : "짓기", () =>
            {
                _panel.Close();
                BuildPlacement.Begin(target);
            }, 80f);
            button.interactable = block == null;
            button.gameObject.AddComponent<TooltipTrigger>().Text = block ?? $"{Josa.EulReul(entry.Name)} 지을 자리를 고릅니다.";
        }

        /// <summary>줄 둘째 칸 한 줄: "짧은 설명 · 재료: 천 2/3 …"(설명이 없으면 재료만, 다 지었으면 설명만).</summary>
        private static string Subtitle(BuildingCatalog.Entry entry, bool showMaterials)
        {
            string materials = showMaterials ? BuildMaterials.Text(entry.Materials) : "";
            if (materials.Length > 0) materials = "재료: " + materials;
            if (string.IsNullOrEmpty(entry.Description)) return materials;
            return materials.Length > 0 ? $"{entry.Description} · {materials}" : entry.Description;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _panel = null;
    }
}
