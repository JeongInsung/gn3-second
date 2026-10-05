using System.Collections;
using GN3.Economy;
using GN3.Quests;
using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class MainMenuBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var marketPanel = GameObject.Find("MarketPanel");
            var partyPanel = GameObject.Find("PartyPanel");
            var questPanel = GameObject.Find("QuestPanel");
            var canvas = Object.FindFirstObjectByType<Canvas>();

            if (canvas == null || marketPanel == null || partyPanel == null || questPanel == null)
            {
                Debug.LogWarning("[MainMenuBootstrapper] Canvas/MarketPanel/PartyPanel/QuestPanel를 찾지 못해 메뉴 버튼을 생성하지 못했습니다.");
                return;
            }

            PanelActivator.RegisterGroup(marketPanel, partyPanel, questPanel);
            // 창은 빈 곳·제목을 잡고 끌어 옮길 수 있다.
            DraggablePanel.Attach((RectTransform)marketPanel.transform);
            DraggablePanel.Attach((RectTransform)partyPanel.transform);
            DraggablePanel.Attach((RectTransform)questPanel.transform);

            CreateMenuBar(canvas.transform, marketPanel, partyPanel, questPanel);
            CreateCloseButton(marketPanel.transform, marketPanel);
            CreateCloseButton(partyPanel.transform, partyPanel);
            CreateCloseButton(questPanel.transform, questPanel);
            CreateDayControls(canvas.transform);
            new GameObject("VillageParty", typeof(VillagePartyPresenter)); // 파티 용병들이 마을을 돌아다닌다
            ConnectBuildings(canvas.transform, partyPanel, questPanel);
            new GameObject("HospitalShop", typeof(HospitalShop)); // 의약품 상점 패널에 치료 아이템 목록
            new GameObject("WeaponShop", typeof(WeaponShop));     // 대장간 패널에 무기 목록
            QuestInfoPanel.Create();                              // 퀘스트 줄 클릭 → 상세 창
            GuildPanel.Create();                                  // 길드 건물·길드 글자 클릭 → 티어·수용 인원 창
            ArrivalPrompt.Create(partyPanel);                     // 목적지 도착 → 퀘스트마다 "자동 진행 / 직접 진행"
            ExpeditionLog.Instance.OnTravelEvent += ToastLog.Show; // 이동 중 습격 소식도 알림으로
            var mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.GetComponent<CameraZoom>() == null)
                mainCamera.gameObject.AddComponent<CameraZoom>(); // 마우스 휠 줌
            new GameObject("UITheme", typeof(UIThemeApplier)); // 숯빛 판·청동 테두리·진홍 버튼 테마를 모든 UI에 자동으로 입힌다
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            new GameObject("DebugHotkeys", typeof(DebugHotkeys)); // F9: 마을 용병 체력 -30%(치료 아이템 시험용)
#endif

            marketPanel.SetActive(false);
            partyPanel.SetActive(false);
            questPanel.SetActive(false);

            // 길드 단계 → 파티 정원. 단계가 오르면 알림·하루 보고서.
            Guild.ApplyPartySize();
            Guild.OnChanged += rankUp =>
            {
                if (!rankUp) return;
                string line = $"길드 등급 상승! {Guild.Current.Name} — {Guild.UnlockText(Guild.Current)}";
                ToastLog.Show(line);
                DailyLog.Add(line);
            };

            // 불러오면 시계·낮밤·시장·게시판을 새 상태에 맞춘다.
            SaveSystem.OnLoaded += () =>
            {
                Object.FindFirstObjectByType<TimeAdvanceController>()?.SyncToClock();
                Object.FindFirstObjectByType<MercenaryMarketUI>(FindObjectsInactive.Include)?.RefreshFromOutside();
                Object.FindFirstObjectByType<QuestBoardUI>(FindObjectsInactive.Include)?.RefreshFromOutside();
            };
            if (SaveSystem.HasSave) ContinuePrompt.Create(); // 저장이 있으면 "이어하기 / 새로 시작"
        }

        private const string QuestBoardSpritePrefix = "오크 퀘스트 게시판";
        private const string BlacksmithSpritePrefix = "대장간 연기"; // 대장간 애니메이션 프레임 "대장간 연기_N"
        private const string GuildHallSpritePrefix = "쇠락한 모험가 길드 홀"; // 구운 "…@285x285" 포함

        /// <summary>
        /// 마을 건물 클릭 연결(클릭 기능이 없는 물체는 실행 중에 붙인다).
        /// 여관 → "파티 구성"과 같은 파티 패널, 퀘스트 게시판 → "퀘스트"와 같은 퀘스트 패널, 나머지 건물 → 건물 패널.
        /// </summary>
        private static void ConnectBuildings(Transform canvasTransform, GameObject partyPanel, GameObject questPanel)
        {
            var inn = VillageInn.FindRenderer();
            if (inn != null) VillageInn.EnsureClickable(inn);
            var board = VillageProps.FindRenderer(QuestBoardSpritePrefix);
            if (board != null) VillageProps.EnsureClickable(board);
            var guildHall = VillageProps.FindRenderer(GuildHallSpritePrefix);
            if (guildHall != null) VillageProps.EnsureClickable(guildHall);
            var blacksmith = VillageProps.FindRenderer(BlacksmithSpritePrefix);
            if (blacksmith != null)
            {
                // 대장간 → 건물 패널(무기상점, WeaponShop이 "대장간" 이름을 보고 상품 칸을 채운다)
                var smithy = VillageProps.EnsureClickable(blacksmith);
                smithy.SetInfo("대장간", "무기를 사서 용병에게 쥐여 줄 수 있습니다. 맞는 클래스가 들면 공격 보너스 ×1.5.");
            }

            var buildingPanel = BuildingPanel.Create(canvasTransform);
            foreach (var building in Object.FindObjectsByType<SelectableBuilding>(FindObjectsSortMode.None))
            {
                if (VillageInn.IsInn(building)) building.onClicked.AddListener(_ => OpenPanel(partyPanel));
                else if (VillageProps.IsProp(building, QuestBoardSpritePrefix)) building.onClicked.AddListener(_ => OpenPanel(questPanel));
                else if (VillageProps.IsProp(building, GuildHallSpritePrefix)) building.onClicked.AddListener(_ => GuildPanel.ShowGlobal());
                else building.onClicked.AddListener(buildingPanel.Open);
            }
        }

        /// <summary>일차·시각 표시 + "진행" 버튼. 누르면 GameClock을 3시간 진행시키고 마을 낮/밤도 함께 흐른다
        /// (TimeAdvanceController). 자정을 넘으면 하루가 지나 진행 중인 파견들의 남은 일수가 줄어든다(ExpeditionLog).
        /// MarketPanel/PartyPanel/QuestPanel은 전부 화면 우측에 붙어 있어서(우상단 anchor),
        /// 패널 안쪽 버튼(예: PartyUI의 파견 시작 버튼)과 겹치지 않도록 좌상단 메뉴 버튼바 바로 아래에 둔다.</summary>
        private static void CreateDayControls(Transform canvasTransform)
        {
            var barGO = new GameObject("DayControlBar", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(TimeAdvanceController));
            barGO.transform.SetParent(canvasTransform, false);

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -64f);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var dayTextGO = new GameObject("DayText", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            dayTextGO.transform.SetParent(barGO.transform, false);
            var dayTextLayout = dayTextGO.GetComponent<LayoutElement>();
            dayTextLayout.minWidth = 170f;
            dayTextLayout.minHeight = 40f;
            var dayText = dayTextGO.GetComponent<Text>();
            dayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            dayText.fontSize = 18;
            dayText.alignment = TextAnchor.MiddleLeft;
            dayText.color = Color.white;

            CreateGoldText(barGO.transform);
            CreateGuildText(barGO.transform);

            var buttonGO = new GameObject("AdvanceDayButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(barGO.transform, false);
            buttonGO.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.5f, 0.9f);
            var buttonLayout = buttonGO.GetComponent<LayoutElement>();
            buttonLayout.minWidth = 120f;
            buttonLayout.minHeight = 40f;
            CreateFillText(buttonGO.transform, "진행", 16);

            // 버튼 클릭이든 스페이스바든 컨트롤러 하나가 시계 진행·낮밤 전환·텍스트 갱신을 맡는다.
            var controller = barGO.GetComponent<TimeAdvanceController>();
            var button = buttonGO.GetComponent<Button>();
            controller.Init(dayText, button);
            button.onClick.AddListener(controller.RequestAdvance);

            // 하루·4일·일주일 건너뛰기(끝나면 그동안의 보고서가 뜬다)
            var skipDay = CreateSkipButton(barGO.transform, "하루", () => controller.RequestSkipDays(1));
            var skipFour = CreateSkipButton(barGO.transform, "4일", () => controller.RequestSkipDays(4));
            var skipWeek = CreateSkipButton(barGO.transform, "일주일", () => controller.RequestSkipDays(7));
            controller.SetSkipButtons(skipDay, skipFour, skipWeek);

            // 저장(자정마다 자동 저장도 된다)
            CreateSkipButton(barGO.transform, "저장", () =>
                ToastLog.Show(SaveSystem.Save() ? $"저장했습니다 ({GameClock.CurrentDay}일차)" : "저장에 실패했습니다"));
        }

        private static Button CreateSkipButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "SkipButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = 70f;
            layout.minHeight = 40f;
            CreateFillText(go.transform, label, 15);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>보유 골드 표시. 바뀔 때마다 숫자를 갱신하고 잠깐 색을 깜빡인다(벌면 초록, 쓰면 빨강).</summary>
        private static void CreateGoldText(Transform parent)
        {
            var go = new GameObject("GoldText", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = 110f;
            layout.minHeight = 40f;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = UITheme.TitleText;
            text.text = $"{Wallet.Gold} G";
            text.raycastTarget = false;

            Coroutine flash = null;
            Wallet.OnChanged += delta =>
            {
                if (text == null) return;
                text.text = $"{Wallet.Gold} G";
                if (flash != null) CoroutineHost.Instance.StopCoroutine(flash);
                flash = CoroutineHost.Instance.StartCoroutine(FlashGold(text, delta > 0 ? new Color(0.5f, 0.9f, 0.45f) : new Color(1f, 0.4f, 0.35f)));
            };
        }

        /// <summary>"견습 길드 · 명성 40". 마우스를 올리면 지금 해금 내용과 다음 단계 조건이 툴팁으로 뜬다.</summary>
        private static void CreateGuildText(Transform parent)
        {
            var go = new GameObject("GuildText", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(Shadow), typeof(TooltipTrigger), typeof(Button));
            go.transform.SetParent(parent, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = 190f;
            layout.minHeight = 40f;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = UITheme.BodyText;
            text.raycastTarget = true; // 툴팁용
            var tip = go.GetComponent<TooltipTrigger>();
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(GuildPanel.ShowGlobal); // 클릭 → 길드 창

            void Refresh()
            {
                if (text == null) return;
                text.text = $"{Guild.Current.Name} · 명성 {Guild.Reputation}";
                tip.Text = Guild.Tooltip();
            }
            Refresh();
            Guild.OnChanged += _ => Refresh();
        }

        private static IEnumerator FlashGold(Text text, Color flashColor)
        {
            const float Duration = 0.4f;
            for (float t = 0f; t < Duration && text != null; t += Time.deltaTime)
            {
                text.color = Color.Lerp(flashColor, UITheme.TitleText, t / Duration);
                yield return null;
            }
            if (text != null) text.color = UITheme.TitleText;
        }

        private static void CreateMenuBar(Transform canvasTransform, GameObject marketPanel, GameObject partyPanel, GameObject questPanel)
        {
            var barGO = new GameObject("MenuButtonBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGO.transform.SetParent(canvasTransform, false);

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(16f, -16f);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateMenuButton(barGO.transform, "용병시장", () => OpenPanel(marketPanel));
            CreateMenuButton(barGO.transform, "파티 구성", () => OpenPanel(partyPanel));
            CreateMenuButton(barGO.transform, "퀘스트", () => OpenPanel(questPanel));
        }

        private static void OpenPanel(GameObject panel) => PanelActivator.Open(panel);

        private static void CreateMenuButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGO = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGO.transform.SetParent(parent, false);

            buttonGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);

            var layoutElement = buttonGO.GetComponent<LayoutElement>();
            layoutElement.minWidth = 120f;
            layoutElement.minHeight = 40f;

            CreateFillText(buttonGO.transform, label, 18);
            buttonGO.GetComponent<Button>().onClick.AddListener(onClick);
        }

        private static void CreateCloseButton(Transform panelTransform, GameObject panel)
        {
            var buttonGO = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGO.transform.SetParent(panelTransform, false);

            var rect = buttonGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-8f, -8f);
            rect.sizeDelta = new Vector2(32f, 32f);

            buttonGO.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.25f, 1f);

            CreateFillText(buttonGO.transform, "X", 18);
            buttonGO.GetComponent<Button>().onClick.AddListener(() => panel.SetActive(false));
        }

        private static void CreateFillText(Transform parent, string content, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
        }
    }
}
