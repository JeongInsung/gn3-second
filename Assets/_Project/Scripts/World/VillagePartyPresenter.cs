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
    /// 마을에 여관이 있으면 용병들은 여관에서 쉬다가 낮 동안 무작위로 문에서 나와 돌아다니고, 또 무작위로 들어간다
    /// (늘 일부는 안에서 쉰다). 저녁·밤에도 자정까지는 돌아다니고, 자정~아침(어두운 동안)에는 모두 안에서 잔다.
    /// 여관이 없으면 밝을 때 전원이 숨기 전 자리에서 나타난다.
    /// MainMenuBootstrapper가 MainScene에서 만든다.
    /// </summary>
    public class VillagePartyPresenter : MonoBehaviour
    {
        private const float AreaMargin = 0.4f;
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
        private const float ShuffleOutsideChance = 0.3f; // 진행으로 시간이 건너뛸 때 밖에 나와 있을 확률

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
        private const float MarchGap = 0.4f;                          // 파견대가 한 줄로 나가는 간격
        private readonly Dictionary<string, float> _nextDecision = new Dictionary<string, float>();
        private float _doorFreeAt;

        private void Start()
        {
            var floor = FindFirstObjectByType<Tilemap>();
            if (floor == null)
            {
                Debug.LogWarning("[VillagePartyPresenter] 마을 바닥 Tilemap을 찾지 못해 용병을 마을에 내보내지 않습니다. (Village 프리팹이 씬에 있는지 확인)");
                return;
            }

            floor.CompressBounds();
            var local = floor.localBounds;
            Vector3 min = floor.transform.TransformPoint(local.min);
            Vector3 max = floor.transform.TransformPoint(local.max);
            _area = Rect.MinMaxRect(min.x + AreaMargin, min.y + AreaMargin, max.x - AreaMargin, max.y - AreaMargin);

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
            _infoPanel = MercenaryInfoPanel.Create(transform);

            PlayerParty.Instance.OnChanged += Refresh;
            ExpeditionLog.Instance.OnChanged += Refresh;
            TimeAdvanceController.Midpoint += ShuffleAfterTimeSkip;
            _subscribed = true;
            Refresh();
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

            if (!_hidden && _hasInn) UpdateInnVisits();

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
                }
                else
                {
                    _nextDecision[pair.Key] = now + RandomRange(OutsideWanderMin, OutsideWanderMax);
                    if (_rng.NextDouble() >= GoInChance) continue;
                    string id = pair.Key;
                    wanderer.ReturnInto(_innExit, _innDoor, () =>
                    {
                        if (wanderer != null) wanderer.gameObject.SetActive(false);
                        _nextDecision[id] = Time.time + RandomRange(InsideRestMin, InsideRestMax);
                    });
                }
            }
        }

        /// <summary>
        /// 파견대가 한 명씩 MarchGap초 간격으로 마을 출구까지 걸어 나가 사라진다. 여관 안에 있던 사람은 문에서 나와 출발한다.
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
                wanderer.WalkAndVanish(_villageExit, () => { if (leaving != null) Destroy(leaving.gameObject); });
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
                _infoPanel.Show(merc);
        }

        /// <summary>보던 용병이 마을에서 사라지면(파견·해고·사망·밤) 창을 닫는다.</summary>
        private void CloseInfoIfGone()
        {
            var shown = _infoPanel.Shown;
            if (shown == null) return;
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
                bool outside = !_hasInn || _rng.NextDouble() < ShuffleOutsideChance;
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
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m))
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
                else if (wanderer != null)
                {
                    Destroy(wanderer.gameObject); // 해고·사망
                }
                _wanderers.Remove(id);
                _mercById.Remove(id);
                _nextDecision.Remove(id);
            }
            if (departing.Count > 0)
            {
                ToastLog.Show($"파견대 출발: {string.Join(", ", departing.Select(w => w.gameObject.name.Replace("Wanderer_", "")))}"
                              + (departingQuest != null ? $" → {departingQuest}" : ""));
                StartCoroutine(MarchOut(departing));
            }

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
                if (_awayIds.Remove(merc.Id) && !_hidden)
                {
                    // 파견에서 돌아온 용병: 마을 출구에서 걸어 들어온다.
                    go.SetActive(true);
                    wanderer.WalkInFrom(_villageExit, _grid.RandomWalkablePoint(_rng));
                    _nextDecision[merc.Id] = Time.time + RandomRange(OutsideWanderMin, OutsideWanderMax);
                }
                else
                {
                    go.SetActive(!_hidden && !_hasInn);
                    _nextDecision[merc.Id] = Time.time + RandomRange(1f, 4f);
                }
                VillageNameTag.Create(_tagLayer, go.transform, merc.Name); // Wanderer가 사라지면 스스로 지워진다
                _wanderers[merc.Id] = wanderer;
            }
        }
    }
}
