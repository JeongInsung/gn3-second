using System.Collections.Generic;
using GN3.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace GN3.World
{
    /// <summary>
    /// 마을 잔디·길을 돌아다니는 강아지들. 에디터 메뉴(GN3/Village/강아지 만들기)가 걷기 프레임 8장과 그림자 재질을 넣어 두고,
    /// Play 때 이 컴포넌트가 강아지를 만든다(에디터에서는 아무것도 만들지 않는다).
    /// 걷는 범위는 3×3 마을 바닥의 가운데 칸(VillageBounds.Center)에서 여백을 뺀 곳이고(용병 걷기 영역과 같은 규칙),
    /// 건물·장식은 VillageNavGrid로 피한다. 밤(NightLightFactor가 용병 숨김 기준을 넘으면)에는 들어가 잔다.
    /// </summary>
    public class VillageDogs : MonoBehaviour
    {
        private const float AreaMargin = 0.5f;
        private const float ScreenMarginSide = 0.3f;
        private const float ScreenMarginBottom = 0.15f;
        private const float ScreenMarginTop = 0.4f;
        private const float HideNightLightFactor = 0.5f; // VillagePartyPresenter와 같은 값

        [SerializeField] private Sprite[] walkFrames;   // 오른쪽을 보고 걷는 8프레임(0번 = 서 있을 때)
        [SerializeField] private Material shadowMaterial;
        [SerializeField, Min(0)] private int dogCount = 1;

        private readonly List<VillageDog> _dogs = new List<VillageDog>();
        private bool _hidden;
        private bool _wasSkipping;
        private VillageNavGrid _grid;
        private System.Random _rng;

        public void Configure(Sprite[] frames, Material shadow, int count)
        {
            walkFrames = frames;
            shadowMaterial = shadow;
            dogCount = count;
        }

        private void Start()
        {
            if (walkFrames == null || walkFrames.Length == 0) return;
            if (!TryGetArea(out var area))
            {
                Debug.LogWarning("[VillageDogs] 마을 바닥 Tilemap을 찾지 못해 강아지를 내보내지 않습니다.");
                return;
            }

            var grid = _grid = VillageNavGrid.Build(area, VillageNavGrid.CollectObstacles());
            var rng = _rng = new System.Random();
            for (int i = 0; i < dogCount; i++)
            {
                var go = new GameObject($"강아지 {i + 1}");
                go.transform.SetParent(transform, false);
                Vector2 start = grid.RandomWalkablePoint(rng);
                go.transform.position = new Vector3(start.x, start.y, 0f);
                var dog = go.AddComponent<VillageDog>();
                dog.Init(walkFrames, shadowMaterial, grid, new System.Random(rng.Next()));
                _dogs.Add(dog);
            }
            _hidden = !IsDaytime();
            foreach (var dog in _dogs) dog.gameObject.SetActive(!_hidden);
        }

        private void OnEnable()
        {
            TimeAdvanceController.Midpoint += RespawnAll;
            VillageLayoutApplier.Rebuilt += RebuildGrid;
        }

        private void OnDisable()
        {
            TimeAdvanceController.Midpoint -= RespawnAll;
            VillageLayoutApplier.Rebuilt -= RebuildGrid;
        }

        /// <summary>건물을 지었으면 같은 격자를 다시 막는다(강아지들이 이 격자를 들고 있다).</summary>
        private void RebuildGrid() => _grid?.Rebuild(VillageNavGrid.CollectObstacles());

        private void Update()
        {
            // 하루·4일·일주일 건너뛰기 중에는 숨고, 끝나면 마을 아무 곳에 다시 나타난다(용병과 같은 규칙)
            bool skipping = TimeAdvanceController.IsSkippingDays;
            if (_wasSkipping && !skipping) RespawnAll();
            _wasSkipping = skipping;

            bool hidden = !IsDaytime() || skipping;
            if (hidden == _hidden) return;
            _hidden = hidden;
            foreach (var dog in _dogs)
                if (dog != null) dog.gameObject.SetActive(!_hidden);
        }

        /// <summary>시간을 넘긴 뒤(몇 시간 진행의 가장 어두운 순간, 건너뛰기 끝): 강아지를 걸을 수 있는 무작위 자리에 둔다.</summary>
        private void RespawnAll()
        {
            if (_grid == null) return;
            foreach (var dog in _dogs)
                if (dog != null) dog.PlaceAt(_grid.RandomWalkablePoint(_rng));
        }

        private static bool IsDaytime() => DayNightCycle.NightLightFactor <= HideNightLightFactor;

        /// <summary>가운데 칸(VillageBounds.Center)에서 여백을 뺀 곳 — 용병 걷기 영역과 같은 기준.</summary>
        private static bool TryGetArea(out Rect area)
        {
            area = default;
            if (!VillageBounds.Exists) return false;
            var world = VillageBounds.Center;
            area = Rect.MinMaxRect(world.xMin + AreaMargin + ScreenMarginSide, world.yMin + AreaMargin + ScreenMarginBottom,
                                   world.xMax - AreaMargin - ScreenMarginSide, world.yMax - AreaMargin - ScreenMarginTop);
            return true;
        }
    }

    /// <summary>
    /// 강아지 한 마리: 잠깐 서 있다가(0프레임) 근처 목표까지 길을 찾아 걷는다. 가끔은 빠르게 뛴다.
    /// 구조는 VillageWanderer와 같다: 루트(발 위치) → Visual(SortingGroup order 1, Y축 정렬) → 그림, 그룹 밖에 그림자.
    /// </summary>
    public class VillageDog : MonoBehaviour
    {
        private const int SortingOrder = 1;
        private const float WalkSpeed = 0.6f;          // 유닛/초
        private const float FramesPerSecond = 10f;     // 8프레임 한 바퀴 0.8초 → 발이 덜 미끄러지는 값
        private const float RunChance = 0.25f;
        private const float RunMultiplier = 1.6f;
        private const float MinIdleSeconds = 1f;
        private const float MaxIdleSeconds = 4f;
        private const float MinWalkDistance = 0.5f;
        private const float MaxWalkDistance = 2.5f;
        private const int WalkAttempts = 8;
        private const float MaxDetourRatio = 2f;
        private const float ShadowHeightScale = 0.5f;

        private Sprite[] _frames;
        private SpriteRenderer _renderer;
        private VillageNavGrid _grid;
        private System.Random _rng;
        private readonly List<Vector2> _path = new List<Vector2>();
        private int _pathIndex;
        private bool _walking;
        private float _idleTimer;
        private float _speedMultiplier = 1f;
        private float _frameTime;

        public void Init(Sprite[] frames, Material shadowMaterial, VillageNavGrid grid, System.Random rng)
        {
            _frames = frames;
            _grid = grid;
            _rng = rng;

            var visual = new GameObject("Visual", typeof(SortingGroup));
            visual.transform.SetParent(transform, false);
            visual.GetComponent<SortingGroup>().sortingOrder = SortingOrder;

            var art = new GameObject("Dog", typeof(SpriteRenderer));
            art.transform.SetParent(visual.transform, false);
            _renderer = art.GetComponent<SpriteRenderer>();
            _renderer.sprite = frames[0];
            _renderer.flipX = rng.NextDouble() < 0.5;

            if (shadowMaterial != null)
            {
                // 그림자를 SortingGroup 안에 두면 그룹 order(1)로 묶여 건물 위에 그려지므로 밖에 둔다(VillageWanderer와 같음).
                var shadowGO = new GameObject("Shadow", typeof(SpriteRenderer));
                shadowGO.transform.SetParent(transform, false);
                shadowGO.GetComponent<SpriteRenderer>().sharedMaterial = shadowMaterial;
                var shadow = shadowGO.AddComponent<ProjectedShadow>();
                shadow.sourceOverride = _renderer;
                shadow.groundAnchor = transform;
                shadow.heightScale = ShadowHeightScale;
            }

            StartIdle();
        }

        /// <summary>이 자리로 옮겨 서 있게 한다(걷던 길은 버린다).</summary>
        public void PlaceAt(Vector2 point)
        {
            transform.position = new Vector3(point.x, point.y, transform.position.z);
            _renderer.flipX = _rng.NextDouble() < 0.5;
            StartIdle();
        }

        private void StartIdle()
        {
            _walking = false;
            _idleTimer = Mathf.Lerp(MinIdleSeconds, MaxIdleSeconds, (float)_rng.NextDouble());
            _renderer.sprite = _frames[0];
        }

        private void StartWalk()
        {
            Vector2 from = _grid.NearestWalkable(transform.position);
            transform.position = new Vector3(from.x, from.y, transform.position.z);
            for (int attempt = 0; attempt < WalkAttempts; attempt++)
            {
                float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Lerp(MinWalkDistance, MaxWalkDistance, (float)_rng.NextDouble());
                Vector2 target = from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (!_grid.IsWalkable(target) || !_grid.TryFindPath(from, target, _path)) continue;
                if (PathLength(from) > distance * MaxDetourRatio) continue;

                _pathIndex = 0;
                _walking = true;
                _speedMultiplier = _rng.NextDouble() < RunChance ? RunMultiplier : 1f;
                _frameTime = 0f;
                FaceTowards(_path[0]);
                return;
            }
            StartIdle();
        }

        private float PathLength(Vector2 from)
        {
            float length = 0f;
            Vector2 previous = from;
            foreach (var point in _path) { length += Vector2.Distance(previous, point); previous = point; }
            return length;
        }

        private void FaceTowards(Vector2 target)
        {
            float dx = target.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.01f) _renderer.flipX = dx < 0f; // 시트는 오른쪽을 본다
        }

        private void Update()
        {
            // Play 중 스크립트를 다시 불러오면 직렬화되지 않는 길찾기·난수가 비어 있다 → 그 자리에 멈춰 있는다(새 Play면 정상).
            if (_frames == null || _grid == null || _rng == null) return;
            if (!_walking)
            {
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f) StartWalk();
                return;
            }

            Vector2 waypoint = _path[_pathIndex];
            Vector2 next = Vector2.MoveTowards(transform.position, waypoint, WalkSpeed * _speedMultiplier * Time.deltaTime);
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            _frameTime += Time.deltaTime * _speedMultiplier;
            _renderer.sprite = _frames[Mathf.FloorToInt(_frameTime * FramesPerSecond) % _frames.Length];

            if (next != waypoint) return;
            _pathIndex++;
            if (_pathIndex < _path.Count) FaceTowards(_path[_pathIndex]);
            else StartIdle();
        }
    }
}
