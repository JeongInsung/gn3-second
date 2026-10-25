using System.Linq;
using GN3.Economy;
using GN3.World;
using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 왼쪽 위 [건설] → 길드 등급별 머리줄 아래 지을 수 있는 건물 카드(그림·이름만). 지금 등급까지만 보인다(다음 등급 것은 숨기고 맨 아래 안내 한 줄).
    /// 카드를 누르면 상세 창(BuildDetailPopup: 비용·재료·막힌 이유·[짓기])이 뜬다. 길드 단계·금화·재료가 모자라면 카드 그림이 흐리고
    /// 상세 창의 [짓기]가 잠긴다. [짓기]를 누르면 창이 닫히고 마을 위에서 자리를 고른다(BuildPlacement).
    /// 재료는 원정 전리품으로만 얻는다(BuildMaterials). 창 머리(X 옆) [옮기기]는 지은 건물 옮기기 모드(BuildingMover).
    /// 목록 맨 위 [건물]·[장식품] 탭: [장식품]은 BuildingCatalog.Decorations를 같은 등급별로 보이고 [놓기]로 몇 개든 놓는다.
    /// 판은 건물 패널(BuildingPanel)과 같은 틀을 하나 더 만들어 쓴다. MainMenuBootstrapper가 만든다.
    /// </summary>
    public static class BuildMenuPanel
    {
        private const string PanelName = "BuildMenuPanel";
        private const float BuildRowHeight = 64f; // 이름 + 짧은 설명·재료 한 줄
        private const float RankRowHeight = 32f;
        // 건물 카드 격자(목록 칸 폭 592 − 여백 안에 5열)
        private static readonly Vector2 CardSize = new Vector2(104f, 118f);
        private const float CardSpacing = 8f;
        private const int CardColumns = 5;
        private const float CardIconSize = 64f;
        private const float BlockedIconAlpha = 0.45f;

        private static readonly Vector2 MoveButtonOffset = new Vector2(-64f, -17f); // X 버튼(40, 오른쪽 위 -14) 왼쪽
        private static readonly Vector2 FellButtonOffset = new Vector2(-162f, -17f); // [옮기기] 왼쪽

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
            // 숲의 나무·덤불·바위를 베어 재료를 얻는 벌목 모드(ForestFelling)
            var fell = BuildingMoveButton.CreateButton(_panel.transform.Find("Box"), FellButtonOffset, () =>
            {
                _panel.Close();
                ForestFelling.Enter();
            }, label: "벌목");
            fell.gameObject.AddComponent<TooltipTrigger>().Text = $"숲의 나무·덤불·바위를 베어 건설 재료를 얻습니다(하루 {ForestLayout.FellsPerDay}번).";
            BuildDetailPopup.Create(_panel.transform.Find("Box"));
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
            BuildDetailPopup.Hide();
            _panel.Show("건설", _decorTab
                ? $"금화와 재료를 들여 마을에 장식품을 놓습니다. 재료는 원정에서 몬스터가 떨어뜨립니다(길드 창고에서 확인). (보유 {Wallet.Gold}G)"
                : $"보유 {Wallet.Gold}G");
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
                if (_decorTab)
                    foreach (var entry in entries) CreateDecorRow(entry);
                else CreateBuildingGrid(rank, entries);
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

        /// <summary>한 등급의 건물 카드 격자: 카드마다 그림 · 이름(주택은 지은 수, 다 지었으면 "완료"). 누르면 상세 창.</summary>
        private static void CreateBuildingGrid(int rank, System.Collections.Generic.List<BuildingCatalog.Entry> entries)
        {
            var row = ShopListUI.CreateRow(_panel, "Buildings_" + rank);
            Object.DestroyImmediate(row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>()); // 같은 오브젝트에 레이아웃 그룹은 하나만
            var grid = row.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = CardSize;
            grid.spacing = new Vector2(CardSpacing, CardSpacing);
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CardColumns;
            row.GetComponent<UnityEngine.UI.Image>().enabled = false; // 줄 바탕 없이 카드만(테마가 칠해도 안 보이게 끈다)
            int rows = (entries.Count + CardColumns - 1) / CardColumns;
            var rowLayout = row.GetComponent<UnityEngine.UI.LayoutElement>();
            rowLayout.minHeight = rowLayout.preferredHeight = rows * CardSize.y + (rows - 1) * CardSpacing + 12f;
            foreach (var entry in entries) CreateBuildingCard(row.transform, entry);
        }

        private static void CreateBuildingCard(Transform parent, BuildingCatalog.Entry entry)
        {
            var slots = VillageLayoutApplier.Slots.Where(s => s.Entry == entry).ToList();
            var spot = slots.FirstOrDefault(s => !s.Built);
            var renderer = (spot ?? slots[0]).Renderer;
            var sprite = renderer != null ? renderer.sprite : null;
            string block = BuildingCatalog.Block(entry, spot != null);
            string status = spot == null ? "완료"
                : entry.House ? $"{slots.Count(s => s.Built)}/{slots.Count}채" : null;

            var card = new GameObject("Card_" + entry.Id, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.VerticalLayoutGroup));
            card.transform.SetParent(parent, false);
            card.GetComponent<UnityEngine.UI.Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // UIThemeApplier가 숯빛 버튼으로 바꾼다
            var v = card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.padding = new RectOffset(4, 4, 8, 6);
            v.spacing = 2f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement));
            icon.transform.SetParent(card.transform, false);
            var image = icon.GetComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            if (block != null) image.color = new Color(1f, 1f, 1f, BlockedIconAlpha); // 지금은 못 짓는 건물은 흐리게
            var iconLayout = icon.GetComponent<UnityEngine.UI.LayoutElement>();
            iconLayout.minHeight = iconLayout.preferredHeight = CardIconSize;

            var name = ShopListUI.CreateText(card.transform, entry.Name, 14, TextAnchor.MiddleCenter, UITheme.BodyText);
            name.raycastTarget = false;
            if (status != null)
                ShopListUI.CreateText(card.transform, status, 12, TextAnchor.MiddleCenter, UITheme.MutedText).raycastTarget = false;

            var target = entry;
            string title = status != null ? $"{entry.Name}  ({status})" : entry.Name;
            var materials = spot != null ? entry.Materials : null; // 다 지었으면 필요한 것 칸 없이
            card.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
                BuildDetailPopup.Show(title, sprite, target.Cost, materials, block, spot == null ? "완료" : "짓기", () =>
                {
                    BuildDetailPopup.Hide();
                    _panel.Close();
                    BuildPlacement.Begin(target);
                }));
        }

        /// <summary>줄 둘째 칸 한 줄: "짧은 설명 · 재료: 천 2/3 …"(설명이 없으면 재료만, 다 지었으면 설명만).</summary>
        private static string Subtitle(BuildingCatalog.Entry entry, bool showMaterials)
        {
            string materials = showMaterials ? BuildMaterials.Text(entry.Materials) : "";
            if (materials.Length > 0) materials = "재료: " + materials;
            string description = entry.Decor ? DecorTags(entry) : entry.Description;
            if (string.IsNullOrEmpty(description)) return materials;
            return materials.Length > 0 ? $"{description} · {materials}" : description;
        }

        /// <summary>장식 기능 태그: "매력 2 · 쉼터 2자리", "매력 2 · 훈련"(VillageCharm·DecorActivities).</summary>
        private static string DecorTags(BuildingCatalog.Entry entry)
        {
            var tags = new System.Collections.Generic.List<string>();
            if (entry.Charm > 0) tags.Add($"매력 {entry.Charm}");
            if (entry.Seats > 0) tags.Add($"쉼터 {entry.Seats}자리");
            if (entry.TrainingDummy) tags.Add("훈련");
            return string.Join(" · ", tags);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _panel = null;
    }
}
