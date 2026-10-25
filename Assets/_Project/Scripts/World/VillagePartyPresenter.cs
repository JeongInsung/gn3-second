using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.World
{
    /// <summary>
    /// 파티(PlayerParty)에 있는 용병들을 마을(Village 프리팹) 바닥 위에 VillageWanderer로 내보낸다.
    /// 파견 중이거나 죽은 용병은 마을에 없고, 고용·해고·파견·귀환 때마다 목록을 다시 맞춘다.
    /// 마을에 여관이 있으면 용병들은 아침(6~9시)에는 여관에서 쉬다가 무작위로 문에서 나와 돌아다니고 또 무작위로 들어간다.
    /// 9시가 되면 안에 있던 용병이 모두 문에서 차례로 나오고, 자정까지는 아무도 들어가지 않는다.
    /// 자정~아침(어두운 동안)에는 모두 안에서 잔다.
    /// 여관이 없으면 밝을 때 전원이 숨기 전 자리에서 나타난다.
    /// 친밀도(Affinity)가 높은 용병끼리는 가끔 짝을 지어 같이 걷고, 여관·훈련소·쉼터에도 같이 드나든다.
    /// 피로가 쌓이거나 사기가 떨어진 용병은 가끔 스스로 쉼터(온천·도박장, RestVenues)에 들어가 쉬고 나온다.
    /// MainMenuBootstrapper가 MainScene에서 만든다.
    /// </summary>
    public class VillagePartyPresenter : MonoBehaviour
    {
        private const float AreaMargin = 0.4f;
        // 마을 바닥 끝에서 발 위치까지의 여백(유닛, AreaMargin에 더함). 위쪽은 몸(약 0.6) + 이름표만큼 넉넉히.
        private const float ScreenMarginSide = 0.3f;
        private const float ScreenMarginBottom = 0.15f;
        private const float ScreenMarginTop = 0.8f;
        private const float HideNightLightFactor = 0.5f;

        // 용병들이 드나드는 여관 문(여관 그림 기준 비율, 왼쪽 아래 0). 512px 원본에서 아치문 중심 x≈206px, 디딤돌 y≈450px.
        private const float InnDoorU = 0.40f;
        private const float InnDoorV = 0.12f;
        // 여관이 없으면 용병 숙소 천막(갈색 원뿔형 텐트)의 아래 가운데 입구로 드나든다
        private const float CampDoorU = 0.5f;
        private const float CampDoorV = 0.1f;

        // 여관 드나들기(낮에만). 안에 있으면 InsideRest초마다 ComeOutChance 확률로 나오고,
        // 밖에 있으면 OutsideWander초마다 GoInChance 확률로 들어간다.
        private const float InsideRestMin = 8f;
        private const float InsideRestMax = 20f;
        private const float ComeOutChance = 0.2f;
        private const float OutsideWanderMin = 20f;
        private const float OutsideWanderMax = 45f;
        private const float GoInChance = 0.3f;
        private const float DoorGap = 0.6f; // 문에서 두 사람이 겹쳐 나오지 않게
        private const float ShuffleOutsideChance = 0.3f; // 진행으로 시간이 건너뛸 때 밖에 나와 있을 확률(9시 전)
        private const float AllOutHour = 9f;              // 이 시각부터 자정까지는 전원 여관 밖

        private readonly Dictionary<string, VillageWanderer> _wanderers = new Dictionary<string, VillageWanderer>();
        // 건물·출구로 걸어 들어가는 중인 인형(이미 _wanderers에서 빠짐). 도착하면 지운다.
        // 시간을 빨리 넘기면 들어가다 다시 나오는 일이 겹쳐 옛 인형이 쌓였다(일주일 넘기기 한 번에 7명 파티가 66개) → 따로 들고 있다가 정리한다.
        private readonly HashSet<VillageWanderer> _leaving = new HashSet<VillageWanderer>();
        private bool _wasAdvancing;
        private bool _wasSkippingDays;
        private readonly Dictionary<string, Mercenary> _mercById = new Dictionary<string, Mercenary>();
        private readonly System.Random _rng = new System.Random();
        private Rect _area;
        private VillageNavGrid _grid;
        private Material _shadowMaterial;
        private Transform _tagLayer;
        private MercenaryInfoPanel _infoPanel;
        private VillageWanderer _hovered;
        private bool _subscribed;
        private bool _hidden;
        private bool _hasInn;
        private Vector2 _innDoor;
        private Vector2 _innExit;
        // 길드 본부(길드 천막·길드 홀): 여관이 없을 때 3명이 묵는다(GuildHqLodging) — 그 3명은 이 문으로 드나든다
        private bool _hasHq;
        private Vector2 _hqDoor;
        private Vector2 _hqExit;
        private const string GuildHallSpritePrefix = "쇠락한 모험가 길드 홀";
        private const float HqDoorU = 0.5f;
        private const float HqDoorV = 0.1f;
        private Vector2 _villageExit;                                 // 파견대가 드나드는 마을 출구(화면 아래쪽 가운데)
        private readonly HashSet<string> _awayIds = new HashSet<string>(); // 파견 나가 있는 용병(돌아오면 출구에서 걸어 들어온다)
        private readonly HashSet<string> _trainingIds = new HashSet<string>(); // 훈련소에 들어간 용병(훈련을 끝내면 훈련소 문에서 나온다)
        private const string TrainingHallSpritePrefix = "중세 마을 훈련소 건물";
        private bool _hasTrainingHall;
        private Vector2 _trainingDoor;
        private Vector2 _trainingExit;
        private readonly HashSet<string> _hospitalIds = new HashSet<string>(); // 병원에 입원한 용병(퇴원하면 병원 문에서 나온다)
        private const string HospitalSpritePrefix = "십자가가 돋보이는 중세 픽셀 병원";
        private const float HospitalDoorU = 0.43f; // 병원 그림에서 아치 문 가운데(가로 비율)
        private bool _hasHospital;
        private Vector2 _hospitalDoor;
        private Vector2 _hospitalExit;
        // 훈련소 스스로 가기: 밖에 나와 걷는 용병마다 TrainingRoll초마다 TrainingChance 확률로 1~3시간 훈련하러 간다(밤엔 안 감).
        private const float TrainingRollMin = 60f;
        private const float TrainingRollMax = 120f;
        private const float FirstTrainingRollMin = 30f;
        private const float FirstTrainingRollMax = 90f;
        private const float TrainingChance = ConditionTuning.TrainingVisitChance;
        private readonly Dictionary<string, float> _nextTrainingRoll = new Dictionary<string, float>();
        // 쉼터(온천·도박장): 밖에서 걷는 용병마다 RestRoll초마다 쉼터를 무작위 순서로 하나씩 RestVenue.VisitChance 확률로 굴려 처음 걸린 곳에 간다.
        private const float RestRollMin = 40f;
        private const float RestRollMax = 90f;
        private const float FirstRestRollMin = 20f;
        private const float FirstRestRollMax = 60f;
        private readonly Dictionary<RestVenue, (Vector2 door, Vector2 exit)> _venueDoors = new Dictionary<RestVenue, (Vector2, Vector2)>(); // 마을에 그림이 있는 쉼터만
        private readonly Dictionary<string, RestVenue> _restingIn = new Dictionary<string, RestVenue>(); // 쉼터에 들어간 용병(다 쉬면 그 문에서 나온다)
        private readonly Dictionary<string, float> _nextRestRoll = new Dictionary<string, float>();
        // 장식(벤치·모닥불·분수·허수아비 — DecorActivities): 밖에서 걷는 용병마다 DecorRoll초마다 굴려 쉬러·훈련하러 간다.
        private const float DecorRollMin = 40f;
        private const float DecorRollMax = 90f;
        private const float FirstDecorRollMin = 20f;
        private const float FirstDecorRollMax = 60f;
        private const float DummyChance = 0.12f;       // 허수아비(훈련하고 싶을 때)
        private const float TiredRestChance = 0.3f;    // 피로가 TiredRestFatigue 이상이면 쉼터
        private const float RestChance = 0.08f;        // 그 밖에는 가끔
        private const int TiredRestFatigue = 30;
        private const float SeatSpacing = 0.32f;       // 쉼터 자리 사이
        private const float SeatFrontGap = 0.22f;      // 장식 아래끝에서 앞으로
        private readonly Dictionary<string, float> _nextDecorRoll = new Dictionary<string, float>();
        // 떠돌이 상인(TravelingMerchant): 와 있는 날 마을 가운데 조금 오른쪽 아래에 서 있고, 누르면 상점 창
        private const string MerchantOwnerId = "merchant";
        private static readonly Vector2 MerchantOffset = new Vector2(2.2f, -1.2f);
        private VillageWanderer _merchant;
        private const float MarchGap = 0.4f;                          // 파견대가 한 줄로 나가는 간격
        private const float ReturnSpacing = 2f;                       // 귀환대 간격 = MarchGap × 이 값(유닛, 성문 밖에서 한 명씩 더 뒤에서 출발)
        private readonly Dictionary<string, float> _nextDecision = new Dictionary<string, float>();
        private float _doorFreeAt;

        // 같이 다니기: 혼자 걷는 용병마다 CompanionRoll초마다, 밖에 있는 가장 친한 동료를 관계 단계의 FollowChance 확률로 따라간다
        // (지인 15% · 친구 35% · 절친 60%, 낯섦 이하는 안 따라감). 서먹·앙숙인 사람이 있는 무리에는 끼지 않는다.
        // 한 무리는 리더 포함 MaxGroupSize명, GroupDuration초가 지나면 흩어진다.
        private const float CompanionRollMin = 20f;
        private const float CompanionRollMax = 40f;
        private const float FirstCompanionRollMin = 3f;
        private const float FirstCompanionRollMax = 12f;
        private const int MaxGroupSize = 3;
        private const float GroupDurationMin = 60f;
        private const float GroupDurationMax = 150f;
        private const float FollowSide = 0.28f;   // 리더 옆으로 떨어지는 거리
        private const float FollowBehind = 0.06f; // 조금 뒤(화면 위쪽)에 서서 리더가 앞에 그려지게
        private readonly Dictionary<string, string> _leaderOf = new Dictionary<string, string>(); // 따라가는 사람 → 리더
        private readonly Dictionary<string, float> _groupEndsAt = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _nextCompanionRoll = new Dictionary<string, float>();

        /// <summary>지금 씬의 마을 길찾기 격자(건설 미리보기가 빈 땅인지 볼 때 쓴다). 씬마다 다시 만든다.</summary>
        public static VillageNavGrid Grid { get; private set; }
        /// <summary>파견대가 드나드는 마을 출구. 건물로 막으면 안 된다.</summary>
        public static Vector2 VillageExit { get; private set; }

        private void Start()
        {
            // 걷기 영역 = 3×3 마을 바닥의 가운데 칸(VillageBounds.Center)에서 안쪽 여백을 뺀 곳
            if (!VillageBounds.Exists)
            {
                Debug.LogWarning("[VillagePartyPresenter] 마을 바닥 Tilemap을 찾지 못해 용병을 마을에 내보내지 않습니다. (Village 프리팹이 씬에 있는지 확인)");
                return;
            }
            _area = WalkArea(VillageBounds.Center);

            // 그림자 머티리얼은 Resources 밖에 있어 마을 건물에 이미 붙은 그림자의 것을 같이 쓴다.
            var anyShadow = FindFirstObjectByType<ProjectedShadow>();
            if (anyShadow != null) _shadowMaterial = anyShadow.GetComponent<SpriteRenderer>().sharedMaterial;
            _hidden = IsBedtime();
            var obstacles = VillageNavGrid.CollectObstacles();
            _grid = VillageNavGrid.Build(_area, obstacles); // 건물·장식 바닥을 막아 그 사이로만 걷게 한다
            Grid = _grid;
            _villageExit = _grid.NearestWalkable(new Vector2(_area.center.x, _area.yMin));
            VillageExit = _villageExit;
            VillageDepthSorter.Apply(obstacles);            // 건물·장식과 캐릭터의 앞뒤를 발밑 높이로 정한다
            CullOffscreenAnimators();
            BuildDecorSpots();
            _tagLayer = CreateNameTagLayer();
            FindLodgings();
            FindTrainingHallDoor();
            FindHospitalDoor();
            FindVenueDoors();
            VillageSkipVisits.SetBuildings(_venueDoors.Keys, _hasTrainingHall); // '진행'으로 건너뛸 때도 같은 건물에만 간다
            _infoPanel = MercenaryInfoPanel.Create(transform);
            VillageLayoutApplier.Rebuilt += OnVillageRebuilt;

            PlayerParty.Instance.OnChanged += Refresh;
            ExpeditionLog.Instance.OnChanged += Refresh;
            TrainingHall.OnChanged += Refresh;
            Hospital.OnChanged += Refresh;
            RestVenues.OnAnyChanged += Refresh;
            DecorActivities.OnChanged += SyncDecorHolds;
            TimeAdvanceController.Midpoint += ShuffleAfterTimeSkip;
            _subscribed = true;
            Refresh();
        }

        /// <summary>
        /// 걷는 칸(가운데 칸)에서 몸·이름표가 칸 끝 밖으로 나가지 않게 발 위치 기준 안쪽 여백을 뺀다.
        /// 목적지·경로·마을 출구가 모두 이 영역에서 정해지므로 여기만 줄이면 된다.
        /// </summary>
        private static Rect WalkArea(Rect world) => Rect.MinMaxRect(
            world.xMin + AreaMargin + ScreenMarginSide, world.yMin + AreaMargin + ScreenMarginBottom,
            world.xMax - AreaMargin - ScreenMarginSide, world.yMax - AreaMargin - ScreenMarginTop);

        /// <summary>
        /// 이름표 전용 화면 UI. 월드 위에 그려져 건물·나무에 가려지지 않고, sortingOrder -1이라
        /// 메뉴 Canvas(0)의 시장·파티·퀘스트 패널보다는 아래에 깔린다. 클릭을 받지 않게 GraphicRaycaster는 없다.
        /// </summary>
        private Transform CreateNameTagLayer()
        {
            var go = new GameObject("VillageNameTags", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            return go.transform;
        }

        /// <summary>
        /// 자정이 지나 아직 어두우면(0시~해 뜰 무렵) 모두 여관에서 잔다. 저녁~자정은 가로등 아래 마을에 있다.
        /// </summary>
        private static bool IsBedtime()
        {
            float hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.TimeOfDay : GameClock.CurrentHour;
            return hour < 12f && DayNightCycle.NightLightFactor > HideNightLightFactor;
        }

        private void Update()
        {
            if (!_subscribed) return;
            // 진행·건너뛰기가 막 끝났으면 남은 옛 인형을 한 번 더 정리한다(가운데 Midpoint 뒤에 생긴 것까지)
            bool advancing = TimeAdvanceController.IsAdvancing;
            if (_wasAdvancing && !advancing) SweepStrays();
            // 넘기기가 막 시작됐으면 성문·건물로 걸어가던 인형은 지운다(몇 시간 뒤면 이미 나갔거나 들어갔다)
            if (!_wasAdvancing && advancing) DropAllLeaving();
            _wasAdvancing = advancing;
            // 하루·4일·일주일 건너뛰기: 그동안은 캐릭터를 숨기고, 끝나면 마을 아무 곳에 무작위로 둔다(문·성문에서 나오기 없음)
            bool skippingDays = TimeAdvanceController.IsSkippingDays;
            if (skippingDays) HideAllForSkip();
            else if (_wasSkippingDays) PlaceAllRandomlyAfterSkip();
            _wasSkippingDays = skippingDays;
            bool hidden = IsBedtime();
            if (hidden != _hidden)
            {
                _hidden = hidden;
                foreach (var pair in _wanderers)
                {
                    if (pair.Value == null) continue;
                    // 아침: 숙소(여관·숙소 천막·길드 본부)가 있는 사람은 안에서 시작해 각자 무작위로 나온다(처음 결정은 조금 빨리).
                    if (!_hidden && TryHomeDoor(pair.Key, out _, out _))
                        _nextDecision[pair.Key] = Time.time + RandomRange(0.5f, InsideRestMax * 0.5f);
                    else pair.Value.gameObject.SetActive(!_hidden);
                }
            }

            if (!_hidden) UpdateCompanions();
            if (!_hidden && (_hasInn || _hasHq))
            {
                if (IsAllOutTime()) BringEveryoneOut();
                else UpdateInnVisits();
            }
            if (!_hidden && _hasTrainingHall) UpdateTrainingVisits();
            if (!_hidden && _venueDoors.Count > 0) UpdateVenueVisits();
            if (!_hidden && DecorActivities.Spots.Count > 0) UpdateDecorVisits();
            if (!_hidden) SyncDecorHolds();
            SyncMerchant();

            UpdateHoverAndClick();
            CloseInfoIfGone();
        }

        /// <summary>
        /// 용병이 묵는 숙소의 문과, 그 아래 처음 걸을 수 있는 바깥 지점: 여관(구운 "여관@..." 포함), 없으면 여관 전 단계의
        /// 용병 숙소 천막(입구 아래 가운데). 둘 다 없으면 드나들기 없이 마을 아무 곳에 나타난다.
        /// 이 문으로 아침에 나오고 낮에 들어가 쉬었다 나온다(_hasInn·_innDoor는 숙소 천막도 뜻한다). 길드 본부 문(_hqDoor)도 함께 찾는다.
        /// </summary>
        private void FindLodgings()
        {
            var renderer = VillageInn.FindRenderer();
            float doorU = InnDoorU, doorV = InnDoorV;
            if (renderer == null)
            {
                renderer = VillageProps.FindRenderer(BuildingCatalog.CampPrefix);
                doorU = CampDoorU;
                doorV = CampDoorV;
            }
            if (renderer != null)
            {
                _innDoor = DoorOf(renderer, doorU, doorV);
                _innExit = _grid.FirstWalkableBelow(_innDoor);
                _hasInn = true;
            }

            // 길드 본부(길드 홀, 없으면 길드 천막)의 아래 가운데 입구
            var hq = VillageProps.FindRenderer(GuildHallSpritePrefix) ?? VillageProps.FindRenderer(BuildingCatalog.TentPrefix);
            _hasHq = hq != null;
            if (!_hasHq) return;
            _hqDoor = DoorOf(hq, HqDoorU, HqDoorV);
            _hqExit = _grid.FirstWalkableBelow(_hqDoor);
        }

        private static Vector2 DoorOf(SpriteRenderer renderer, float u, float v)
        {
            var bounds = renderer.sprite.bounds;
            var local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, u), Mathf.Lerp(bounds.min.y, bounds.max.y, v), 0f);
            return renderer.transform.TransformPoint(local);
        }

        /// <summary>
        /// 이 용병이 묵는 곳의 문: 길드 본부에 묵는 3명(GuildHqLodging — 여관이 없을 때)은 본부 문, 그 밖은 여관·숙소 천막 문.
        /// 묵을 곳이 없으면 false(마을 아무 곳에 나타나고 드나들지 않는다).
        /// </summary>
        private bool TryHomeDoor(string mercId, out Vector2 door, out Vector2 exit)
        {
            if (_hasHq && !GuildHqLodging.HasInn && GuildHqLodging.IsResident(mercId))
            {
                door = _hqDoor;
                exit = _hqExit;
                return true;
            }
            door = _innDoor;
            exit = _innExit;
            return _hasInn;
        }

        /// <summary>
        /// 낮 동안 용병마다 정해진 시각이 되면 주사위를 굴린다: 안에 있으면 나올지, 밖에 있으면 들어갈지.
        /// 안 = GameObject 꺼짐. 들어가는 중인 용병은 건드리지 않는다.
        /// </summary>
        /// <summary>훈련소 그림의 아래 가운데(발밑)를 문으로, 그 아래 처음 걸을 수 있는 지점을 문 앞으로 정한다. 없으면 훈련 드나들기 연출 없음.</summary>
        private void FindTrainingHallDoor()
        {
            var renderer = VillageProps.FindRenderer(TrainingHallSpritePrefix) ?? VillageProps.FindRenderer(BuildingCatalog.BarracksPrefix); // 간이 훈련소
            if (renderer == null) return;
            var bounds = renderer.bounds;
            _trainingDoor = new Vector2(bounds.center.x, bounds.min.y + bounds.size.y * 0.08f);
            _trainingExit = _grid.FirstWalkableBelow(_trainingDoor);
            _hasTrainingHall = true;
        }

        /// <summary>병원 그림의 아치 문(아래쪽)을 문으로, 그 아래 처음 걸을 수 있는 지점을 문 앞으로 정한다. 없으면 입·퇴원 드나들기 연출 없음.</summary>
        private void FindHospitalDoor()
        {
            var renderer = VillageProps.FindRenderer(HospitalSpritePrefix);
            if (renderer == null) return;
            var bounds = renderer.bounds;
            _hospitalDoor = new Vector2(Mathf.Lerp(bounds.min.x, bounds.max.x, HospitalDoorU), bounds.min.y + bounds.size.y * 0.08f);
            _hospitalExit = _grid.FirstWalkableBelow(_hospitalDoor);
            _hasHospital = true;
        }

        /// <summary>쉼터(온천·도박장) 그림마다 문(아래쪽, RestVenue.DoorU)과 그 아래 처음 걸을 수 있는 지점을 정한다. 그림이 없는 쉼터는 드나들기 없음.</summary>
        private void FindVenueDoors()
        {
            foreach (var venue in RestVenues.All)
            {
                var renderer = VillageProps.FindRenderer(venue.SpritePrefix);
                if (renderer == null) continue;
                var bounds = renderer.bounds;
                var door = new Vector2(Mathf.Lerp(bounds.min.x, bounds.max.x, venue.DoorU), bounds.min.y + bounds.size.y * 0.08f);
                _venueDoors[venue] = (door, _grid.FirstWalkableBelow(door));
            }
        }

        /// <summary>9시~자정: 전원 밖. 게임 시계 기준이라 "진행" 전환이 끝나 9시가 된 뒤에 나온다(새벽은 _hidden이 먼저 처리).</summary>
        private static bool IsAllOutTime() => GameClock.CurrentHour >= AllOutHour;

        /// <summary>여관 안에 있는 용병을 문에서 DoorGap초 간격으로 한 명씩 내보낸다. 들어가는 중인 용병은 다 들어간 뒤 나온다.</summary>
        private void BringEveryoneOut()
        {
            float now = Time.time;
            if (now < _doorFreeAt) return;
            foreach (var wanderer in _wanderers.Values)
            {
                if (wanderer == null || wanderer.IsEntering || wanderer.gameObject.activeSelf) continue;
                wanderer.gameObject.SetActive(true);
                if (TryHomeDoor(wanderer.OwnerId, out var door, out var exit)) wanderer.ExitBuilding(door, exit);
                _doorFreeAt = now + DoorGap;
                return; // 한 번에 한 명
            }
        }

        /// <summary>
        /// 밖에 나와 걷고 있는 용병이 가끔 스스로 훈련소에 간다. 사람마다 결정 시각이 되면 TrainingChance 확률로
        /// TrainingHall에 1~3시간 훈련을 등록하고, 등록되면 Refresh가 훈련소 문까지 걸어 들어가게 한다. 밤·정원 초과면 TrainingHall이 거절한다.
        /// </summary>
        private void UpdateTrainingVisits()
        {
            if (GameClock.IsNight || TrainingHall.IsFull) return;
            float now = Time.time;
            foreach (var pair in _wanderers.ToList())
            {
                var wanderer = pair.Value;
                if (wanderer == null || !wanderer.gameObject.activeSelf || wanderer.IsEntering) continue;
                if (!_nextTrainingRoll.TryGetValue(pair.Key, out float at))
                {
                    _nextTrainingRoll[pair.Key] = now + RandomRange(FirstTrainingRollMin, FirstTrainingRollMax);
                    continue;
                }
                if (now < at) continue;
                _nextTrainingRoll[pair.Key] = now + RandomRange(TrainingRollMin, TrainingRollMax);
                if (_rng.NextDouble() >= TrainingChance || !_mercById.TryGetValue(pair.Key, out var merc)) continue;
                if (!TrainingHall.WantsToTrain(merc)) continue; // 다쳤거나 지쳤으면 훈련하러 가지 않는다
                float hours = RandomRange(TrainingHall.MinSessionHours, TrainingHall.MaxSessionHours);
                var buddy = ClosestOutsideFriend(merc, pair.Key); // Refresh가 _wanderers를 바꾸기 전에 고른다
                if (!TrainingHall.TryAdd(merc, hours)) continue;
                // 친한 동료가 밖에 있으면 관계 단계의 동행 확률로 같이 훈련하러 간다(정원·피로는 TrainingHall이 거절).
                if (buddy.merc != null && TrainingHall.WantsToTrain(buddy.merc)
                    && _rng.NextDouble() < Affinity.TierOf(buddy.value).TogetherChance && TrainingHall.TryAdd(buddy.merc, hours))
                    DailyLog.Add($"{Josa.WaGwa(merc.Name)} {Josa.IGa(buddy.merc.Name)} 같이 훈련했다."); // 이동 알림은 띄우지 않는다(위치는 파티 구성)
                return; // 한 번에 한 무리(Refresh가 _wanderers를 바꾼다)
            }
        }

        /// <summary>
        /// 밖에 나와 걷는 용병이 가끔 스스로 쉼터(온천·도박장)에 간다. 사람마다 결정 시각이 되면 마을에 있는 쉼터를 무작위 순서로
        /// 하나씩 RestVenue.VisitChance(온천은 피로, 도박장은 낮은 사기·피로 비례) 확률로 굴려 처음 걸린 곳에 등록하고,
        /// 등록되면 Refresh가 그 문까지 걸어 들어가게 한다. 친한 동료도 그 쉼터에 갈 만하면(VisitChance > 0) 같이 갈 수 있다.
        /// </summary>
        /// <summary>
        /// 마을에 놓은 쉼터 장식(Seats &gt; 0)과 허수아비(TrainingDummy)를 DecorActivities 자리로 넘긴다(마을을 고칠 때마다).
        /// 쉼터는 장식 앞쪽에 자리 수만큼 나란히 서서 가운데를 보고, 허수아비는 왼쪽 옆에 서서 오른쪽(허수아비)을 본다.
        /// </summary>
        private void BuildDecorSpots()
        {
            var spots = new List<DecorActivities.Spot>();
            foreach (var slot in VillageLayoutApplier.Slots)
            {
                var entry = slot.Entry;
                if (!entry.Decor || !slot.Built || slot.Renderer == null || !slot.Renderer.gameObject.activeInHierarchy) continue;
                if (entry.Seats <= 0 && !entry.TrainingDummy) continue;
                var b = slot.Renderer.bounds;
                var spot = new DecorActivities.Spot { Key = slot.Key, Center = b.center };
                if (entry.TrainingDummy)
                {
                    spot.Kind = DecorActivities.Kind.Training;
                    spot.Seats = new[] { new Vector2(b.min.x - 0.12f, b.min.y + 0.05f) };
                    spot.FaceRight = new[] { true };
                }
                else
                {
                    spot.Kind = DecorActivities.Kind.Rest;
                    spot.Seats = new Vector2[entry.Seats];
                    spot.FaceRight = new bool[entry.Seats];
                    for (int i = 0; i < entry.Seats; i++)
                    {
                        float x = b.center.x + (i - (entry.Seats - 1) * 0.5f) * SeatSpacing;
                        spot.Seats[i] = new Vector2(x, b.min.y - SeatFrontGap);
                        spot.FaceRight[i] = x <= b.center.x; // 가운데(장식)를 본다
                    }
                }
                spots.Add(spot);
            }
            DecorActivities.SetSpots(spots);
        }

        /// <summary>
        /// 밖에서 걷는 용병이 가끔 스스로 장식에 간다: 훈련하고 싶으면(TrainingHall.WantsToTrain) 허수아비, 아니면 지쳤을수록
        /// 쉼터(빈자리가 있는 가장 가까운 곳). 친한 동료가 밖에 있으면 관계 단계의 동행 확률로 같은 쉼터 옆자리에 같이 간다.
        /// </summary>
        private void UpdateDecorVisits()
        {
            if (GameClock.IsNight) return;
            float now = Time.time;
            foreach (var pair in _wanderers.ToList())
            {
                var wanderer = pair.Value;
                if (wanderer == null || !wanderer.gameObject.activeSelf || wanderer.IsEntering || wanderer.IsHolding) continue;
                if (!_nextDecorRoll.TryGetValue(pair.Key, out float at))
                {
                    _nextDecorRoll[pair.Key] = now + RandomRange(FirstDecorRollMin, FirstDecorRollMax);
                    continue;
                }
                if (now < at) continue;
                _nextDecorRoll[pair.Key] = now + RandomRange(DecorRollMin, DecorRollMax);
                if (!_mercById.TryGetValue(pair.Key, out var merc) || !DecorActivities.IsFree(merc)) continue;

                Vector2 here = wanderer.transform.position;
                float hours = RandomRange(DecorActivities.MinSessionHours, DecorActivities.MaxSessionHours);
                var dummy = ClosestFreeSpot(DecorActivities.Kind.Training, here);
                if (dummy != null && TrainingHall.WantsToTrain(merc) && _rng.NextDouble() < DummyChance)
                {
                    if (DecorActivities.TryStart(merc, dummy, hours)) return;
                    continue;
                }
                var rest = ClosestFreeSpot(DecorActivities.Kind.Rest, here);
                float chance = merc.Fatigue >= TiredRestFatigue ? TiredRestChance : RestChance;
                if (rest == null || _rng.NextDouble() >= chance) continue;
                var buddy = ClosestOutsideFriend(merc, pair.Key);
                if (!DecorActivities.TryStart(merc, rest, hours)) continue;
                if (buddy.merc != null && _rng.NextDouble() < Affinity.TierOf(buddy.value).TogetherChance && DecorActivities.TryStart(buddy.merc, rest, hours))
                    DailyLog.Add($"{Josa.WaGwa(merc.Name)} {Josa.IGa(buddy.merc.Name)} 같이 앉아 쉬었다.");
                return; // 한 번에 한 무리
            }
        }

        /// <summary>떠돌이 상인이 와 있으면(낮) 마을 가운데쯤에 세우고, 떠났거나 밤이면 치운다(그날 외형은 날짜로 고정).</summary>
        private void SyncMerchant()
        {
            bool show = GN3.Economy.TravelingMerchant.IsHere && !_hidden && !TimeAdvanceController.IsSkippingDays;
            if (!show)
            {
                if (_merchant != null) Destroy(_merchant.gameObject);
                _merchant = null;
                return;
            }
            if (_merchant != null) return;
            Vector2 spot = _grid.NearestWalkable(_area.center + MerchantOffset);
            var go = new GameObject("Wanderer_떠돌이 상인");
            go.transform.position = spot;
            _merchant = go.AddComponent<VillageWanderer>();
            _merchant.Init(GN3.CharacterAnim.RandomCharacterComposer.Compose(new System.Random(GN3.Economy.TravelingMerchant.HereDay)), _grid, _rng, _shadowMaterial);
            _merchant.OwnerId = MerchantOwnerId;
            _merchant.HoldAt(spot, false);
            VillageNameTag.Create(_tagLayer, go.transform, "<color=#f0c060>떠돌이 상인</color>");
        }

        private DecorActivities.Spot ClosestFreeSpot(DecorActivities.Kind kind, Vector2 from) =>
            DecorActivities.Spots.Where(s => s.Kind == kind && DecorActivities.FreeSeat(s) >= 0)
                .OrderBy(s => (s.Center - from).sqrMagnitude).FirstOrDefault();

        /// <summary>장식에서 쉬거나 훈련하는 용병은 그 자리로 가서 머물고(허수아비는 검 휘두르기), 끝난 사람은 다시 돌아다닌다.</summary>
        private void SyncDecorHolds()
        {
            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (wanderer == null || !wanderer.gameObject.activeSelf || wanderer.IsEntering) continue;
                if (_mercById.TryGetValue(pair.Key, out var merc) && DecorActivities.TryGetSeat(merc, out var seat, out bool faceRight))
                {
                    if (!wanderer.IsHolding || (wanderer.HoldSpot - seat).sqrMagnitude > 0.0001f)
                    {
                        Dissolve(pair.Key); // 같이 걷던 무리에서 빠진다
                        wanderer.HoldAt(seat, faceRight, DecorActivities.KindOf(merc) == DecorActivities.Kind.Training ? "slash" : "idle");
                    }
                }
                else if (wanderer.IsHolding) wanderer.Release();
            }
        }

        private void UpdateVenueVisits()
        {
            float now = Time.time;
            foreach (var pair in _wanderers.ToList())
            {
                var wanderer = pair.Value;
                if (wanderer == null || !wanderer.gameObject.activeSelf || wanderer.IsEntering || wanderer.IsHolding) continue;
                if (!_nextRestRoll.TryGetValue(pair.Key, out float at))
                {
                    _nextRestRoll[pair.Key] = now + RandomRange(FirstRestRollMin, FirstRestRollMax);
                    continue;
                }
                if (now < at) continue;
                _nextRestRoll[pair.Key] = now + RandomRange(RestRollMin, RestRollMax);
                if (!_mercById.TryGetValue(pair.Key, out var merc)) continue;

                var venue = _venueDoors.Keys.OrderBy(_ => _rng.Next())
                    .FirstOrDefault(v => v.IsOpen && !v.IsFull && _rng.NextDouble() < v.VisitChance(merc));
                if (venue == null) continue;
                float hours = RandomRange(venue.MinSessionHours, venue.MaxSessionHours);
                var buddy = ClosestOutsideFriend(merc, pair.Key); // Refresh가 _wanderers를 바꾸기 전에 고른다
                if (!venue.TryAdd(merc, hours)) continue;
                if (buddy.merc != null && venue.VisitChance(buddy.merc) > 0f
                    && _rng.NextDouble() < Affinity.TierOf(buddy.value).TogetherChance && venue.TryAdd(buddy.merc, hours))
                    DailyLog.Add($"{Josa.WaGwa(merc.Name)} {Josa.IGa(buddy.merc.Name)} 같이 {venue.Name}에 갔다.");
                return; // 한 번에 한 무리(Refresh가 _wanderers를 바꾼다)
            }
        }

        private void UpdateInnVisits()
        {
            float now = Time.time;
            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (wanderer == null || wanderer.IsEntering || wanderer.IsHolding) continue; // 장식에서 쉬거나 훈련 중이면 그대로
                if (!TryHomeDoor(pair.Key, out var homeDoor, out var homeExit)) continue; // 묵을 곳이 없는 사람은 드나들지 않는다
                if (_nextDecision.TryGetValue(pair.Key, out float at) && now < at) continue;

                bool inside = !wanderer.gameObject.activeSelf;
                if (inside)
                {
                    if (now < _doorFreeAt) continue; // 다음 프레임에 다시 굴린다
                    _nextDecision[pair.Key] = now + RandomRange(InsideRestMin, InsideRestMax);
                    if (_rng.NextDouble() >= ComeOutChance) continue;
                    wanderer.gameObject.SetActive(true);
                    wanderer.ExitBuilding(homeDoor, homeExit);
                    _doorFreeAt = now + DoorGap;
                    BringFriendOutAfter(pair.Key);
                }
                else
                {
                    _nextDecision[pair.Key] = now + RandomRange(OutsideWanderMin, OutsideWanderMax);
                    if (_leaderOf.ContainsKey(pair.Key)) continue; // 따라가는 중이면 리더가 들어갈 때 같이 들어간다
                    if (_rng.NextDouble() >= GoInChance) continue;
                    var followers = FollowersOf(pair.Key);
                    GoIntoInn(pair.Key, wanderer);
                    foreach (var followerId in followers)
                        if (_wanderers.TryGetValue(followerId, out var follower) && IsOutside(follower))
                            GoIntoInn(followerId, follower);
                }
            }
        }

        private void GoIntoInn(string id, VillageWanderer wanderer)
        {
            Dissolve(id);
            if (!TryHomeDoor(id, out var door, out var exit)) return; // 묵을 곳이 없으면 밖에 남는다
            wanderer.ReturnInto(exit, door, () =>
            {
                if (wanderer != null) wanderer.gameObject.SetActive(false);
                _nextDecision[id] = Time.time + RandomRange(InsideRestMin, InsideRestMax);
            });
        }

        /// <summary>
        /// 여관에서 막 나온 사람의 친구 중 아직 안에 있는 가장 친한 사람이 관계 단계의 TogetherChance 확률로
        /// DoorGap초 뒤 따라 나와 같이 걷는다(지인 20% · 친구 45% · 절친 75%).
        /// </summary>
        private void BringFriendOutAfter(string leaderId)
        {
            if (!_mercById.TryGetValue(leaderId, out var leaderMerc)) return;
            string bestId = null;
            int best = Affinity.Acquaintance.MinValue - 1;
            foreach (var pair in _wanderers)
            {
                if (pair.Key == leaderId || pair.Value == null || pair.Value.gameObject.activeSelf || pair.Value.IsEntering) continue;
                if (!_mercById.TryGetValue(pair.Key, out var other)) continue;
                int value = Affinity.Get(leaderMerc, other);
                if (value > best) { best = value; bestId = pair.Key; }
            }
            if (bestId == null || _rng.NextDouble() >= Affinity.TierOf(best).TogetherChance) return;
            _doorFreeAt = Time.time + DoorGap * 2f; // 그 사이 다른 사람이 문을 쓰지 않게
            _nextDecision[bestId] = Time.time + DoorGap * 3f;
            StartCoroutine(ExitAndFollow(bestId, leaderId, DoorGap));
        }

        private System.Collections.IEnumerator ExitAndFollow(string id, string leaderId, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_hidden || !_wanderers.TryGetValue(id, out var wanderer) || wanderer == null || wanderer.gameObject.activeSelf) yield break;
            if (!_wanderers.TryGetValue(leaderId, out var leader) || !IsOutside(leader)) yield break;
            wanderer.gameObject.SetActive(true);
            if (TryHomeDoor(id, out var door, out var exit)) wanderer.ExitBuilding(door, exit);
            _nextDecision[id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
            StartFollowing(id, wanderer, leaderId, leader);
        }

        // ---------- 같이 다니기 ----------

        private List<string> FollowersOf(string leaderId) =>
            _leaderOf.Where(kv => kv.Value == leaderId).Select(kv => kv.Key).ToList();

        private static bool IsOutside(VillageWanderer w) => w != null && w.gameObject.activeSelf && !w.IsEntering;

        /// <summary>밖에 나와 걷는 사람 중 merc와 가장 친한 동료(지인 이상). 없으면 (null, 0).</summary>
        private (Mercenary merc, int value) ClosestOutsideFriend(Mercenary merc, string selfId)
        {
            Mercenary best = null;
            int bestValue = Affinity.Acquaintance.MinValue - 1;
            foreach (var pair in _wanderers)
            {
                if (pair.Key == selfId || !IsOutside(pair.Value) || !_mercById.TryGetValue(pair.Key, out var other)) continue;
                int value = Affinity.Get(merc, other);
                if (value > bestValue) { bestValue = value; best = other; }
            }
            return best != null ? (best, bestValue) : (null, 0);
        }

        private void StartFollowing(string id, VillageWanderer wanderer, string leaderId, VillageWanderer leader)
        {
            float side = FollowersOf(leaderId).Count % 2 == 0 ? FollowSide : -FollowSide;
            _leaderOf[id] = leaderId;
            _groupEndsAt[id] = Time.time + RandomRange(GroupDurationMin, GroupDurationMax);
            wanderer.Follow(leader, new Vector2(side, FollowBehind));
        }

        /// <summary>id가 든 무리를 푼다(따라가던 사람이면 혼자 걷고, 리더면 따라오던 사람들이 흩어진다).</summary>
        private void Dissolve(string id)
        {
            if (_leaderOf.Remove(id))
            {
                _groupEndsAt.Remove(id);
                if (_wanderers.TryGetValue(id, out var self) && self != null) self.StopFollowing();
            }
            foreach (var followerId in FollowersOf(id))
            {
                _leaderOf.Remove(followerId);
                _groupEndsAt.Remove(followerId);
                if (_wanderers.TryGetValue(followerId, out var follower) && follower != null) follower.StopFollowing();
            }
        }

        /// <summary>
        /// 무리를 정리하고(누가 사라짐·시간이 다 됨·따라가기가 스스로 끊김), 혼자 걷는 사람은 정해진 시각마다
        /// 가장 친한 동료를 친밀도에 비례한 확률로 따라가기 시작한다.
        /// </summary>
        private void UpdateCompanions()
        {
            float now = Time.time;
            foreach (var pair in _leaderOf.ToList())
            {
                if (!_leaderOf.ContainsKey(pair.Key)) continue; // 앞에서 이미 풀림
                _wanderers.TryGetValue(pair.Key, out var follower);
                _wanderers.TryGetValue(pair.Value, out var leader);
                bool ended = _groupEndsAt.TryGetValue(pair.Key, out float end) && now >= end;
                if (ended || !IsOutside(follower) || !IsOutside(leader) || follower.Leader != leader)
                    Dissolve(pair.Key);
            }

            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (!IsOutside(wanderer) || _leaderOf.ContainsKey(pair.Key)) continue;
                if (!_nextCompanionRoll.TryGetValue(pair.Key, out float at))
                {
                    _nextCompanionRoll[pair.Key] = now + RandomRange(FirstCompanionRollMin, FirstCompanionRollMax);
                    continue;
                }
                if (now < at) continue;
                _nextCompanionRoll[pair.Key] = now + RandomRange(CompanionRollMin, CompanionRollMax);
                if (FollowersOf(pair.Key).Count > 0 || !_mercById.TryGetValue(pair.Key, out var merc)) continue; // 리더는 남을 따라가지 않는다

                // 따라갈 사람: 밖에 있고, 남을 따라가는 중이 아니고, 무리에 자리가 남았고, 무리에 서먹·앙숙이 없는 가장 친한 동료
                string leaderId = null;
                int best = Affinity.Acquaintance.MinValue - 1;
                foreach (var other in _wanderers)
                {
                    if (other.Key == pair.Key || !IsOutside(other.Value) || _leaderOf.ContainsKey(other.Key)) continue;
                    var followers = FollowersOf(other.Key);
                    if (followers.Count + 1 >= MaxGroupSize || !_mercById.TryGetValue(other.Key, out var otherMerc)) continue;
                    if (followers.Any(f => _mercById.TryGetValue(f, out var fm) && Affinity.TierBetween(merc, fm).Avoids)) continue;
                    int value = Affinity.Get(merc, otherMerc);
                    if (value > best) { best = value; leaderId = other.Key; }
                }
                if (leaderId == null || _rng.NextDouble() >= Affinity.TierOf(best).FollowChance) continue;
                StartFollowing(pair.Key, wanderer, leaderId, _wanderers[leaderId]);
                return; // _leaderOf가 바뀌었으니 나머지는 다음 프레임에
            }
        }

        /// <summary>
        /// 파견대가 한 명씩 MarchGap초 간격으로 마을 출구까지 걸어 나가 사라진다. 여관 안에 있던 사람은 문에서 나와 출발한다.
        /// 성벽 성문(CastleGate.Expedition)이 있으면 출구에서 멈추지 않고 성문을 지나 성벽 밖까지 걸어 나간 뒤 사라진다.
        /// 모두 자는 시간(자정~아침)이면 그냥 사라진다.
        /// </summary>
        private System.Collections.IEnumerator MarchOut(List<VillageWanderer> party)
        {
            foreach (var wanderer in party)
            {
                if (wanderer == null) continue;
                if (_hidden || TimeAdvanceController.IsAdvancing) // 자는 시간이거나 넘기는 중: 걷지 않고 바로
                {
                    Destroy(wanderer.gameObject);
                    continue;
                }
                if (!wanderer.gameObject.activeSelf)
                {
                    wanderer.gameObject.SetActive(true);
                    if (TryHomeDoor(wanderer.OwnerId, out var door, out _)) wanderer.PlaceAt(door); // 문간에서 출발 → 가장 가까운 바깥 칸으로 먼저 나온다
                }
                var leaving = wanderer;
                var gate = CastleGate.Expedition;
                _leaving.Add(leaving);
                // 성문이 있으면 출구를 지나 성문으로 성벽 밖까지 걸어 나간다(가까이 가면 성문이 열린다).
                wanderer.WalkAndVanish(_villageExit, () => { _leaving.Remove(leaving); if (leaving != null) Destroy(leaving.gameObject); },
                    gate != null ? gate.OutwardSteps : null);
                if (gate != null) gate.Pass(wanderer.transform);
                yield return new WaitForSeconds(MarchGap);
            }
        }

        private float RandomRange(float min, float max) => Mathf.Lerp(min, max, (float)_rng.NextDouble());

        /// <summary>마우스 아래 가장 앞의 캐릭터에 흰 테두리를 켜고, 왼쪽 클릭하면 그 용병 정보 창을 연다. UI 위에서는 무시.</summary>
        private void UpdateHoverAndClick()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            VillageWanderer target = null;
            if (mouse != null && cam != null)
            {
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUI) target = VillageWanderer.Pick(cam.ScreenToWorldPoint(mouse.position.ReadValue()));
                // 건물로 들어가는 중인(등록 안 된) 인형은 고르지 않는다 — 눌러도 창이 안 열리고 뒤의 진짜 인형을 가렸다
                if (target != null && target != _merchant
                    && (target.OwnerId == null || !_wanderers.TryGetValue(target.OwnerId, out var registered) || registered != target))
                    target = null;
            }

            if (target != _hovered)
            {
                if (_hovered != null) _hovered.SetHighlighted(false);
                _hovered = target;
                if (_hovered != null) _hovered.SetHighlighted(true);
            }

            bool canClick = _hovered != null && mouse.leftButton.wasPressedThisFrame && !BuildPlacement.Active && !BuildingMover.Active && !ForestFelling.Active;
            if (canClick && _hovered == _merchant) MerchantShopUI.Open();
            else if (canClick && _hovered.OwnerId != null && _mercById.TryGetValue(_hovered.OwnerId, out var merc))
                _infoPanel.Show(merc, fromVillage: true);
        }

        /// <summary>보던 용병이 마을에서 사라지면(파견·해고·사망·밤) 창을 닫는다.</summary>
        private void CloseInfoIfGone()
        {
            var shown = _infoPanel.Shown;
            if (shown == null) return;
            // 시장·파티 패널에서 연 창은 여관 안·파견 중 용병도 볼 수 있어야 하므로 닫지 않는다.
            if (!_infoPanel.OpenedFromVillage) return;
            if (!_wanderers.TryGetValue(shown.Id, out var wanderer) || wanderer == null || !wanderer.gameObject.activeInHierarchy)
                _infoPanel.Hide();
        }

        // Play 중 Scene 뷰에 막힌 바닥(건물·장식 점유 영역 + 캐릭터 반지름)을 빨갛게 보여 준다. 비율 조정용.
        private void OnDrawGizmos()
        {
            if (_grid == null) return;
            Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
            foreach (var r in _grid.BlockedRects)
                Gizmos.DrawCube(r.center, new Vector3(r.width, r.height, 0.01f));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(_area.center, new Vector3(_area.width, _area.height, 0.01f));
        }

        /// <summary>
        /// 건물을 지었다(씬을 다시 부르지 않음 — VillageLayoutApplier.ApplyLive). 같은 격자를 새 장애물로 다시 막고(걷는 용병이 들고 있는 격자),
        /// 새 건물만 앞뒤 정렬하고(이미 정렬된 물체는 건너뜀), 문 위치를 다시 찾는다.
        /// </summary>
        private void OnVillageRebuilt()
        {
            if (_grid == null) return;
            var obstacles = VillageNavGrid.CollectObstacles();
            _grid.Rebuild(obstacles);
            _villageExit = _grid.NearestWalkable(new Vector2(_area.center.x, _area.yMin));
            VillageExit = _villageExit;
            VillageDepthSorter.Apply(obstacles);
            CullOffscreenAnimators();
            BuildDecorSpots();
            _hasInn = _hasHq = _hasTrainingHall = _hasHospital = false;
            _venueDoors.Clear();
            FindLodgings();
            FindTrainingHallDoor();
            FindHospitalDoor();
            FindVenueDoors();
            VillageSkipVisits.SetBuildings(_venueDoors.Keys, _hasTrainingHall);
            Refresh();
        }

        /// <summary>
        /// 마을이 3×3으로 넓어 흔들리는 나무 Animator가 수백 개다. 화면에 안 보이는 그림은 애니메이션을 멈춘다(보이면 다시 움직임).
        /// 멈춘 나무는 그림자(ProjectedShadow)도 바뀐 게 없어 계산을 건너뛴다. 놓은 장식·새 건물도 맞추게 Rebuilt 때도 부른다.
        /// </summary>
        private static void CullOffscreenAnimators()
        {
            var village = GameObject.Find("Village");
            if (village == null) return;
            foreach (var animator in village.GetComponentsInChildren<Animator>(true))
                if (animator.cullingMode != AnimatorCullingMode.CullCompletely) animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        private void OnDestroy()
        {
            VillageLayoutApplier.Rebuilt -= OnVillageRebuilt;
            if (!_subscribed) return;
            PlayerParty.Instance.OnChanged -= Refresh;
            ExpeditionLog.Instance.OnChanged -= Refresh;
            TrainingHall.OnChanged -= Refresh;
            Hospital.OnChanged -= Refresh;
            RestVenues.OnAnyChanged -= Refresh;
            DecorActivities.OnChanged -= SyncDecorHolds;
            TimeAdvanceController.Midpoint -= ShuffleAfterTimeSkip;
            VillageSkipVisits.SetBuildings(null, false);
        }

        /// <summary>
        /// "진행"으로 3시간이 지나는 동안(화면이 가장 어두운 순간) 마을 사람들의 자리를 새로 정한다.
        /// 여관이 있으면 반반 확률로 밖(마을 아무 곳에서 쉬는 중) 또는 안(여관에서 쉬는 중)으로 다시 나눈다. 밤이면 그대로.
        /// </summary>
        private void ShuffleAfterTimeSkip()
        {
            SweepStrays();
            if (TimeAdvanceController.IsSkippingDays) return; // 날짜 건너뛰기는 끝날 때 한 번에 무작위 배치(PlaceAllRandomlyAfterSkip)
            if (_hidden || _grid == null) return;
            float now = Time.time;
            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (wanderer == null) continue;
                bool outside = !TryHomeDoor(pair.Key, out _, out _) || IsAllOutTime() || _rng.NextDouble() < ShuffleOutsideChance;
                if (outside)
                {
                    wanderer.gameObject.SetActive(true);
                    wanderer.PlaceAt(_grid.RandomWalkablePoint(_rng));
                    _nextDecision[pair.Key] = now + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else
                {
                    wanderer.gameObject.SetActive(false);
                    _nextDecision[pair.Key] = now + RandomRange(InsideRestMin, InsideRestMax);
                }
            }
        }

        /// <summary>
        /// 건물·출구로 들어가게 한다(도착하면 지움). 시간을 넘기는 중이거나 안 보이는 인형이면 걷지 않고 바로 지운다
        /// (빨리 넘기면 들어가다 나오기가 겹쳐 옛 인형이 쌓였다).
        /// </summary>
        private void SendInto(VillageWanderer wanderer, Vector2 exit, Vector2 door)
        {
            if (TimeAdvanceController.IsAdvancing || !wanderer.gameObject.activeSelf)
            {
                Destroy(wanderer.gameObject);
                return;
            }
            _leaving.Add(wanderer);
            wanderer.ReturnInto(exit, door, () => { _leaving.Remove(wanderer); if (wanderer != null) Destroy(wanderer.gameObject); });
        }

        /// <summary>날짜 건너뛰기 중: 모든 캐릭터를 숨기고 걸어 들어가던 인형은 지운다.</summary>
        private void HideAllForSkip()
        {
            DropAllLeaving();
            foreach (var w in _wanderers.Values)
                if (w != null && w.gameObject.activeSelf) w.gameObject.SetActive(false);
        }

        /// <summary>날짜 건너뛰기가 끝난 순간: 마을에 있는 캐릭터를 모두 무작위 자리에 둔다(밤이 아니면 여관 시간이어도 밖에).</summary>
        private void PlaceAllRandomlyAfterSkip()
        {
            SweepStrays();
            if (_grid == null) return;
            float now = Time.time;
            foreach (var pair in _wanderers.ToList())
            {
                var wanderer = pair.Value;
                if (wanderer == null) continue;
                Dissolve(pair.Key);
                wanderer.gameObject.SetActive(!_hidden);
                wanderer.PlaceAt(_grid.RandomWalkablePoint(_rng));
                _nextDecision[pair.Key] = now + RandomRange(OutsideWanderMin, OutsideWanderMax);
            }
            _trainingIds.Clear();
            _hospitalIds.Clear();
            _restingIn.Clear();
            _awayIds.Clear();
        }

        /// <summary>걸어 나가는·들어가는 중인 인형(_leaving)을 모두 지운다.</summary>
        private void DropAllLeaving()
        {
            foreach (var w in _leaving.ToList())
                if (w != null) Destroy(w.gameObject);
            _leaving.Clear();
        }

        /// <summary>이 용병의 들어가는·나가는 중인 옛 인형을 지운다.</summary>
        private void DropLeaving(string mercId)
        {
            foreach (var w in _leaving.Where(w => w == null || w.OwnerId == mercId).ToList())
            {
                _leaving.Remove(w);
                if (w != null) Destroy(w.gameObject);
            }
        }

        /// <summary>등록(_wanderers)도 안 되고 걸어 나가는 중(_leaving)도 아닌 인형을 모두 지운다(시간 넘기기 뒤 안전망).</summary>
        private void SweepStrays()
        {
            _leaving.RemoveWhere(w => w == null);
            var keep = new HashSet<VillageWanderer>(_wanderers.Values);
            foreach (var w in FindObjectsByType<VillageWanderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!keep.Contains(w) && !_leaving.Contains(w)) Destroy(w.gameObject);
        }

        private void Refresh()
        {
            var present = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m) && !TrainingHall.IsTraining(m) && !Hospital.IsAdmitted(m) && !RestVenues.IsResting(m))
                .ToList();

            var departing = new List<VillageWanderer>();
            string departingQuest = null;
            foreach (var id in _wanderers.Keys.Where(id => present.All(m => m.Id != id)).ToList())
            {
                var wanderer = _wanderers[id];
                // 살아서 파티에 있는데 파견 중이 됐으면: 바로 지우지 않고 출구로 걸어 나가게 한다.
                if (_mercById.TryGetValue(id, out var merc) && merc.IsAlive && PlayerParty.Instance.Members.Contains(merc)
                    && ExpeditionLog.Instance.IsOnExpedition(merc))
                {
                    _awayIds.Add(id);
                    departingQuest ??= ExpeditionLog.Instance.FindQuest(merc)?.Title;
                    if (wanderer != null) departing.Add(wanderer);
                }
                else if (merc != null && merc.IsAlive && TrainingHall.IsTraining(merc))
                {
                    // 훈련 시작: 밖에 나와 있으면 훈련소 문으로 걸어 들어가 사라진다.
                    _trainingIds.Add(id);
                    if (wanderer != null)
                    {
                        var entering = wanderer;
                        if (_hasTrainingHall) SendInto(entering, _trainingExit, _trainingDoor);
                        else Destroy(entering.gameObject);
                    }
                }
                else if (merc != null && merc.IsAlive && Hospital.IsAdmitted(merc))
                {
                    // 입원: 밖에 나와 있으면 병원 문으로 걸어 들어가 사라진다.
                    _hospitalIds.Add(id);
                    if (wanderer != null)
                    {
                        var entering = wanderer;
                        if (_hasHospital) SendInto(entering, _hospitalExit, _hospitalDoor);
                        else Destroy(entering.gameObject);
                    }
                }
                else if (merc != null && merc.IsAlive && RestVenues.Find(merc) is RestVenue venue)
                {
                    // 쉼터(온천·도박장): 밖에 나와 있으면 그 문으로 걸어 들어가 사라진다.
                    _restingIn[id] = venue;
                    if (wanderer != null)
                    {
                        var entering = wanderer;
                        if (_venueDoors.TryGetValue(venue, out var doors)) SendInto(entering, doors.exit, doors.door);
                        else Destroy(entering.gameObject);
                    }
                }
                else if (wanderer != null)
                {
                    Destroy(wanderer.gameObject); // 해고·사망
                }
                Dissolve(id);
                _wanderers.Remove(id);
                _mercById.Remove(id);
                _nextDecision.Remove(id);
                _nextCompanionRoll.Remove(id);
                _nextRestRoll.Remove(id);
            }
            if (departing.Count > 0 && TimeAdvanceController.IsAdvancing)
            {
                foreach (var w in departing) Destroy(w.gameObject); // 넘기는 중: 걸어 나가는 연출 없이 바로
                departing.Clear();
            }
            if (departing.Count > 0)
            {
                StartCoroutine(MarchOut(departing));
            }

            int returning = 0;
            foreach (var merc in present)
            {
                if (_wanderers.ContainsKey(merc.Id)) continue;
                DropLeaving(merc.Id); // 아직 들어가던 옛 인형이 있으면 지운다(같은 사람이 둘 보이지 않게)

                var go = new GameObject($"Wanderer_{merc.Name}");
                go.transform.position = _grid.RandomWalkablePoint(_rng);
                var wanderer = go.AddComponent<VillageWanderer>();
                wanderer.Init(merc.Appearance, _grid, _rng, _shadowMaterial);
                wanderer.OwnerId = merc.Id;
                _mercById[merc.Id] = merc;
                // 여관이 있으면 새로 온 용병은 여관 안에서 시작해 곧 무작위로 나온다.
                if (TimeAdvanceController.IsSkippingDays)
                {
                    // 날짜 건너뛰기 중: 숨겨 두었다가 끝날 때 무작위 자리에 둔다
                    _trainingIds.Remove(merc.Id);
                    _hospitalIds.Remove(merc.Id);
                    _restingIn.Remove(merc.Id);
                    _awayIds.Remove(merc.Id);
                    go.SetActive(false);
                }
                else if (_trainingIds.Remove(merc.Id) && _hasTrainingHall && !_hidden)
                {
                    // 훈련을 마친 용병: 훈련소 문에서 걸어 나온다.
                    go.SetActive(true);
                    wanderer.ExitBuilding(_trainingDoor, _trainingExit);
                    _nextDecision[merc.Id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else if (_hospitalIds.Remove(merc.Id) && _hasHospital && !_hidden)
                {
                    // 퇴원한 용병: 병원 문에서 걸어 나온다.
                    go.SetActive(true);
                    wanderer.ExitBuilding(_hospitalDoor, _hospitalExit);
                    _nextDecision[merc.Id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else if (_restingIn.Remove(merc.Id, out var leftVenue) && _venueDoors.TryGetValue(leftVenue, out var leftDoors) && !_hidden)
                {
                    // 쉼터에서 다 쉰 용병: 들어간 문에서 걸어 나온다.
                    go.SetActive(true);
                    wanderer.ExitBuilding(leftDoors.door, leftDoors.exit);
                    _nextDecision[merc.Id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else if (_awayIds.Remove(merc.Id) && !_hidden)
                {
                    // 파견에서 돌아온 용병: 마을 출구에서 걸어 들어온다.
                    go.SetActive(true);
                    var gate = CastleGate.Expedition;
                    if (gate != null)
                    {
                        // 성벽 밖에서 성문으로 들어와 마을 출구를 거쳐 들어온다.
                        // 함께 돌아온 사람은 한 명씩 더 바깥에서 출발해 겹치지 않고 한 줄로 들어온다.
                        var approach = new List<Vector2>(gate.InwardSteps) { _villageExit };
                        approach.Insert(0, approach[0] + Vector2.down * (MarchGap * ReturnSpacing * returning++));
                        wanderer.WalkInFrom(approach, _grid.RandomWalkablePoint(_rng));
                        gate.Pass(wanderer.transform);
                    }
                    else
                    {
                        wanderer.WalkInFrom(_villageExit, _grid.RandomWalkablePoint(_rng));
                    }
                    _nextDecision[merc.Id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else
                {
                    go.SetActive(!_hidden && !TryHomeDoor(merc.Id, out _, out _));
                    _nextDecision[merc.Id] = Time.time + RandomRange(1f, 4f);
                }
                VillageNameTag.Create(_tagLayer, go.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}"); // Wanderer가 사라지면 스스로 지워진다
                _wanderers[merc.Id] = wanderer;
            }
        }
    }
}
