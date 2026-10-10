using System.Collections;
using GN3.Economy;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.Save;
using GN3.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GN3.UI
{
    public static class MainMenuBootstrapper
    {
        private const string TargetSceneName = "MainScene";

        // BattleScene에 다녀오면(SceneManager.LoadScene) MainScene이 런타임에 다시 로드되는데,
        // [RuntimeInitializeOnLoadMethod]는 Play 시작 직후 딱 한 번만 불려서 그때는 메뉴 버튼 등이
        // 다시 안 만들어진다(BattleSceneBootstrapper와 같은 함정). SceneManager.sceneLoaded로 바꿔서
        // MainScene이 로드될 때마다(최초 Play 진입 포함) 다시 짜 넣는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Subscribe()
        {
            _globalHooksInstalled = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == TargetSceneName) Bootstrap();
        }

        // Guild/SaveSystem/ExpeditionLog 같은 전역 싱글턴에 거는 구독·"이어하기" 알림은 Play 세션당 한 번만
        // 걸어야 한다(이 싱글턴들은 MainScene을 몇 번 오가도 파괴되지 않고 그대로 유지되므로, Bootstrap이
        // 다시 불릴 때마다 또 걸면 이벤트가 중복으로 울린다).
        private static bool _globalHooksInstalled;

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
            // ESC로도 닫는다(닫기 버튼과 같은 동작). 위에 뜬 상세 창이 있으면 그것부터 닫힌다.
            foreach (var p in new[] { marketPanel, partyPanel, questPanel })
                EscapeCloser.Register(p, () => p.SetActive(false));
            CreateDayControls(canvas.transform);
            new GameObject("VillageParty", typeof(VillagePartyPresenter)); // 파티 용병들이 마을을 돌아다닌다
            new GameObject("WeatherEffects", typeof(WeatherEffects));     // 오늘 날씨(서울 평년값): 비·눈 파티클, 흐린 날 조명
            ConnectBuildings(canvas.transform, partyPanel, questPanel);
            new GameObject("HospitalShop", typeof(HospitalShop)); // 의약품 상점 패널에 치료 아이템 목록
            new GameObject("WeaponShop", typeof(WeaponShop));     // 대장간 패널에 무기 목록
            new GameObject("TrainingHallUI", typeof(TrainingHallUI)); // 훈련소 패널에 훈련 중·맡길 용병 목록
            new GameObject("HospitalUI", typeof(HospitalUI));         // 병원 패널에 입원 환자·대기 환자 목록
            new GameObject("HotSpringUI", typeof(HotSpringUI));       // 온천 패널에 쉬는 중인 용병 목록
            QuestInfoPanel.Create();                              // 퀘스트 줄 클릭 → 상세 창
            GuildPanel.Create();                                  // 길드 건물·길드 글자 클릭 → 티어·수용 인원 창
            RelationsPanel.Create();                              // 오른쪽 아래 "관계" 버튼 → 용병 친밀도 창
            HealthPanel.Create();                                 // 오른쪽 아래 "건강" 버튼 → 전원 체력·부상·질병·입원 표
            ImportantAlertPanel.Create();                         // 중요 소식(도착 선택·퀘스트 결과 등) → 게임을 멈추고 알림창
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

            Guild.ApplyPartySize(); // 길드 단계 → 파티 정원(매번 다시 맞춰도 안전)

            if (!_globalHooksInstalled)
            {
                _globalHooksInstalled = true;

                // 이동 중 소식은 알림으로. 용병이 죽거나 전멸하면 중요(게임을 멈추고 알림창).
                ExpeditionLog.Instance.OnTravelEvent += line =>
                {
                    bool deadly = line.Contains("사망") || line.Contains("전멸");
                    ToastLog.Show(line, !deadly);
                    if (deadly) Mailbox.Post(MailKind.Alert, line.Contains("전멸") ? "파견대 전멸" : "용병 전사", line, important: true);
                };

                // 길드 단계가 오르면 중요 알림·하루 보고서.
                Guild.OnChanged += rankUp =>
                {
                    if (!rankUp) return;
                    string line = $"길드 등급 상승! {Guild.Current.Name} — {Guild.UnlockText(Guild.Current)}";
                    ToastLog.Show(line, false);
                    DailyLog.Add(line);
                    Mailbox.Post(MailKind.Alert, $"길드 등급 상승! {Guild.Current.Name}", Guild.UnlockText(Guild.Current), important: true);
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
        }

        private const string QuestBoardSpritePrefix = "오크 퀘스트 게시판";
        private const string BlacksmithSpritePrefix = "대장간 연기"; // 대장간 애니메이션 프레임 "대장간 연기_N"
        private const string GuildHallSpritePrefix = "쇠락한 모험가 길드 홀"; // 구운 "…@285x285" 포함
        private const string TrainingHallSpritePrefix = "중세 마을 훈련소 건물";
        private const string HospitalSpritePrefix = "십자가가 돋보이는 중세 픽셀 병원";
        private const string HotSpringSpritePrefix = "청록 지붕의 포렴 찻집"; // 찻집 그림을 온천으로 쓴다

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
            var trainingHall = VillageProps.FindRenderer(TrainingHallSpritePrefix);
            if (trainingHall != null)
            {
                // 훈련소 → 건물 패널(TrainingHallUI가 "훈련소" 이름을 보고 목록을 채운다)
                VillageProps.EnsureClickable(trainingHall)
                    .SetInfo("훈련소", $"용병들이 가끔 스스로 찾아와 {TrainingHall.MinSessionHours:0}~{TrainingHall.MaxSessionHours:0}시간 훈련하며 경험치를 얻습니다. (최대 {TrainingHall.Capacity}명)");
            }
            var hospital = VillageProps.FindRenderer(HospitalSpritePrefix);
            Hospital.Available = hospital != null; // 병원 건물이 있어야 입원한다
            if (hospital != null)
            {
                // 병원 → 건물 패널(HospitalUI가 "병원" 이름을 보고 입원 환자 목록을 채운다)
                VillageProps.EnsureClickable(hospital).SetInfo("병원",
                    $"다치거나 병든 용병이 스스로 입원합니다. 시간당 {Hospital.ProgressPerHour * 100f:0}%씩 나으며, 퇴원할 때 입원 시간당 {Hospital.FeePerHour}G를 냅니다. (최대 {Hospital.Capacity}명)");
                Hospital.AdmitWaiting();
            }

            var hotSpring = VillageProps.FindRenderer(HotSpringSpritePrefix);
            if (hotSpring != null)
            {
                // 온천 → 건물 패널(HotSpringUI가 "온천" 이름을 보고 목록을 채운다)
                VillageProps.EnsureClickable(hotSpring).SetInfo("온천",
                    $"지친 용병들이 가끔 스스로 찾아와 {HotSpring.MinSessionHours:0}~{HotSpring.MaxSessionHours:0}시간 쉬며 피로를 풉니다. 1시간마다 피로 -{HotSpring.FatigueReliefPerHour:0}, 사기 +{HotSpring.MoralePerHour:0}. (최대 {HotSpring.Capacity}명)");
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

        /// <summary>일차·시각 표시 + 오른쪽 위 "진행" 버튼(AdvanceButton, 우클릭 → 며칠 진행). 누르면 GameClock을 3시간 진행시키고 마을 낮/밤도 함께 흐른다
        /// (TimeAdvanceController). 자정을 넘으면 하루가 지나 진행 중인 파견들의 남은 일수가 줄어든다(ExpeditionLog).
        /// MarketPanel/PartyPanel/QuestPanel은 전부 화면 우측에 붙어 있어서(우상단 anchor),
        /// 패널 안쪽 버튼(예: PartyUI의 파견 시작 버튼)과 겹치지 않도록 좌상단 메뉴 버튼바의 [퀘스트] 버튼 오른쪽, 같은 줄에 둔다.</summary>
        private static void CreateDayControls(Transform canvasTransform)
        {
            var barGO = new GameObject("DayControlBar", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter),
                typeof(HorizontalLayoutGroup), typeof(TimeAdvanceController));
            barGO.transform.SetParent(canvasTransform, false);

            // 다른 창과 같은 숯빛 판(청동 테두리)으로 글씨 줄을 감싼다. 판은 클릭을 막지 않는다(툴팁·길드 클릭은 글씨가 받는다).
            var background = barGO.GetComponent<Image>();
            background.sprite = UITheme.Panel;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            var fitter = barGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            float menuBarWidth = MenuButtonCount * MenuButtonWidth + (MenuButtonCount - 1) * MenuSpacing;
            rect.anchoredPosition = new Vector2(MenuBarMargin + menuBarWidth + MenuBarMargin, -MenuBarMargin);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(14, 14, 0, 0); // 위아래 0 → 판 높이 40 = 메뉴 버튼 높이
            layout.childAlignment = TextAnchor.UpperLeft; // 메뉴 버튼(높이 40, 위 맞춤)과 같은 줄에 오도록 위로 붙인다
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // "650년 3월 1일 · 봄 · 오전 6:00 · 맑음 8°C", 마우스를 올리면 "N일차"와 최저·최고 기온(TimeAdvanceController가 갱신)
            var dayTextGO = new GameObject("DayText", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(TooltipTrigger));
            dayTextGO.transform.SetParent(barGO.transform, false);
            var dayTextLayout = dayTextGO.GetComponent<LayoutElement>();
            dayTextLayout.minWidth = 430f;
            dayTextLayout.minHeight = 40f;
            var dayText = dayTextGO.GetComponent<Text>();
            dayText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            dayText.fontSize = 18;
            dayText.alignment = TextAnchor.MiddleLeft;
            dayText.color = Color.white;
            dayText.raycastTarget = true; // 툴팁용

            CreateGoldText(barGO.transform);
            CreateGuildText(barGO.transform);

            // 버튼 클릭이든 스페이스바든 컨트롤러 하나가 시계 진행·낮밤 전환·텍스트 갱신을 맡는다.
            // 진행 버튼은 화면 오른쪽 위에 크게, 우클릭하면 하루·4일·일주일 건너뛰기 메뉴(끝나면 그동안의 보고서가 뜬다).
            var controller = barGO.GetComponent<TimeAdvanceController>();
            var button = AdvanceButton.Create(canvasTransform, controller);
            controller.Init(dayText, button);

            // 가만히 둬도 흐르는 시간의 속도(1배속 ↔ 2배속)
            controller.SetSpeedButton(CreateSpeedButton(canvasTransform, controller)); // 오른쪽 위 저장 버튼 왼쪽

            // 저장(자정마다 자동 저장도 된다): 오른쪽 위 진행 버튼 바로 왼쪽
            CreateSaveButton(canvasTransform);

            // 지나간 알림(왼쪽 아래 토스트) 다시 보기: 화면 오른쪽 아래 우편함 아이콘, 안 읽은 알림이 있으면 "!"
            MailboxButton.Create(canvasTransform);
            RelationsPanel.CreateButton(canvasTransform); // 우편함 왼쪽: 용병 관계(친밀도) 창
            HealthPanel.CreateButton(canvasTransform);    // 관계 버튼 왼쪽: 용병 건강 창
        }

        private static readonly Vector2 SmallButtonSize = new Vector2(70f, 40f); // 배속·저장 버튼(예전 바 안 크기)
        private const float TopRightSpacing = 8f;

        /// <summary>오른쪽 위 작은 버튼의 세로 위치: 진행 버튼 높이의 가운데.</summary>
        private static float SmallButtonTop => -(AdvanceButton.Margin + (AdvanceButton.ButtonSize.y - SmallButtonSize.y) * 0.5f);

        /// <summary>저장 버튼 왼쪽, 세로 가운데를 맞춘 1배속/2배속 토글.</summary>
        private static Button CreateSpeedButton(Transform canvasTransform, TimeAdvanceController controller)
        {
            var go = new GameObject("SpeedButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvasTransform, false);
            go.transform.SetAsFirstSibling();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(
                -(AdvanceButton.Margin + AdvanceButton.ButtonSize.x + TopRightSpacing + SmallButtonSize.x + TopRightSpacing),
                SmallButtonTop);
            rect.sizeDelta = SmallButtonSize;
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            CreateFillText(go.transform, "1배속", 15);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(controller.ToggleSpeed);
            return button;
        }

        /// <summary>진행 버튼(AdvanceButton) 왼쪽, 배속 버튼과 같은 크기·세로 가운데로 붙는 저장 버튼. 진행 버튼처럼 Canvas 맨 뒤라 창이 열리면 그 아래로 깔린다.</summary>
        private static void CreateSaveButton(Transform canvasTransform)
        {
            var go = new GameObject("SaveButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvasTransform, false);
            go.transform.SetAsFirstSibling();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-(AdvanceButton.Margin + AdvanceButton.ButtonSize.x + TopRightSpacing), SmallButtonTop);
            rect.sizeDelta = SmallButtonSize;
            go.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f, 0.9f);
            CreateFillText(go.transform, "저장", 15);
            go.GetComponent<Button>().onClick.AddListener(() =>
                ToastLog.Show(SaveSystem.Save() ? $"저장했습니다 ({GameCalendar.Format(GameClock.CurrentDay)})" : "저장에 실패했습니다"));
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

        /// <summary>보유 골드 표시: [동전 아이콘][숫자]. 바뀔 때마다 숫자를 갱신하고 잠깐 색을 깜빡인다(벌면 초록, 쓰면 빨강).
        /// 아이콘(Resources/UIIcons/gold)을 못 읽으면 예전처럼 "N G" 글자만.</summary>
        private static void CreateGoldText(Transform parent)
        {
            var group = new GameObject("Gold", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            group.transform.SetParent(parent, false);
            var groupLayout = group.GetComponent<HorizontalLayoutGroup>();
            groupLayout.spacing = 4f;
            groupLayout.childAlignment = TextAnchor.MiddleLeft;
            groupLayout.childControlWidth = groupLayout.childControlHeight = true;
            groupLayout.childForceExpandWidth = groupLayout.childForceExpandHeight = false;

            var icon = UIIcons.Load("UIIcons/gold");
            if (icon != null)
            {
                var iconGO = new GameObject("GoldIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                iconGO.transform.SetParent(group.transform, false);
                var iconImage = iconGO.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                var iconLayout = iconGO.GetComponent<LayoutElement>();
                iconLayout.minWidth = iconLayout.preferredWidth = 32f;
                iconLayout.minHeight = iconLayout.preferredHeight = 32f;
            }
            string Format() => icon != null ? $"{Wallet.Gold}" : $"{Wallet.Gold} G";

            var go = new GameObject("GoldText", typeof(RectTransform), typeof(Text), typeof(LayoutElement), typeof(Shadow));
            go.transform.SetParent(group.transform, false);
            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = 80f;
            layout.minHeight = 40f;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = UITheme.TitleText;
            text.text = Format();
            text.raycastTarget = false;

            Coroutine flash = null;
            Wallet.OnChanged += delta =>
            {
                if (text == null) return;
                text.text = Format();
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
            for (float t = 0f; t < Duration && text != null; t += Time.unscaledDeltaTime)
            {
                text.color = Color.Lerp(flashColor, UITheme.TitleText, t / Duration);
                yield return null;
            }
            if (text != null) text.color = UITheme.TitleText;
        }

        // 좌상단 메뉴 버튼바. 날짜·골드·명성 줄(DayControlBar)이 이 값으로 [퀘스트] 버튼 오른쪽 자리를 계산한다.
        private const float MenuBarMargin = 16f;
        private const float MenuButtonWidth = 120f;
        private const float MenuButtonHeight = 40f;
        private const float MenuSpacing = 8f;
        private const int MenuButtonCount = 3;

        private static void CreateMenuBar(Transform canvasTransform, GameObject marketPanel, GameObject partyPanel, GameObject questPanel)
        {
            var barGO = new GameObject("MenuButtonBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGO.transform.SetParent(canvasTransform, false);

            var rect = barGO.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MenuBarMargin, -MenuBarMargin);

            var layout = barGO.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = MenuSpacing;
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
            layoutElement.minWidth = MenuButtonWidth;
            layoutElement.minHeight = MenuButtonHeight;

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
