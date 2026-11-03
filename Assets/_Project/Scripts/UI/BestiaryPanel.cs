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
    /// 몬스터 도감 창(왼쪽 위 "도감" 버튼): 왼쪽에 은월의 숲 몬스터 목록(그림·이름·처치 수, 휠로 스크롤), 오른쪽에 고른 몬스터의
    /// 큰 그림·사는 곳·설명·특징. 떨어뜨리는 아이템은 [획득한 아이템] 버튼으로 따로 보고, 한 번이라도 얻어 본 것만 알 수 있다(LootBag.HasSeen).
    /// 한 번도 못 쓰러뜨린 몬스터는 검은 실루엣과 "???".
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
        private const float LootIconSize = 40f;                 // 아이템 판 아이콘
        private const float LootPad = 6f, LootSlot = LootIconSize + LootPad * 2f; // 칸 = 아이콘 + 안쪽 여백
        private const float LootSlotGap = 4f, LootRowGap = 10f, LootBorder = 2f;  // 칸 사이(고블린 최상급 8칸이 판 폭 안에) · 줄 사이 · 등급 색 테두리
        private const float LootLabelWidth = 72f;               // 줄 이름(등급) 칸 — "건설 재료"가 한 줄에 들어가게
        private const float LootViewTop = 52f;                  // 아이템 판 머리줄(획득한 아이템 n/m · 구분선) 아래부터 스크롤 칸
        private const float LootContentTop = 6f;                // 스크롤 칸 안 첫 줄 위 여백(등급 테두리가 잘리지 않게)
        private static readonly Color LootSlotColor = new Color(0.058f, 0.058f, 0.072f, 1f); // 아이템 판 칸(불투명: 뒤 등급 색이 테두리로만 보이게)
        private static readonly Color Silhouette = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color RowColor = UITheme.RowFill;
        private static readonly Color SlotColor = new Color(0f, 0f, 0f, 0.28f);   // 그림·아이콘 바닥 칸
        private static readonly Color RuleColor = new Color(1f, 1f, 1f, 0.12f);   // 구분선
        private static readonly Color Gold = new Color(0.94f, 0.75f, 0.38f, 1f);  // 발견 막대 채움
        private const float FrameWidth = 2f;                                     // 고른 줄·탭의 흰 테두리 두께(근처 길드 창과 같음)
        private const float InfoLabelWidth = 120f;
        private const float ButtonWidth = 220f, ButtonHeight = 38f;
        private const string Muted = UITheme.MutedHex;
        private const string Accent = UITheme.SelectedTextHex;

        public static BestiaryPanel Instance { get; private set; }

        private GameObject _panel;
        private Font _font;
        private Sprite _white;
        private Text _found;
        private RectTransform _foundFill;       // 발견 진행 막대
        private readonly List<(Image back, Image art, Text label, GameObject frame)> _rows = new List<(Image, Image, Text, GameObject)>();
        private Image _portrait;
        private Text _name;
        private Text _title;                    // 보스 칭호 줄(없으면 빈칸)
        private Text _infoLabels, _infoValues;  // 처치·가장 강한 개체·사는 곳(라벨 | 값 두 칸)
        private Text _body;
        private RectTransform _listContent;    // 왼쪽 목록 스크롤 내용(줄 수만큼 높이)
        private ScrollRect _listScroll;
        private GameObject _lootButton;         // [획득한 아이템 n/m] — 누르면 상세 자리에 아이템 판
        private Text _lootButtonText;
        private GameObject _lootPanel;
        private RectTransform _lootContent;     // 아이템 판 머리줄 아래 스크롤 칸의 내용(줄 수만큼 높이)
        private ScrollRect _lootScroll;
        private readonly List<GameObject> _lootPanelItems = new List<GameObject>(); // 판을 열 때마다 새로 그린다
        private BestiaryEntry _lootShownFor;    // 판이 열려 있는 몬스터(null이면 닫힘)
        private BestiaryEntry _detailEntry;     // 지금 상세에 보이는 몬스터
        private int _selected;
        private string _region = Bestiary.Forest; // 고른 지역 탭
        private readonly List<(string region, Image back, Text label, GameObject frame)> _tabs = new List<(string, Image, Text, GameObject)>();
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
            LootBag.OnChanged += panel.RefreshIfOpen; // 새 아이템을 얻으면 "획득한 아이템" 수가 바뀐다
            return panel;
        }

        private void OnDestroy()
        {
            Bestiary.OnChanged -= RefreshIfOpen;
            LootBag.OnChanged -= RefreshIfOpen;
        }

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
            float found = Bestiary.All.Length > 0 ? Bestiary.DiscoveredCount / (float)Bestiary.All.Length : 0f;
            _foundFill.sizeDelta = new Vector2(((RectTransform)_foundFill.parent).sizeDelta.x * found, _foundFill.sizeDelta.y);
            foreach (var (region, back, label, frame) in _tabs)
            {
                bool on = region == _region;
                back.color = RowColor;
                frame.SetActive(on);
                string name = on ? $"<color={Accent}><b>{region}</b></color>" : region;
                label.text = $"{name}  <color={Muted}>{Bestiary.DiscoveredIn(region)}/{Bestiary.EntriesIn(region).Length}</color>";
            }

            var all = Bestiary.EntriesIn(_region);
            if (_selected >= all.Length) _selected = 0;
            _listContent.sizeDelta = new Vector2(ListWidth, all.Length * RowHeight);
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].back.gameObject.SetActive(i < all.Length);
                if (i >= all.Length) continue;
                var entry = all[i];
                bool known = Bestiary.IsDiscovered(entry);
                var (back, art, label, frame) = _rows[i];
                back.color = RowColor;
                frame.SetActive(i == _selected);
                SetArt(art, entry, known);
                label.text = known ? $"<b>{entry.Name}</b>\n<size=14><color={Muted}>처치 {Bestiary.Kills(entry)}</color></size>"
                                   : $"<color={Muted}><b>???</b></color>\n<size=14><color={Muted}>아직 만나지 못함</color></size>";
            }
            if (all.Length > 0) ShowDetail(all[_selected]);
        }

        private void ShowDetail(BestiaryEntry entry)
        {
            bool known = Bestiary.IsDiscovered(entry);
            SetArt(_portrait, entry, known);
            _detailEntry = entry;
            if (_lootShownFor != null && (_lootShownFor != entry || !known)) _lootShownFor = null; // 다른 몬스터를 고르면 판을 닫는다
            _lootButton.SetActive(known);

            if (!known)
            {
                _name.text = "???";
                _title.text = "";
                PlaceInfo(false);
                _infoLabels.text = $"<color={Muted}>아직 만나지 못한 몬스터</color>";
                _infoValues.text = "";
                _body.text = $"<color={Muted}>한 마리라도 쓰러뜨리면 기록됩니다.</color>";
                ShowLootPanel(null);
                return;
            }

            _name.text = entry.Name;
            _title.text = entry.Title ?? "";
            PlaceInfo(entry.Title != null);
            var best = Bestiary.BestRank(entry);
            _infoLabels.text = $"<color={Muted}>처치\n가장 강한 개체\n사는 곳</color>";
            _infoValues.text = $"{Bestiary.Kills(entry)}마리\n<color={RankColor(best)}>{MonsterIV.Label(best)}</color>\n{entry.Habitat}";
            _body.text = entry.Description + $"\n\n<color={Accent}><b>특징</b></color>\n• " + string.Join("\n• ", entry.Traits);

            var items = DropsOf(entry);
            _lootButtonText.text = _lootShownFor == entry ? "돌아가기" : $"획득한 아이템 {items.Count(LootBag.HasSeen)}/{items.Count}";
            ShowLootPanel(_lootShownFor);
        }

        /// <summary>그 몬스터에게서 나올 수 있는 것 전부: 전리품(흔한 3·드문 1) · 건설 재료 · 마정석 5색 · 고블린 보물(고블린만).</summary>
        private static List<LootItem> DropsOf(BestiaryEntry entry) =>
            entry.Loot.Concat(LootCatalog.OwnMaterialsFor(entry.Id)).Concat(LootCatalog.ManaStonesFor(entry.Id))
                .Concat(LootCatalog.TreasuresFor(entry.Id)).Concat(LootCatalog.JunkFor(entry.Id)).Distinct().ToList();

        private void ToggleLoot()
        {
            _lootShownFor = _lootShownFor == null ? _detailEntry : null;
            if (_detailEntry != null) ShowDetail(_detailEntry);
        }

        /// <summary>상세 자리를 덮는 아이템 판. 얻어 본 것은 아이콘·이름·값, 못 얻어 본 것은 검은 실루엣과 "???". entry가 null이면 닫는다.</summary>
        private void ShowLootPanel(BestiaryEntry entry)
        {
            foreach (var go in _lootPanelItems) Destroy(go);
            _lootPanelItems.Clear();
            _lootPanel.SetActive(entry != null);
            if (entry == null) return;
            _lootPanel.transform.SetAsLastSibling();
            _lootButton.transform.SetAsLastSibling();

            var drops = DropsOf(entry);
            float panelWidth = ((RectTransform)_lootPanel.transform).sizeDelta.x;
            var head = CreateText("Head", 18, _lootPanel.transform,
                $"<color={Accent}><b>획득한 아이템</b></color>  <color={Muted}>{drops.Count(LootBag.HasSeen)} / {drops.Count}</color>", 16f, -14f, panelWidth - 32f, 26f);
            _lootPanelItems.Add(head.gameObject);
            _lootPanelItems.Add(CreateRule(_lootPanel.transform, 16f, -46f, panelWidth - 32f));
            // 줄은 스크롤 칸 안에(고블린은 잡동사니까지 줄이 많다). 머리줄은 위에 고정.
            var parent = _lootContent;
            float y = -LootContentTop;
            if (entry.LootNote != null)
            {
                _lootPanelItems.Add(CreateText("Note", 15, parent, $"<color={Muted}>{entry.LootNote}</color>", 16f, y, panelWidth - 32f, 22f).gameObject);
                y -= 28f;
            }
            // 전리품·마정석·고블린 보물·잡동사니를 등급별 줄로(높은 등급부터, 있는 등급만), 건설 재료는 맨 아래 따로
            var graded = drops.Where(i => i.HasGrade).OrderByDescending(i => i.Price).ToList();
            foreach (var grade in LootGrades.HighestFirst)
            {
                var items = graded.Where(i => i.Grade == grade).ToList();
                if (items.Count > 0) y = LootRow(parent, LootGrades.NameOf(grade), LootGrades.HexOf(grade), items, y, panelWidth);
            }
            y = LootRow(parent, "건설 재료", Muted, LootCatalog.OwnMaterialsFor(entry.Id), y, panelWidth);
            _lootContent.sizeDelta = new Vector2(_lootContent.sizeDelta.x, -y);
            _lootScroll.verticalNormalizedPosition = 1f; // 열 때마다 맨 위부터
        }

        /// <summary>아이템 판 한 줄: 왼쪽에 줄 이름(등급 색), 오른쪽에 아이콘들(판 폭을 넘으면 다음 줄로 이어서).
        /// 칸 테두리는 등급 색(못 얻은 것은 옅게). 다음 줄의 y를 돌려준다.</summary>
        private float LootRow(Transform parent, string label, string labelHex, IReadOnlyList<LootItem> items, float y, float panelWidth)
        {
            _lootPanelItems.Add(CreateText("Label", 15, parent, $"<color={labelHex}><b>{label}</b></color>", 16f, y - LootSlot / 2f + 11f, LootLabelWidth, 22f).gameObject);
            int perLine = Mathf.Max(1, Mathf.FloorToInt((panelWidth - 16f - LootLabelWidth - 12f + LootSlotGap) / (LootSlot + LootSlotGap)));
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                bool seen = LootBag.HasSeen(item);
                float x = 16f + LootLabelWidth + (i % perLine) * (LootSlot + LootSlotGap);
                float slotY = y - (i / perLine) * (LootSlot + LootSlotGap);
                if (item.HasGrade)
                {
                    var border = CreateBlock(parent, "GradeBorder", x - LootBorder, slotY + LootBorder, LootSlot + LootBorder * 2f, LootSlot + LootBorder * 2f,
                        LootGrades.ColorOf(item.Grade) * new Color(1f, 1f, 1f, seen ? 0.9f : 0.35f));
                    _lootPanelItems.Add(border);
                }
                var back = CreateBlock(parent, "ItemSlot", x, slotY, LootSlot, LootSlot, LootSlotColor);
                _lootPanelItems.Add(back);
                var slot = new GameObject("Item", typeof(RectTransform), typeof(Image), typeof(TooltipTrigger));
                slot.transform.SetParent(parent, false);
                Place(slot.GetComponent<RectTransform>(), x + LootPad, slotY - LootPad, LootIconSize, LootIconSize);
                var image = slot.GetComponent<Image>();
                image.preserveAspect = true;
                image.sprite = item.Icon;
                image.color = seen ? Color.white : Silhouette;
                float chance = LootCatalog.PerKillChance(item);
                slot.GetComponent<TooltipTrigger>().Text = seen
                    ? $"<b>{item.Name}</b>" + (item.HasGrade ? $" <color={LootGrades.HexOf(item.Grade)}>{item.GradeTag}</color>" : "")
                      + $"\n팔면 개당 {item.Price}G" + (chance > 0f ? $"\n한 마리당 {chance * 100:0.##}%" : "")
                    : $"<b>???</b>\n<color={Muted}>아직 얻지 못한 아이템</color>";
                _lootPanelItems.Add(slot);
            }
            int lines = Mathf.Max(1, (items.Count + perLine - 1) / perLine);
            return y - lines * LootSlot - (lines - 1) * LootSlotGap - LootRowGap;
        }

        /// <summary>정보 두 칸(라벨 | 값)을 칭호 줄이 있으면 그만큼 내려 놓는다.</summary>
        private void PlaceInfo(bool hasTitle)
        {
            float top = ListTop + (hasTitle ? 66f : 44f);
            _infoLabels.rectTransform.anchoredPosition = new Vector2(_infoLabels.rectTransform.anchoredPosition.x, -top);
            _infoValues.rectTransform.anchoredPosition = new Vector2(_infoValues.rectTransform.anchoredPosition.x, -top);
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
                var frames = BattleMonsterSprite.SheetFrames(entry.Sheet) ?? BattleMonsterSprite.SheetFrames(entry.FallbackSheet); // 그림이 아직 없으면 대신 쓸 시트
                sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            }
            _art[entry] = sprite;
            return sprite;
        }

        private void SelectRegion(string region)
        {
            _region = region;
            _selected = 0;
            _listScroll.verticalNormalizedPosition = 1f; // 목록 맨 위부터
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
            // 발견 수 + 가는 진행 막대(닫기 버튼 왼쪽)
            const float barWidth = 160f, barRight = 48f;
            _found = CreateText("Found", 16, _panel.transform, "", Width - barRight - barWidth - 12f - 200f, -20f, 200f, 24f);
            _found.alignment = TextAnchor.UpperRight;
            _found.color = UITheme.MutedText;
            var bar = CreateBlock(_panel.transform, "FoundBar", Width - barRight - barWidth, -29f, barWidth, 6f, new Color(0f, 0f, 0f, 0.45f));
            _foundFill = CreateBlock(bar.transform, "Fill", 0f, 0f, 0f, 6f, Gold).GetComponent<RectTransform>();

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
                _tabs.Add((region, tabBack, tabLabel, HiddenFrame(tab.transform)));
            }

            // 왼쪽 목록: 창 아래까지의 스크롤 칸(휠·끌기). 가장 많은 지역의 몬스터 수만큼 줄을 만들어 두고, 고른 지역보다 많은 줄은 숨긴다.
            var view = new GameObject("ListView", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            view.transform.SetParent(_panel.transform, false);
            Place(view.GetComponent<RectTransform>(), ListX, -ListTop, ListWidth, Height - ListTop - 20f);
            var viewImage = view.GetComponent<Image>();
            viewImage.sprite = _white;
            viewImage.color = new Color(0f, 0f, 0f, 0f); // 보이지 않지만 휠·끌기를 받는다
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(view.transform, false);
            _listContent = content.GetComponent<RectTransform>();
            Place(_listContent, 0f, 0f, ListWidth, 0f);
            _listScroll = view.GetComponent<ScrollRect>();
            _listScroll.content = _listContent;
            _listScroll.horizontal = false;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            SmoothWheelScroll.Attach(_listScroll);
            ScrollListWrapper.AddOverlayScrollbar(_listScroll); // 공통 스크롤바(내용이 짧으면 숨음)

            int maxRows = regions.Max(r => Bestiary.EntriesIn(r).Length);
            for (int i = 0; i < maxRows; i++)
            {
                int index = i;
                var row = new GameObject("Row" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                row.transform.SetParent(_listContent, false);
                Place(row.GetComponent<RectTransform>(), 0f, -(i * RowHeight), ListWidth, RowHeight - 6f);
                var back = row.GetComponent<Image>();
                back.sprite = _white;
                back.color = RowColor;
                row.GetComponent<Button>().onClick.AddListener(() => Select(index));

                CreateBlock(row.transform, "ArtSlot", 8f, -4f, 64f, 64f, SlotColor); // 그림이 놓이는 어두운 칸
                var art = CreateArt(row.transform, "Portrait", 8f, -4f, 64f);
                var label = CreateText("Label", 17, row.transform, "", 84f, -14f, ListWidth - 92f, RowHeight - 20f);
                label.lineSpacing = 1.1f;
                _rows.Add((back, art, label, HiddenFrame(row.transform)));
            }

            // 오른쪽 상세 머리: 큰 그림(흐린 테두리) · 이름 · 칭호 · 정보(라벨 | 값)
            var frame = CreateBlock(_panel.transform, "PortraitFrame", DetailX, -ListTop, PortraitBox, PortraitBox, SlotColor);
            UITheme.CreateSelectFrame(frame.transform, 1f, RuleColor); // 그림 상자의 흐린 테두리
            _portrait = CreateArt(frame.transform, "Portrait", 6f, -6f, PortraitBox - 12f);

            float infoX = DetailX + PortraitBox + 20f, infoWidth = Width - infoX - 24f;
            _name = CreateText("Name", 24, _panel.transform, "", infoX, -ListTop, infoWidth, 32f);
            _name.fontStyle = FontStyle.Bold;
            _name.color = UITheme.TitleText;
            _title = CreateText("Subtitle", 16, _panel.transform, "", infoX, -(ListTop + 34f), infoWidth, 22f);
            _title.color = new Color(0.94f, 0.82f, 0.55f, 1f);
            _infoLabels = CreateText("InfoLabels", 16, _panel.transform, "", infoX, -(ListTop + 44f), InfoLabelWidth, 120f);
            _infoValues = CreateText("InfoValues", 16, _panel.transform, "", infoX + InfoLabelWidth, -(ListTop + 44f), infoWidth - InfoLabelWidth, 120f);
            _infoLabels.lineSpacing = _infoValues.lineSpacing = 1.35f;

            // 구분선 · 본문(설명 + 특징) · 구분선 · [획득한 아이템]
            float detailWidth = Width - DetailX - 24f;
            float bodyTop = ListTop + PortraitBox + 28f;
            float buttonTop = Height - 18f - ButtonHeight;
            CreateRule(_panel.transform, DetailX, -(ListTop + PortraitBox + 14f), detailWidth);
            _body = CreateText("Body", 16, _panel.transform, "", DetailX, -bodyTop, detailWidth, buttonTop - 14f - bodyTop);
            _body.lineSpacing = 1.15f;
            CreateRule(_panel.transform, DetailX, -(buttonTop - 12f), detailWidth);

            // 획득한 아이템: 상세 오른쪽 아래 버튼 → 상세 자리를 덮는 판(ShowLootPanel)
            _lootPanel = new GameObject("LootPanel", typeof(RectTransform), typeof(Image));
            _lootPanel.transform.SetParent(_panel.transform, false);
            Place(_lootPanel.GetComponent<RectTransform>(), DetailX - 8f, -(ListTop - 6f), Width - DetailX - 16f, Height - ListTop - ButtonHeight - 30f);
            var lootBack = _lootPanel.GetComponent<Image>();
            lootBack.sprite = _white;
            lootBack.color = new Color(0.08f, 0.08f, 0.1f, 1f); // 상세 글자를 가린다

            // 머리줄 아래 스크롤 칸(휠·끌기) — 왼쪽 목록과 같은 방식
            var lootSize = _lootPanel.GetComponent<RectTransform>().sizeDelta;
            var lootView = new GameObject("LootView", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            lootView.transform.SetParent(_lootPanel.transform, false);
            Place(lootView.GetComponent<RectTransform>(), 0f, -LootViewTop, lootSize.x, lootSize.y - LootViewTop - 4f);
            var lootViewImage = lootView.GetComponent<Image>();
            lootViewImage.sprite = _white;
            lootViewImage.color = new Color(0f, 0f, 0f, 0f); // 보이지 않지만 휠·끌기를 받는다
            var lootContent = new GameObject("Content", typeof(RectTransform));
            lootContent.transform.SetParent(lootView.transform, false);
            _lootContent = lootContent.GetComponent<RectTransform>();
            Place(_lootContent, 0f, 0f, lootSize.x, 0f);
            _lootScroll = lootView.GetComponent<ScrollRect>();
            _lootScroll.content = _lootContent;
            _lootScroll.horizontal = false;
            _lootScroll.movementType = ScrollRect.MovementType.Clamped;
            SmoothWheelScroll.Attach(_lootScroll);
            ScrollListWrapper.AddOverlayScrollbar(_lootScroll); // 공통 스크롤바(내용이 짧으면 숨음)
            _lootPanel.SetActive(false);

            _lootButton = new GameObject("LootButton", typeof(RectTransform), typeof(Image), typeof(Button));
            _lootButton.transform.SetParent(_panel.transform, false);
            Place(_lootButton.GetComponent<RectTransform>(), Width - 24f - ButtonWidth, -buttonTop, ButtonWidth, ButtonHeight);
            _lootButton.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 1f); // 테마 진홍 버튼
            _lootButton.GetComponent<Button>().onClick.AddListener(ToggleLoot);
            _lootButtonText = CreateText("Label", 16, _lootButton.transform, "획득한 아이템", 0f, 0f, 0f, 0f);
            _lootButtonText.alignment = TextAnchor.MiddleCenter;
            var lootLabelRect = _lootButtonText.rectTransform;
            lootLabelRect.anchorMin = Vector2.zero;
            lootLabelRect.anchorMax = Vector2.one;
            lootLabelRect.offsetMin = lootLabelRect.offsetMax = Vector2.zero;

            // 닫기
            UITheme.CreateCloseX(_panel.transform, Hide); // 공통 닫기 X(32px, 오른쪽 위)

            _panel.SetActive(false);
            EscapeCloser.Register(_panel, Hide);
        }

        /// <summary>단색 사각형(바닥 칸·막대). 자기 그림(_white)이라 테마가 색을 바꾸지 않는다. 누름은 부모로 넘긴다.</summary>
        private GameObject CreateBlock(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x, y, width, height);
            var image = go.GetComponent<Image>();
            image.sprite = _white;
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        /// <summary>가로 구분선(1px, 흐린 흰색).</summary>
        private GameObject CreateRule(Transform parent, float x, float y, float width) =>
            CreateBlock(parent, "Rule", x, y, width, 1f, RuleColor);

        /// <summary>고른 줄·탭에만 켜는 흰 테두리(UITheme.CreateSelectFrame — 처음엔 꺼 둔다).</summary>
        private static GameObject HiddenFrame(Transform parent)
        {
            var frame = UITheme.CreateSelectFrame(parent, FrameWidth);
            frame.SetActive(false);
            return frame;
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
