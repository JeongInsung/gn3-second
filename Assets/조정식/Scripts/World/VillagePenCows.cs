using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GN3.World
{
    /// <summary>
    /// 가축 우리 안을 천천히 거니는 소들. 에디터 메뉴(GN3/Village/소 만들기)가 걷기 프레임 8장·그림자 재질·우리 안쪽 범위를 넣어 두고,
    /// Play 때 이 컴포넌트가 소를 만든다(에디터에서는 아무것도 만들지 않는다). 우리 안은 비어 있어 길찾기 없이 직선으로 걷는다.
    /// </summary>
    public class VillagePenCows : MonoBehaviour
    {
        private const float MinSpawnSpacing = 0.9f;
        private const int SpawnAttempts = 30;

        [SerializeField] private Sprite[] walkFrames;   // 오른쪽을 보고 걷는 8프레임
        [SerializeField] private Sprite idleSprite;     // 서 있을 때(다리를 모은 그림). 없으면 걷기 0번 프레임
        [SerializeField] private Material shadowMaterial;
        [SerializeField, Min(0)] private int cowCount = 3;
        [SerializeField] private Rect area;             // 우리 안쪽(월드 좌표, 소 발 위치가 머무는 범위)
        [SerializeField] private Rect[] avoid = new Rect[0]; // 여물통처럼 소가 서지 않을 자리

        public void Configure(Sprite[] frames, Sprite idle, Material shadow, int count, Rect penArea, Rect[] avoidRects)
        {
            walkFrames = frames;
            idleSprite = idle;
            shadowMaterial = shadow;
            cowCount = count;
            area = penArea;
            avoid = avoidRects ?? new Rect[0];
        }

        private void Start()
        {
            if (walkFrames == null || walkFrames.Length == 0 || area.width <= 0f || area.height <= 0f) return;

            var rng = new System.Random();
            var placed = new List<Vector2>();
            for (int i = 0; i < cowCount; i++)
            {
                Vector2 start = RandomPoint(rng);
                for (int attempt = 0; attempt < SpawnAttempts && placed.Exists(p => Vector2.Distance(p, start) < MinSpawnSpacing); attempt++)
                    start = RandomPoint(rng);
                placed.Add(start);

                var go = new GameObject($"소 {i + 1}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(start.x, start.y, 0f);
                go.AddComponent<VillagePenCow>().Init(walkFrames, idleSprite, shadowMaterial, this, new System.Random(rng.Next()));
            }
        }

        /// <summary>우리 안에서 피할 자리를 뺀 무작위 지점.</summary>
        internal Vector2 RandomPoint(System.Random rng)
        {
            Vector2 point = default;
            for (int attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                point = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)rng.NextDouble()),
                                    Mathf.Lerp(area.yMin, area.yMax, (float)rng.NextDouble()));
                bool blocked = false;
                foreach (var rect in avoid) blocked |= rect.Contains(point);
                if (!blocked) break;
            }
            return point;
        }
    }

    /// <summary>
    /// 소 한 마리: 한동안 서 있다가(다리를 모은 그림) 우리 안 무작위 지점까지 천천히 걷는다. 밤에는 걷지 않고 서 있는다.
    /// 구조는 VillageDog와 같다: 루트(발 위치) → Visual(SortingGroup) → 그림, 그룹 밖에 그림자.
    /// SortingGroup order 2 = 우리 위 울타리(1) 앞, 아래 울타리(3) 뒤.
    /// </summary>
    public class VillagePenCow : MonoBehaviour
    {
        private const int SortingOrder = 2;
        private const float WalkSpeed = 0.09f;         // 유닛/초. 프레임 속도와 같은 비율로 낮춰 발이 미끄러지지 않게
        private const float FramesPerSecond = 3f;      // 8프레임 한 바퀴 약 2.7초(느릿느릿)
        private const float MinIdleSeconds = 2f;
        private const float MaxIdleSeconds = 6f;
        private const float MinWalkDistance = 0.4f;
        private const float ShadowHeightScale = 0.5f;
        private const float HideNightLightFactor = 0.5f; // VillageDogs와 같은 밤 기준

        private Sprite[] _frames;
        private Sprite _idle;
        private SpriteRenderer _renderer;
        private VillagePenCows _pen;
        private System.Random _rng;
        private Vector2 _target;
        private bool _walking;
        private float _idleTimer;
        private float _frameTime;

        public void Init(Sprite[] frames, Sprite idle, Material shadowMaterial, VillagePenCows pen, System.Random rng)
        {
            _frames = frames;
            _idle = idle != null ? idle : frames[0];
            _pen = pen;
            _rng = rng;

            var visual = new GameObject("Visual", typeof(SortingGroup));
            visual.transform.SetParent(transform, false);
            visual.GetComponent<SortingGroup>().sortingOrder = SortingOrder;

            var art = new GameObject("Cow", typeof(SpriteRenderer));
            art.transform.SetParent(visual.transform, false);
            _renderer = art.GetComponent<SpriteRenderer>();
            _renderer.sprite = _idle;
            _renderer.flipX = rng.NextDouble() < 0.5;

            if (shadowMaterial != null)
            {
                // 그림자를 SortingGroup 안에 두면 그룹 order로 묶여 울타리 위에 그려지므로 밖에 둔다(VillageDog와 같음).
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

        private void StartIdle()
        {
            _walking = false;
            _idleTimer = Mathf.Lerp(MinIdleSeconds, MaxIdleSeconds, (float)_rng.NextDouble());
            _renderer.sprite = _idle;
        }

        private void StartWalk()
        {
            _target = _pen.RandomPoint(_rng);
            if (Vector2.Distance(transform.position, _target) < MinWalkDistance) { StartIdle(); return; }
            _walking = true;
            _frameTime = 0f;
            float dx = _target.x - transform.position.x;
            if (Mathf.Abs(dx) > 0.01f) _renderer.flipX = dx < 0f; // 시트는 오른쪽을 본다
        }

        private void Update()
        {
            if (_frames == null) return;
            if (!_walking)
            {
                if (DayNightCycle.NightLightFactor > HideNightLightFactor) return; // 밤에는 서서 쉰다
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f) StartWalk();
                return;
            }

            Vector2 next = Vector2.MoveTowards(transform.position, _target, WalkSpeed * Time.deltaTime);
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            _frameTime += Time.deltaTime;
            _renderer.sprite = _frames[Mathf.FloorToInt(_frameTime * FramesPerSecond) % _frames.Length];
            if (next == _target) StartIdle();
        }
    }
}
