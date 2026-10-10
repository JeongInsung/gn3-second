using System.Collections.Generic;
using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using GN3.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
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
        // 화면 가장자리에서 발 위치까지의 여백(유닛). 위쪽은 몸(약 0.6) + 이름표만큼 넉넉히.
        private const float ScreenMarginSide = 0.3f;
        private const float ScreenMarginBottom = 0.15f;
        private const float ScreenMarginTop = 0.8f;
        private const float HideNightLightFactor = 0.5f;

        // 용병들이 드나드는 여관 문(여관 그림 기준 비율, 왼쪽 아래 0). 512px 원본에서 아치문 중심 x≈206px, 디딤돌 y≈450px.
        private const float InnDoorU = 0.40f;
        private const float InnDoorV = 0.12f;

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
        private const float TrainingChance = 0.15f;
        private readonly Dictionary<string, float> _nextTrainingRoll = new Dictionary<string, float>();
        // 쉼터(온천·도박장): 밖에서 걷는 용병마다 RestRoll초마다 쉼터를 무작위 순서로 하나씩 RestVenue.VisitChance 확률로 굴려 처음 걸린 곳에 간다.
        private const float RestRollMin = 40f;
        private const float RestRollMax = 90f;
        private const float FirstRestRollMin = 20f;
        private const float FirstRestRollMax = 60f;
        private readonly Dictionary<RestVenue, (Vector2 door, Vector2 exit)> _venueDoors = new Dictionary<RestVenue, (Vector2, Vector2)>(); // 마을에 그림이 있는 쉼터만
        private readonly Dictionary<string, RestVenue> _restingIn = new Dictionary<string, RestVenue>(); // 쉼터에 들어간 용병(다 쉬면 그 문에서 나온다)
        private readonly Dictionary<string, float> _nextRestRoll = new Dictionary<string, float>();
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

        private void Start()
        {
            // 바닥은 바깥 바닥·광장·흙길(칠하기 층) 여러 장이라, 가장 넓은 것(바깥 바닥)을 걷기 영역으로 쓴다.
            Tilemap floor = null;
            float floorArea = 0f;
            foreach (var tilemap in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            {
                tilemap.CompressBounds();
                var size = tilemap.localBounds.size;
                if (size.x * size.y > floorArea) { floor = tilemap; floorArea = size.x * size.y; }
            }
            if (floor == null)
            {
                Debug.LogWarning("[VillagePartyPresenter] 마을 바닥 Tilemap을 찾지 못해 용병을 마을에 내보내지 않습니다. (Village 프리팹이 씬에 있는지 확인)");
                return;
            }

            var local = floor.localBounds;
            Vector3 min = floor.transform.TransformPoint(local.min);
            Vector3 max = floor.transform.TransformPoint(local.max);
            _area = Rect.MinMaxRect(min.x + AreaMargin, min.y + AreaMargin, max.x - AreaMargin, max.y - AreaMargin);
            ClampAreaToScreen();

            // 그림자 머티리얼은 Resources 밖에 있어 마을 건물에 이미 붙은 그림자의 것을 같이 쓴다.
            var anyShadow = FindFirstObjectByType<ProjectedShadow>();
            if (anyShadow != null) _shadowMaterial = anyShadow.GetComponent<SpriteRenderer>().sharedMaterial;
            _hidden = IsBedtime();
            var obstacles = VillageNavGrid.CollectObstacles();
            _grid = VillageNavGrid.Build(_area, obstacles); // 건물·장식 바닥을 막아 그 사이로만 걷게 한다
            _villageExit = _grid.NearestWalkable(new Vector2(_area.center.x, _area.yMin));
            VillageDepthSorter.Apply(obstacles);            // 건물·장식과 캐릭터의 앞뒤를 발밑 높이로 정한다
            _tagLayer = CreateNameTagLayer();
            FindInnDoor();
            FindTrainingHallDoor();
            FindHospitalDoor();
            FindVenueDoors();
            _infoPanel = MercenaryInfoPanel.Create(transform);

            PlayerParty.Instance.OnChanged += Refresh;
            ExpeditionLog.Instance.OnChanged += Refresh;
            TrainingHall.OnChanged += Refresh;
            Hospital.OnChanged += Refresh;
            RestVenues.OnAnyChanged += Refresh;
            TimeAdvanceController.Midpoint += ShuffleAfterTimeSkip;
            _subscribed = true;
            Refresh();
        }

        /// <summary>
        /// 바닥 타일은 화면보다 넓게 깔려 있어 그대로 쓰면 용병이 화면 밖으로 걸어 나갔다. 시작 화면(= CameraZoom이 가두는
        /// 가장 넓은 화면)과 겹치는 부분으로 줄이고, 몸·이름표가 잘리지 않게 발 위치 기준으로 안쪽 여백을 둔다.
        /// 목적지·경로·마을 출구가 모두 이 영역에서 정해지므로 여기만 줄이면 된다.
        /// </summary>
        private void ClampAreaToScreen()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;
            float halfHeight = cam.orthographicSize, halfWidth = halfHeight * cam.aspect;
            Vector3 center = cam.transform.position;
            float xMin = Mathf.Max(_area.xMin, center.x - halfWidth + ScreenMarginSide);
            float xMax = Mathf.Min(_area.xMax, center.x + halfWidth - ScreenMarginSide);
            float yMin = Mathf.Max(_area.yMin, center.y - halfHeight + ScreenMarginBottom);
            float yMax = Mathf.Min(_area.yMax, center.y + halfHeight - ScreenMarginTop);
            if (xMin < xMax && yMin < yMax) _area = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

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
            bool hidden = IsBedtime();
            if (hidden != _hidden)
            {
                _hidden = hidden;
                if (_hidden || !_hasInn)
                {
                    foreach (var wanderer in _wanderers.Values)
                        if (wanderer != null) wanderer.gameObject.SetActive(!_hidden);
                }
                else
                {
                    // 아침: 모두 여관 안에서 시작해 각자 무작위로 나온다(처음 결정은 조금 빨리).
                    foreach (var id in _wanderers.Keys)
                        _nextDecision[id] = Time.time + RandomRange(0.5f, InsideRestMax * 0.5f);
                }
            }

            if (!_hidden) UpdateCompanions();
            if (!_hidden && _hasInn)
            {
                if (IsAllOutTime()) BringEveryoneOut();
                else UpdateInnVisits();
            }
            if (!_hidden && _hasTrainingHall) UpdateTrainingVisits();
            if (!_hidden && _venueDoors.Count > 0) UpdateVenueVisits();

            UpdateHoverAndClick();
            CloseInfoIfGone();
        }

        /// <summary>
        /// 마을의 여관 그림(구운 "여관@..." 포함)을 찾아 문 앞 지점과, 그 아래 처음 걸을 수 있는 바깥 지점을 정한다.
        /// 창문 불빛 마스크·그림자·외곽선도 같은 그림 이름을 쓰므로 뺀다. 여관이 없으면 예전처럼 제자리에서 나타난다.
        /// </summary>
        private void FindInnDoor()
        {
            var renderer = VillageInn.FindRenderer();
            if (renderer == null) return;

            var bounds = renderer.sprite.bounds;
            var local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, InnDoorU), Mathf.Lerp(bounds.min.y, bounds.max.y, InnDoorV), 0f);
            _innDoor = renderer.transform.TransformPoint(local);
            _innExit = _grid.FirstWalkableBelow(_innDoor);
            _hasInn = true;
        }

        /// <summary>
        /// 낮 동안 용병마다 정해진 시각이 되면 주사위를 굴린다: 안에 있으면 나올지, 밖에 있으면 들어갈지.
        /// 안 = GameObject 꺼짐. 들어가는 중인 용병은 건드리지 않는다.
        /// </summary>
        /// <summary>훈련소 그림의 아래 가운데(발밑)를 문으로, 그 아래 처음 걸을 수 있는 지점을 문 앞으로 정한다. 없으면 훈련 드나들기 연출 없음.</summary>
        private void FindTrainingHallDoor()
        {
            var renderer = VillageProps.FindRenderer(TrainingHallSpritePrefix);
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
                wanderer.ExitBuilding(_innDoor, _innExit);
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
                float hours = RandomRange(TrainingHall.MinSessionHours, TrainingHall.MaxSessionHours);
                var buddy = ClosestOutsideFriend(merc, pair.Key); // Refresh가 _wanderers를 바꾸기 전에 고른다
                if (!TrainingHall.TryAdd(merc, hours)) continue;
                // 친한 동료가 밖에 있으면 관계 단계의 동행 확률로 같이 훈련하러 간다(정원·피로는 TrainingHall이 거절).
                if (buddy.merc != null && _rng.NextDouble() < Affinity.TierOf(buddy.value).TogetherChance && TrainingHall.TryAdd(buddy.merc, hours))
                    ToastLog.Show($"{merc.Name}와(과) {buddy.merc.Name}이(가) 같이 훈련하러 간다");
                return; // 한 번에 한 무리(Refresh가 _wanderers를 바꾼다)
            }
        }

        /// <summary>
        /// 밖에 나와 걷는 용병이 가끔 스스로 쉼터(온천·도박장)에 간다. 사람마다 결정 시각이 되면 마을에 있는 쉼터를 무작위 순서로
        /// 하나씩 RestVenue.VisitChance(온천은 피로, 도박장은 낮은 사기·피로 비례) 확률로 굴려 처음 걸린 곳에 등록하고,
        /// 등록되면 Refresh가 그 문까지 걸어 들어가게 한다. 친한 동료도 그 쉼터에 갈 만하면(VisitChance > 0) 같이 갈 수 있다.
        /// </summary>
        private void UpdateVenueVisits()
        {
            float now = Time.time;
            foreach (var pair in _wanderers.ToList())
            {
                var wanderer = pair.Value;
                if (wanderer == null || !wanderer.gameObject.activeSelf || wanderer.IsEntering) continue;
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
                    ToastLog.Show($"{merc.Name}와(과) {buddy.merc.Name}이(가) 같이 {venue.Name}에 간다");
                return; // 한 번에 한 무리(Refresh가 _wanderers를 바꾼다)
            }
        }

        private void UpdateInnVisits()
        {
            float now = Time.time;
            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (wanderer == null || wanderer.IsEntering) continue;
                if (_nextDecision.TryGetValue(pair.Key, out float at) && now < at) continue;

                bool inside = !wanderer.gameObject.activeSelf;
                if (inside)
                {
                    if (now < _doorFreeAt) continue; // 다음 프레임에 다시 굴린다
                    _nextDecision[pair.Key] = now + RandomRange(InsideRestMin, InsideRestMax);
                    if (_rng.NextDouble() >= ComeOutChance) continue;
                    wanderer.gameObject.SetActive(true);
                    wanderer.ExitBuilding(_innDoor, _innExit);
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
            wanderer.ReturnInto(_innExit, _innDoor, () =>
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
            wanderer.ExitBuilding(_innDoor, _innExit);
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
                if (_hidden)
                {
                    Destroy(wanderer.gameObject);
                    continue;
                }
                if (!wanderer.gameObject.activeSelf)
                {
                    wanderer.gameObject.SetActive(true);
                    if (_hasInn) wanderer.PlaceAt(_innDoor); // 문간에서 출발 → 가장 가까운 바깥 칸으로 먼저 나온다
                }
                var leaving = wanderer;
                var gate = CastleGate.Expedition;
                // 성문이 있으면 출구를 지나 성문으로 성벽 밖까지 걸어 나간다(가까이 가면 성문이 열린다).
                wanderer.WalkAndVanish(_villageExit, () => { if (leaving != null) Destroy(leaving.gameObject); },
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
            }

            if (target != _hovered)
            {
                if (_hovered != null) _hovered.SetHighlighted(false);
                _hovered = target;
                if (_hovered != null) _hovered.SetHighlighted(true);
            }

            if (_hovered != null && mouse.leftButton.wasPressedThisFrame
                && _hovered.OwnerId != null && _mercById.TryGetValue(_hovered.OwnerId, out var merc))
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

        private void OnDestroy()
        {
            if (!_subscribed) return;
            PlayerParty.Instance.OnChanged -= Refresh;
            ExpeditionLog.Instance.OnChanged -= Refresh;
            TrainingHall.OnChanged -= Refresh;
            Hospital.OnChanged -= Refresh;
            RestVenues.OnAnyChanged -= Refresh;
            TimeAdvanceController.Midpoint -= ShuffleAfterTimeSkip;
        }

        /// <summary>
        /// "진행"으로 3시간이 지나는 동안(화면이 가장 어두운 순간) 마을 사람들의 자리를 새로 정한다.
        /// 여관이 있으면 반반 확률로 밖(마을 아무 곳에서 쉬는 중) 또는 안(여관에서 쉬는 중)으로 다시 나눈다. 밤이면 그대로.
        /// </summary>
        private void ShuffleAfterTimeSkip()
        {
            if (_hidden || _grid == null) return;
            float now = Time.time;
            foreach (var pair in _wanderers)
            {
                var wanderer = pair.Value;
                if (wanderer == null) continue;
                bool outside = !_hasInn || IsAllOutTime() || _rng.NextDouble() < ShuffleOutsideChance;
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
                        if (_hasTrainingHall && entering.gameObject.activeSelf)
                            entering.ReturnInto(_trainingExit, _trainingDoor, () => { if (entering != null) Destroy(entering.gameObject); });
                        else
                            Destroy(entering.gameObject);
                    }
                }
                else if (merc != null && merc.IsAlive && Hospital.IsAdmitted(merc))
                {
                    // 입원: 밖에 나와 있으면 병원 문으로 걸어 들어가 사라진다.
                    _hospitalIds.Add(id);
                    if (wanderer != null)
                    {
                        var entering = wanderer;
                        if (_hasHospital && entering.gameObject.activeSelf)
                            entering.ReturnInto(_hospitalExit, _hospitalDoor, () => { if (entering != null) Destroy(entering.gameObject); });
                        else
                            Destroy(entering.gameObject);
                    }
                }
                else if (merc != null && merc.IsAlive && RestVenues.Find(merc) is RestVenue venue)
                {
                    // 쉼터(온천·도박장): 밖에 나와 있으면 그 문으로 걸어 들어가 사라진다.
                    _restingIn[id] = venue;
                    if (wanderer != null)
                    {
                        var entering = wanderer;
                        if (_venueDoors.TryGetValue(venue, out var doors) && entering.gameObject.activeSelf)
                            entering.ReturnInto(doors.exit, doors.door, () => { if (entering != null) Destroy(entering.gameObject); });
                        else
                            Destroy(entering.gameObject);
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
            if (departing.Count > 0)
            {
                ToastLog.Show($"파견대 출발: {string.Join(", ", departing.Select(w => w.gameObject.name.Replace("Wanderer_", "")))}"
                              + (departingQuest != null ? $" → {departingQuest}" : ""));
                StartCoroutine(MarchOut(departing));
            }

            int returning = 0;
            foreach (var merc in present)
            {
                if (_wanderers.ContainsKey(merc.Id)) continue;

                var go = new GameObject($"Wanderer_{merc.Name}");
                go.transform.position = _grid.RandomWalkablePoint(_rng);
                var wanderer = go.AddComponent<VillageWanderer>();
                wanderer.Init(merc.Appearance, _grid, _rng, _shadowMaterial);
                wanderer.OwnerId = merc.Id;
                _mercById[merc.Id] = merc;
                // 여관이 있으면 새로 온 용병은 여관 안에서 시작해 곧 무작위로 나온다.
                if (_trainingIds.Remove(merc.Id) && _hasTrainingHall && !_hidden)
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
                    go.SetActive(!_hidden && !_hasInn);
                    _nextDecision[merc.Id] = Time.time + RandomRange(1f, 4f);
                }
                VillageNameTag.Create(_tagLayer, go.transform, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}"); // Wanderer가 사라지면 스스로 지워진다
                _wanderers[merc.Id] = wanderer;
            }
        }
    }
}
