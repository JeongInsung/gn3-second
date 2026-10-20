using System.Collections.Generic;
using System.Linq;
using GN3.Battle;
using GN3.Combat;
using GN3.Economy;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 몬스터 도감 창(왼쪽 위 "도감" 버튼): 왼쪽에 은월의 숲 몬스터 6종 목록(그림·이름·처치 수), 오른쪽에 고른 몬스터의
    /// 큰 그림·사는 곳·설명·특징·전리품(마우스를 올리면 이름·값). 한 번도 못 쓰러뜨린 몬스터는 검은 실루엣과 "???".
    /// 전투로 처치 수가 바뀌면 열린 채로 다시 그린다. 끌어 옮길 수 있고 X·ESC로 닫는다. HealthPanel과 같은 틀.
    /// </summary>
    public class BestiaryPanel : MonoBehaviour
    {
        private const float Width = 880f;
        private const float Height = 666f; // 앤트킹 특징 5줄이 전리품 줄과 겹치지 않는 높이(+지역 탭 줄)
        private const float TabTop = 58f, TabHeight = 30f, TabWidth = 170f;
        private const float ListX = 20f, ListWidth = 280f, ListTop = 106f, RowHeight = 76f;
        private const float DetailX = 320f;
        private const float PortraitBox = 190f;
        private const float LootIconSize = 52f;
        private static readonly Color Silhouette = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color RowSelected = new Color(0.95f, 0.72f, 0.3f, 0.22f);
        private const string Muted = "#a3937c";
        private const string Rare = "#c8a0ff";

        public static BestiaryPanel Instance { get; private set; }

        private GameObject _panel;
        private Font _font;
        private Sprite _white;
        private Text _found;
        private readonly List<(Image back, Image art, Text label)> _rows = new List<(Image, Image, Text)>();
        private Image _portrait;
        private Text _name;
        private Text _info;
        private Text _body;
        private Text _lootTitle;
        private readonly List<(Image icon, TooltipTrigger tip)> _loot = new List<(Image, TooltipTrigger)>();
        private readonly List<(Image icon, TooltipTrigger tip)> _mana = new List<(Image, TooltipTrigger)>(); // 마정석 5칸(전리품 오른쪽)
        private const float ManaIconSize = 40f;
        private int _selected;
        private string _region = Bestiary.Forest; // 고른 지역 탭
        private readonly List<(string region, Image back, Text label)> _tabs = new List<(string, Image, Text)>();
        private readonly Dictionary<BestiaryEntry, Sprite> _art = new Dictionary<BestiaryEntry, Sprite>();

        public static BestiaryPanel Create()
        {
            var go = new GameObject("BestiaryInfo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(BestiaryPanel));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1; // 메뉴 패널(0) 위
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var panel = go.GetComponent<BestiaryPanel>();
            panel.Build();
            Instance = panel;
            Bestiary.OnChanged += panel.RefreshIfOpen;
            return panel;
        }

        private void OnDestroy() => Bestiary.OnChanged -= RefreshIfOpen;

        public static void ShowGlobal()
        {
            if (Instance != null) Instance.Toggle();
        }

        public void Show()
        {
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            Refresh();
        }

        public void Hide() => _panel.SetActive(false);

        public void Toggle()
        {
            if (_panel.activeSelf) Hide();
            else Show();
        }

        private void RefreshIfOpen()
        {
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        // ---------- 그리기 ----------

        private void Refresh()
        {
            _found.text = $"발견 {Bestiary.DiscoveredCount} / {Bestiary.All.Length}종";
            foreach (var (region, back, label) in _tabs)
            {
                bool on = region == _region;
                back.color = on ? RowSelected : RowColor;
                label.text = $"{(on ? "<b>" : "")}{region}{(on ? "</b>" : "")}  <color={Muted}>{Bestiary.DiscoveredIn(region)}/{Bestiary.EntriesIn(region).Length}</color>";
            }

            var all = Bestiary.EntriesIn(_region);
            if (_selected >= all.Length) _selected = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].back.gameObject.SetActive(i < all.Length);
                if (i >= all.Length) continue;
                var entry = all[i];
                bool known = Bestiary.IsDiscovered(entry);
                var (back, art, label) = _rows[i];
                back.color = i == _selected ? RowSelected : RowColor;
                SetArt(art, entry, known);
                label.text = known ? $"<b>{entry.Name}</b>\n<color={Muted}>처치 {Bestiary.Kills(entry)}</color>"
                                   : $"<b>???</b>\n<color={Muted}>아직 만나지 못함</color>";
            }
            if (all.Length > 0) ShowDetail(all[_selected]);
        }

        private void ShowDetail(BestiaryEntry entry)
        {
            bool known = Bestiary.IsDiscovered(entry);
            SetArt(_portrait, entry, known);

            if (!known)
            {
                _name.text = "???";
                _info.text = $"<color={Muted}>아직 만나지 못한 몬스터</color>";
                _body.text = "한 마리라도 쓰러뜨리면 정보가 기록됩니다.";
                _lootTitle.text = "";
                foreach (var (icon, _) in _loot) icon.gameObject.SetActive(false);
                foreach (var (icon, _) in _mana) icon.gameObject.SetActive(false);
                return;
            }

            _name.text = entry.Title != null ? $"{entry.Name}  <size=16><color=#f0c060>{entry.Title}</color></size>" : entry.Name;
            var best = Bestiary.BestRank(entry);
            _info.text = $"처치 {Bestiary.Kills(entry)}마리\n<color={Muted}>가장 강한 개체</color>  <color={RankColor(best)}>{MonsterIV.Label(best)}</color>" +
                         $"\n<color={Muted}>사는 곳</color>  {entry.Habitat}";
            _body.text = entry.Description + "\n\n<b>특징</b>\n· " + string.Join("\n· ", entry.Traits) +
                         $"\n<color={Muted}>· {Bestiary.IndividualNote}</color>";

            _lootTitle.text = "<b>전리품</b>" + (entry.LootNote != null ? $"  <color={Muted}>{entry.LootNote}</color>" : "") +
                              $"  <color={Muted}>· 마정석 5종</color>";
            for (int i = 0; i < _loot.Count; i++)
            {
                var (icon, tip) = _loot[i];
                bool has = i < entry.Loot.Length;
                icon.gameObject.SetActive(has);
                if (!has) continue;
                var item = entry.Loot[i];
                icon.sprite = item.Icon;
                tip.Text = $"<b>{item.Name}</b>" + (item.Rare ? $" <color={Rare}>(드묾)</color>" : "") + $"\n팔면 개당 {item.Price}G";
            }

            // 마정석 5칸(파·보·초·금·빨): 한 마리마다 따로 굴린다
            var stones = LootCatalog.ManaStonesFor(entry.Id);
            for (int i = 0; i < _mana.Count; i++)
            {
                var (icon, tip) = _mana[i];
                icon.gameObject.SetActive(i < stones.Length);
                if (i >= stones.Length) continue;
                var stone = stones[i];
                icon.sprite = stone.Icon;
                tip.Text = $"<b>{stone.Name}</b>" + (stone.Rare ? $" <color={Rare}>(드묾)</color>" : "") +
                           $"\n팔면 개당 {stone.Price}G\n한 마리당 {LootCatalog.ManaChances[i] * 100:0.#}%";
            }
        }

        /// <summary>개체 등급 글자 색(전투 이름표와 같은 색).</summary>
        public static string RankColor(MonsterRank rank) => rank switch
        {
            MonsterRank.Weak => "#9a9a9a",
            MonsterRank.Strong => "#f0a54a",
            MonsterRank.Alpha => "#ff6a5a",
            _ => "#e8dcc8",
        };

        /// <summary>몬스터 그림(대기 첫 장, 앤트킹은 초상). 못 만난 몬스터는 검은 실루엣.</summary>
        private void SetArt(Image image, BestiaryEntry entry, bool known)
        {
            image.sprite = ArtFor(entry);
            image.color = known ? Color.white : Silhouette;
            image.enabled = image.sprite != null;
        }

        private Sprite ArtFor(BestiaryEntry entry)
        {
            if (_art.TryGetValue(entry, out var cached) && cached != null) return cached;
            Sprite sprite = entry.Portrait ? BattleMonsterSprite.PortraitFor(MonsterKind.Ent) : null;
            if (sprite == null)
            {
                var frames = BattleMonsterSprite.SheetFrames(entry.Sheet);
                sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            }
            _art[entry] = sprite;
            return sprite;
        }

        private void SelectRegion(string region)
        {
            _region = region;
            _selected = 0;
            Refresh();
        }

        private void Select(int index)
        {
            _selected = index;
            Refresh();
        }

        // ---------- 틀 ----------

        private void Build()
        {
            _font = UITheme.Font;
            _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _white.name = "BestiaryWhite"; // 자기 그림으로 보여 테마가 색을 바꾸지 않는다

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var panelRect = _panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(Width, Height); // 화면 가운데
            _panel.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.13f, 0.95f);
            DraggablePanel.Attach(panelRect);

            var title = CreateText("Title", 22, _panel.transform, "몬스터 도감", 20f, -16f, 300f, 32f);
            title.fontStyle = FontStyle.Bold;
            title.color = UITheme.TitleText;
            _found = CreateText("Found", 16, _panel.transform, "", Width - 260f, -22f, 200f, 24f);
            _found.alignment = TextAnchor.UpperRight;
            _found.color = UITheme.MutedText;

            // 지역 탭(지금은 은월의 숲 하나 — 새 지역 몬스터가 생기면 늘어난다)
            var regions = Bestiary.Regions;
            for (int i = 0; i < regions.Length; i++)
            {
                string region = regions[i];
                var tab = new GameObject("Tab_" + region, typeof(RectTransform), typeof(Image), typeof(Button));
                tab.transform.SetParent(_panel.transform, false);
                Place(tab.GetComponent<RectTransform>(), ListX + i * (TabWidth + 8f), -TabTop, TabWidth, TabHeight);
                var tabBack = tab.GetComponent<Image>();
                tabBack.sprite = _white;
                tab.GetComponent<Button>().onClick.AddListener(() => SelectRegion(region));
                var tabLabel = CreateText("Label", 16, tab.transform, region, 0f, 0f, 0f, 0f);
                tabLabel.alignment = TextAnchor.MiddleCenter;
                var tabLabelRect = tabLabel.rectTransform;
                tabLabelRect.anchorMin = Vector2.zero;
                tabLabelRect.anchorMax = Vector2.one;
                tabLabelRect.offsetMin = tabLabelRect.offsetMax = Vector2.zero;
                _tabs.Add((region, tabBack, tabLabel));
            }

            // 왼쪽 목록(가장 많은 지역의 몬스터 수만큼 줄을 만들어 두고, 고른 지역보다 많은 줄은 숨긴다)
            int maxRows = regions.Max(r => Bestiary.EntriesIn(r).Length);
            for (int i = 0; i < maxRows; i++)
            {
                int index = i;
                var row = new GameObject("Row" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                row.transform.SetParent(_panel.transform, false);
                Place(row.GetComponent<RectTransform>(), ListX, -(ListTop + i * RowHeight), ListWidth, RowHeight - 6f);
                var back = row.GetComponent<Image>();
                back.sprite = _white;
                back.color = RowColor;
                row.GetComponent<Button>().onClick.AddListener(() => Select(index));

                var art = CreateArt(row.transform, "Portrait", 8f, -4f, 64f);
                var label = CreateText("Label", 17, row.transform, "", 82f, -12f, ListWidth - 90f, RowHeight - 20f);
                _rows.Add((back, art, label));
            }

            // 오른쪽 상세: 큰 그림 · 이름 · 정보
            var frame = new GameObject("PortraitFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(_panel.transform, false);
            Place(frame.GetComponent<RectTransform>(), DetailX, -ListTop, PortraitBox, PortraitBox);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = _white;
            frameImage.color = new Color(0f, 0f, 0f, 0.35f);
            frameImage.raycastTarget = false;
            _portrait = CreateArt(frame.transform, "Portrait", 6f, -6f, PortraitBox - 12f);

            float infoX = DetailX + PortraitBox + 20f, infoWidth = Width - infoX - 24f;
            _name = CreateText("Name", 24, _panel.transform, "", infoX, -ListTop, infoWidth, 34f);
            _name.fontStyle = FontStyle.Bold;
            _name.color = UITheme.TitleText;
            _info = CreateText("Info", 16, _panel.transform, "", infoX, -(ListTop + 44f), infoWidth, PortraitBox - 44f);
            _info.lineSpacing = 1.2f;
            _body = CreateText("Body", 16, _panel.transform, "", DetailX, -(ListTop + PortraitBox + 16f), Width - DetailX - 24f, 170f);
            _body.lineSpacing = 1.1f;

            // 전리품(아래)
            float lootTop = Height - 24f - LootIconSize - 30f;
            _lootTitle = CreateText("LootTitle", 16, _panel.transform, "", DetailX, -lootTop, Width - DetailX - 24f, 24f);
            for (int i = 0; i < 4; i++)
            {
                var slot = new GameObject("LootSlot" + i, typeof(RectTransform), typeof(Image), typeof(TooltipTrigger));
                slot.transform.SetParent(_panel.transform, false);
                Place(slot.GetComponent<RectTransform>(), DetailX + i * (LootIconSize + 14f), -(lootTop + 28f), LootIconSize, LootIconSize);
                var image = slot.GetComponent<Image>();
                image.preserveAspect = true;
                image.sprite = LootCatalog.All[0].Icon; // 테마가 빈 칸으로 보고 색을 바꾸지 않게 처음부터 그림을 넣어 둔다
                _loot.Add((image, slot.GetComponent<TooltipTrigger>()));
            }
            // 마정석 5칸: 전리품 4칸 오른쪽, 아래쪽 맞춤
            float manaX = DetailX + 4 * (LootIconSize + 14f) + 10f;
            for (int i = 0; i < LootCatalog.ManaColorNames.Length; i++)
            {
                var slot = new GameObject("ManaSlot" + i, typeof(RectTransform), typeof(Image), typeof(TooltipTrigger));
                slot.transform.SetParent(_panel.transform, false);
                Place(slot.GetComponent<RectTransform>(), manaX + i * (ManaIconSize + 8f), -(lootTop + 28f + LootIconSize - ManaIconSize), ManaIconSize, ManaIconSize);
                var image = slot.GetComponent<Image>();
                image.preserveAspect = true;
                image.sprite = LootCatalog.GoblinMana[i].Icon; // 처음부터 그림 — 테마가 건드리지 않게
                _mana.Add((image, slot.GetComponent<TooltipTrigger>()));
            }

            // 닫기
            var close = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_panel.transform, false);
            var closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-8f, -8f);
            closeRect.sizeDelta = new Vector2(28f, 28f);
            close.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);
            close.GetComponent<Button>().onClick.AddListener(Hide);
            var x = CreateText("Text", 16, close.transform, "X", 0f, 0f, 0f, 0f);
            x.alignment = TextAnchor.MiddleCenter;
            var xRect = x.rectTransform;
            xRect.anchorMin = Vector2.zero;
            xRect.anchorMax = Vector2.one;
            xRect.offsetMin = xRect.offsetMax = Vector2.zero;

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        /// <summary>정사각 칸 안의 몬스터 그림. 시트 칸은 발밑 기준이라 가운데·비율 유지로 맞춘다.</summary>
        private Image CreateArt(Transform parent, string name, float x, float y, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); // 이름에 "Portrait" → 테마가 건드리지 않는다
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, size, size);
            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private Text CreateText(string name, int size, Transform parent, string content, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, width, height);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = UITheme.BodyText;
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }
    }
}
