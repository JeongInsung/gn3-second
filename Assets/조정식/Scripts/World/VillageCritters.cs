using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을의 작은 생물: 가끔 화면을 가로질러 날아가는 새 무리와, 꽃 주위를 맴도는 나비. 낮에만 보인다.
    /// 새는 높이 나는 느낌이 나게 아래쪽으로 떨어진 그림자를 같이 끌고 간다(ProjectedShadow처럼 땅에 비친 그림자).
    /// 나비는 이름에 flowerKeywords가 든 물체(꽃화분·야생화)를 찾아 그 위를 맴돌다 가끔 다른 꽃으로 옮겨 간다.
    /// </summary>
    public class VillageCritters : MonoBehaviour
    {
        [Tooltip("Sprite-Lit-Default. 해질녘 조명에 맞춰 같이 어두워지게")]
        [SerializeField] private Material litMaterial;

        [Header("새")]
        [Tooltip("새 무리가 나타나는 간격(초) 최소~최대")]
        [SerializeField] private Vector2 birdInterval = new Vector2(15f, 35f);
        [SerializeField] private int maxBirdsPerFlock = 2;
        [SerializeField] private float birdSpeed = 2.2f;
        [SerializeField] private Color birdColor = new Color(0.22f, 0.2f, 0.24f);
        [Tooltip("새 그림자가 새에서 떨어진 거리(월드). 멀수록 높이 나는 것처럼 보인다")]
        [SerializeField] private Vector2 birdShadowOffset = new Vector2(0.35f, -1.1f);
        [Range(0f, 1f)] [SerializeField] private float birdShadowOpacity = 0.25f;

        [Header("나비")]
        [SerializeField] private int butterflyCount = 2;
        [SerializeField] private string[] flowerKeywords = { "꽃이 핀 나무 화분", "흰 데이지와 주황 야생화 묶음" };
        [SerializeField] private Color[] butterflyColors =
        {
            new Color(1f, 0.98f, 0.9f),
            new Color(1f, 0.85f, 0.3f),
            new Color(0.65f, 0.85f, 1f),
        };

        [SerializeField] private int sortingOrder = 96; // 새 = +2, 새 그림자 = +1, 나비 = +0

        private class Bird
        {
            public SpriteRenderer Body;
            public SpriteRenderer Shadow;
            public Vector2 Offset;   // 무리 중심에서의 자리
            public float FlapPhase;
        }

        private class Flock
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public readonly List<Bird> Birds = new List<Bird>();
        }

        private class Butterfly
        {
            public SpriteRenderer Renderer;
            public Vector2 Home;
            public float Seed;
            public float NextMove;
            public float FlapPhase;
        }

        private readonly List<Flock> _flocks = new List<Flock>();
        private readonly List<Butterfly> _butterflies = new List<Butterfly>();
        private readonly List<Vector2> _flowers = new List<Vector2>();
        private Sprite[] _birdFrames;
        private Sprite[] _butterflyFrames;
        private Rect _view;
        private float _nextFlock;

        public void Configure(Material lit) => litMaterial = lit;

        private void Start()
        {
            var cam = Camera.main;
            if (cam == null || litMaterial == null)
            {
                enabled = false;
                return;
            }
            _view = PixelSprites.ViewRect(cam);
            float dot = PixelSprites.ArtDot(cam);

            // 위에서 내려다본 새: 날개 편 모양 / 접은 모양
            _birdFrames = PixelSprites.Frames("Bird", new[]
            {
                new[] { "bb...bb", ".bb.bb.", "..bbb..", "...b..." },
                new[] { ".......", "..bbb..", ".bbbbb.", "...b..." },
            }, "b", new[] { Color.white }, dot);
            // 나비: 날개 펼침 / 날개 세움(좁아 보임). 날개는 흰색으로 그리고 나비마다 색을 입힌다.
            // 테두리(e)를 어둡게 둬야 흰 꽃·밝은 돌바닥 위에서도 나비 모양이 보인다.
            _butterflyFrames = PixelSprites.Frames("Butterfly", new[]
            {
                new[] { "ee...ee", "ewwkwwe", ".ewkwe.", "..eke.." },
                new[] { ".......", "..ewe..", "..eke..", "...k..." },
            }, "wek", new[] { Color.white, new Color(0.45f, 0.4f, 0.4f), new Color(0.2f, 0.15f, 0.15f) }, dot);

            FindFlowers();
            for (int i = 0; i < butterflyCount && _flowers.Count > 0; i++) SpawnButterfly(i);
            _nextFlock = Time.time + Random.Range(2f, birdInterval.x); // 첫 무리는 조금 일찍
        }

        private void Update()
        {
            float day = 1f - DayNightCycle.NightLightFactor;

            if (Time.time >= _nextFlock)
            {
                if (day > 0.5f) SpawnFlock();
                _nextFlock = Time.time + Random.Range(birdInterval.x, birdInterval.y);
            }
            UpdateFlocks(day);
            UpdateButterflies(day);
        }

        private void FindFlowers()
        {
            foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.GetComponent<ProjectedShadow>() != null) continue;
                foreach (var keyword in flowerKeywords)
                {
                    if (!renderer.name.Contains(keyword)) continue;
                    var bounds = renderer.bounds;
                    _flowers.Add(new Vector2(bounds.center.x, bounds.center.y + bounds.extents.y * 0.5f)); // 잎·꽃 쪽
                    break;
                }
            }
        }

        private void SpawnFlock()
        {
            // 바람 방향에서 ±25° 틀어진 방향으로, 화면 반대편 밖에서 들어와 가로지른다
            float angle = Mathf.Atan2(VillageWind.Direction.y, VillageWind.Direction.x) + Random.Range(-25f, 25f) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (Random.value < 0.3f) direction = -direction; // 가끔은 바람을 거슬러
            var side = new Vector2(-direction.y, direction.x);
            float reach = _view.size.magnitude * 0.5f + 1f;

            var flock = new Flock
            {
                Position = _view.center - direction * reach + side * Random.Range(-_view.height * 0.4f, _view.height * 0.4f),
                Velocity = direction * birdSpeed * Random.Range(0.85f, 1.15f),
            };
            int count = Random.Range(1, maxBirdsPerFlock + 1);
            for (int i = 0; i < count; i++)
            {
                // V자 대형: 앞장서는 한 마리 뒤로 양옆에 번갈아
                int rank = (i + 1) / 2;
                float sideSign = i % 2 == 0 ? 1f : -1f;
                var offset = i == 0 ? Vector2.zero : -direction * rank * 0.35f + side * sideSign * rank * 0.3f;
                flock.Birds.Add(CreateBird(offset));
            }
            _flocks.Add(flock);
        }

        private Bird CreateBird(Vector2 offset)
        {
            var body = CreateRenderer("Bird", _birdFrames[0], sortingOrder + 2);
            var shadow = CreateRenderer("Bird Shadow", _birdFrames[0], sortingOrder + 1);
            return new Bird { Body = body, Shadow = shadow, Offset = offset, FlapPhase = Random.value };
        }

        private void UpdateFlocks(float day)
        {
            for (int f = _flocks.Count - 1; f >= 0; f--)
            {
                var flock = _flocks[f];
                flock.Position += flock.Velocity * Time.deltaTime;
                float rotation = Mathf.Atan2(flock.Velocity.y, flock.Velocity.x) * Mathf.Rad2Deg - 90f; // 그림은 위쪽(머리)이 앞
                bool gone = true;

                foreach (var bird in flock.Birds)
                {
                    var position = flock.Position + bird.Offset;
                    // 날갯짓 몇 번 → 잠깐 활공을 반복
                    float t = Time.time * 1.2f + bird.FlapPhase;
                    bool gliding = Mathf.Repeat(t, 1f) > 0.6f;
                    var frame = gliding ? _birdFrames[0] : _birdFrames[(int)(Time.time * 8f + bird.FlapPhase * 2f) % 2];

                    bird.Body.sprite = frame;
                    bird.Body.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
                    bird.Body.color = WithAlpha(birdColor, day);
                    bird.Shadow.sprite = frame;
                    var shadowPosition = position + birdShadowOffset;
                    bird.Shadow.transform.SetPositionAndRotation(shadowPosition, Quaternion.Euler(0f, 0f, rotation));
                    bird.Shadow.color = new Color(0f, 0f, 0f, birdShadowOpacity * day);

                    if (_view.Overlaps(Around(position)) || _view.Overlaps(Around(shadowPosition))) gone = false;
                }

                // 화면 안으로 들어온 적이 있고, 이제 새와 그림자 모두 화면 밖으로 나가 멀어지는 중이면 정리
                if (gone && Vector2.Dot(flock.Velocity, flock.Position - _view.center) > 0f)
                {
                    foreach (var bird in flock.Birds)
                    {
                        Destroy(bird.Body.gameObject);
                        Destroy(bird.Shadow.gameObject);
                    }
                    _flocks.RemoveAt(f);
                }
            }
        }

        private static Rect Around(Vector2 position) => new Rect(position.x - 0.5f, position.y - 0.5f, 1f, 1f);

        private void SpawnButterfly(int index)
        {
            var renderer = CreateRenderer("Butterfly", _butterflyFrames[0], sortingOrder);
            renderer.color = butterflyColors[index % butterflyColors.Length];
            var butterfly = new Butterfly
            {
                Renderer = renderer,
                Home = _flowers[Random.Range(0, _flowers.Count)],
                Seed = Random.Range(0f, 100f),
                NextMove = Time.time + Random.Range(6f, 15f),
                FlapPhase = Random.value,
            };
            renderer.transform.position = butterfly.Home;
            _butterflies.Add(butterfly);
        }

        private void UpdateButterflies(float day)
        {
            foreach (var butterfly in _butterflies)
            {
                if (Time.time >= butterfly.NextMove)
                {
                    butterfly.Home = _flowers[Random.Range(0, _flowers.Count)]; // 다른 꽃으로(같은 꽃일 수도)
                    butterfly.NextMove = Time.time + Random.Range(6f, 15f);
                }

                // 꽃 위를 불규칙하게 맴돈다
                float t = Time.time * 0.6f;
                var wander = new Vector2(
                    (Mathf.PerlinNoise(butterfly.Seed, t) - 0.5f) * 0.9f,
                    (Mathf.PerlinNoise(butterfly.Seed + 31f, t) - 0.5f) * 0.6f);
                var target = butterfly.Home + wander;
                var body = butterfly.Renderer.transform;
                body.position = Vector2.MoveTowards(body.position, target, 0.9f * Time.deltaTime);

                int frame = (int)(Time.time * 10f + butterfly.FlapPhase * 2f) % 2;
                butterfly.Renderer.sprite = _butterflyFrames[frame];
                butterfly.Renderer.color = WithAlpha(butterfly.Renderer.color, day);
            }
        }

        private SpriteRenderer CreateRenderer(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = litMaterial;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
