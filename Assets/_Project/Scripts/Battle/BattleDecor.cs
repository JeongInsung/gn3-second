using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 바닥 꾸미기(보이는 것만, 클릭·판정과 무관). BattleGroundTiles가 바닥을 깔 때 같은 seed의 rng로 부른다(같은 퀘스트는 늘 같은 배치).
    ///  - 판 안: 꽃밭(꽃무더기를 몇 포기씩 모음)·야생화 묶음·풀 포기, 돌은 싸우는 가운데 줄을 피해 위·아래 띠에만.
    ///  - 판 밖: 좌우 여백에 같은 것을 듬성듬성(판 경계가 드러나지 않게).
    ///  - 살아 있는 것(Atmosphere 층, 유닛 위): 꽃밭 사이를 오가는 나비, 숲 가장자리·꽃밭 위의 빛 가루, 천천히 지나가는 구름 그림자.
    /// 그림은 Resources/Battle/Decor(Tools/bake_battle_decor.py가 Assets/조정식/Decorations에서 표시 크기 2배로 구운 것).
    /// </summary>
    public static class BattleDecor
    {
        private const string DecorPath = "Battle/Decor/";
        private const int FlowerKinds = 12;              // flower_01..12
        private const float Shade = 0.85f;               // 바닥 타일과 같은 밝기
        private const float LightJitter = 0.06f;

        // 판 안(1760×560) 배치
        private const int PatchCount = 7;
        private const float PatchSpacing = 220f;        // 꽃밭 중심끼리 이만큼 떨어뜨린다
        private const float PatchRadius = 60f;
        private const int WildflowerCount = 4, GrassCount = 24, StoneCount = 6;
        private const float TopBandBottom = 450f;        // 돌은 이 위(숲 쪽)나
        private const float BottomBandTop = 80f;         // 이 아래에만 둔다
        private const int Attempts = 60;                 // 한 개를 놓으려고 던져 보는 횟수

        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _sprites.Clear();

        private struct Item
        {
            public string Name;
            public Vector2 Position; // 밑동(아래 가운데)
            public float Height;
        }

        /// <summary>판 안 꾸미기. 돌려준 꽃밭 중심은 나비·빛 가루가 쓴다.</summary>
        public static List<Vector2> BuildField(Transform groundTiles, Vector2 fieldSize, System.Random rng)
        {
            var items = new List<Item>();
            var taken = new List<(Vector2 pos, float radius)>();
            var patches = new List<Vector2>();

            // 꽃밭: 중심을 서로 떨어뜨려 고르고, 둘레에 같은 꽃 1~2가지를 몇 포기씩 모은다.
            for (int i = 0; i < PatchCount * Attempts && patches.Count < PatchCount; i++)
            {
                var center = new Vector2(Range(rng, 60f, fieldSize.x - 60f), Range(rng, 30f, fieldSize.y - 50f));
                bool far = true;
                foreach (var p in patches) if (Vector2.Distance(p, center) < PatchSpacing) { far = false; break; }
                if (far) patches.Add(center);
            }
            foreach (var center in patches)
            {
                string kindA = Flower(rng), kindB = rng.NextDouble() < 0.5 ? kindA : Flower(rng);
                int count = rng.Next(3, 6);
                for (int n = 0; n < count; n++)
                {
                    float angle = Range(rng, 0f, Mathf.PI * 2f), dist = Range(rng, 0f, PatchRadius);
                    var pos = center + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist * 0.6f);
                    TryAdd(items, taken, n % 2 == 0 ? kindA : kindB, pos, Range(rng, 30f, 40f), 16f, fieldSize);
                }
            }

            Scatter(items, taken, rng, "wildflowers", WildflowerCount, 40f, 48f, 26f, fieldSize, 0f, fieldSize.y);
            Scatter(items, taken, rng, "grass", GrassCount, 26f, 36f, 14f, fieldSize, 0f, fieldSize.y);
            // 돌: 위쪽 띠(숲 쪽)와 아래쪽 띠에만 — 싸우는 가운데 줄은 비워 둔다.
            for (int i = 0; i < StoneCount; i++)
                ScatterBand(items, taken, rng, "stone", 28f, 40f, 22f, fieldSize);

            Spawn(groundTiles, "Decor", items, rng);
            return patches;
        }

        /// <summary>판 밖 좌우 여백(sideWidth씩)에 꽃·풀·돌을 듬성듬성.</summary>
        public static void BuildOutskirts(Transform area, Vector2 fieldSize, float sideWidth, System.Random rng)
        {
            var items = new List<Item>();
            var taken = new List<(Vector2 pos, float radius)>();
            for (int side = 0; side < 2; side++)
            {
                float x0 = side == 0 ? -sideWidth : fieldSize.x + 10f;
                float x1 = side == 0 ? -10f : fieldSize.x + sideWidth;
                for (int i = 0; i < 6; i++)
                    AddInRect(items, taken, rng, Flower(rng), 30f, 40f, 16f, x0, x1, 0f, fieldSize.y);
                for (int i = 0; i < 6; i++)
                    AddInRect(items, taken, rng, "grass", 26f, 36f, 14f, x0, x1, 0f, fieldSize.y);
                for (int i = 0; i < 2; i++)
                    AddInRect(items, taken, rng, "stone", 28f, 40f, 22f, x0, x1, 0f, fieldSize.y);
            }
            Spawn(area, "OutskirtsDecor", items, rng);
        }

        /// <summary>Atmosphere 층(유닛 위)에 나비·빛 가루·구름 그림자. 나비·빛 가루는 판 좌표로 움직이도록 판과 같은 자리의 층에 둔다.</summary>
        public static void BuildLife(RectTransform atmosphere, RectTransform field, List<Vector2> flowerSpots)
        {
            if (atmosphere == null) return;

            var clouds = new GameObject("CloudShadows", typeof(RectTransform), typeof(BattleCloudShadows));
            clouds.transform.SetParent(atmosphere, false);
            clouds.transform.SetAsFirstSibling(); // 떨어지는 잎·비네트 아래
            Stretch((RectTransform)clouds.transform);

            var life = new GameObject("FieldLife", typeof(RectTransform));
            life.transform.SetParent(atmosphere, false);
            var lifeRect = (RectTransform)life.transform;
            lifeRect.anchorMin = field.anchorMin;
            lifeRect.anchorMax = field.anchorMax;
            lifeRect.pivot = field.pivot;
            lifeRect.sizeDelta = field.sizeDelta;
            lifeRect.anchoredPosition = field.anchoredPosition;
            life.transform.SetSiblingIndex(1);

            var spots = flowerSpots.Count > 0 ? flowerSpots : new List<Vector2> { field.sizeDelta * 0.5f };
            life.AddComponent<BattleGlowMotes>().Init(field.sizeDelta, spots);
            life.AddComponent<BattleButterflies>().Init(field.sizeDelta, spots);
        }

        // ---------- 배치 도우미 ----------

        private static string Flower(System.Random rng) => $"flower_{rng.Next(1, FlowerKinds + 1):00}";

        private static float Range(System.Random rng, float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        /// <summary>판 안에 들어오고 이미 놓인 것과 radius만큼 떨어져 있으면 놓는다.</summary>
        private static bool TryAdd(List<Item> items, List<(Vector2 pos, float radius)> taken, string name, Vector2 pos, float height, float radius, Vector2 bounds)
        {
            if (pos.x < 10f || pos.x > bounds.x - 10f || pos.y < 4f || pos.y > bounds.y - height * 0.6f) return false;
            return Place(items, taken, name, pos, height, radius);
        }

        private static bool Place(List<Item> items, List<(Vector2 pos, float radius)> taken, string name, Vector2 pos, float height, float radius)
        {
            foreach (var t in taken)
                if (Vector2.Distance(t.pos, pos) < t.radius + radius) return false;
            taken.Add((pos, radius));
            items.Add(new Item { Name = name, Position = pos, Height = height });
            return true;
        }

        private static void Scatter(List<Item> items, List<(Vector2 pos, float radius)> taken, System.Random rng, string name, int count,
            float minHeight, float maxHeight, float radius, Vector2 bounds, float yMin, float yMax)
        {
            for (int i = 0; i < count; i++)
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    var pos = new Vector2(Range(rng, 0f, bounds.x), Range(rng, yMin, yMax));
                    if (TryAdd(items, taken, name, pos, Range(rng, minHeight, maxHeight), radius, bounds)) break;
                }
        }

        /// <summary>위쪽 띠나 아래쪽 띠 중 한 곳에 하나.</summary>
        private static void ScatterBand(List<Item> items, List<(Vector2 pos, float radius)> taken, System.Random rng, string name,
            float minHeight, float maxHeight, float radius, Vector2 bounds)
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                float height = Range(rng, minHeight, maxHeight);
                bool top = rng.NextDouble() < 0.6;
                float y = top ? Range(rng, TopBandBottom, bounds.y - height * 0.6f) : Range(rng, 6f, BottomBandTop - height * 0.5f);
                var pos = new Vector2(Range(rng, 30f, bounds.x - 30f), y);
                if (TryAdd(items, taken, name, pos, height, radius, bounds)) return;
            }
        }

        private static void AddInRect(List<Item> items, List<(Vector2 pos, float radius)> taken, System.Random rng, string name,
            float minHeight, float maxHeight, float radius, float x0, float x1, float y0, float y1)
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var pos = new Vector2(Range(rng, x0, x1), Range(rng, y0, y1));
                if (Place(items, taken, name, pos, Range(rng, minHeight, maxHeight), radius)) return;
            }
        }

        /// <summary>
        /// 뒤(큰 y)부터 그려 앞 것이 가린다. 좌우 뒤집기·밝기 차이로 같은 그림 반복을 줄인다.
        /// 꽃·돌은 바로 앞 층에 해 그림자(BattleShadow, 마을과 같은 기울인 그림자)를 둔다. 바닥에 붙은 풀 포기는 그림자 없음.
        /// </summary>
        private static void Spawn(Transform parent, string name, List<Item> items, System.Random rng)
        {
            var shadows = CreateLayer(parent, name + "Shadows");
            var layer = new GameObject(name, typeof(RectTransform));
            layer.transform.SetParent(parent, false);
            var layerRect = (RectTransform)layer.transform;
            layerRect.anchorMin = layerRect.anchorMax = layerRect.pivot = Vector2.zero;

            items.Sort((a, b) => b.Position.y.CompareTo(a.Position.y));
            foreach (var item in items)
            {
                var sprite = Load(item.Name);
                if (sprite == null) continue;
                var go = new GameObject(item.Name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(layer.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = item.Position;
                float aspect = sprite.rect.width / sprite.rect.height;
                rect.sizeDelta = new Vector2(item.Height * aspect, item.Height);
                if (rng.NextDouble() < 0.5) rect.localScale = new Vector3(-1f, 1f, 1f);

                float light = Mathf.Min(1f, Shade * (1f + Range(rng, -LightJitter, LightJitter)));
                var image = go.GetComponent<Image>();
                image.sprite = sprite;
                image.color = new Color(light, light, light, 1f);
                image.raycastTarget = false;
                if (item.Name != "grass") BattleShadow.Attach(image, shadows, ShadowHeightScale);
            }
        }

        private const float ShadowHeightScale = 0.8f; // 마을 장식(ShadowBuilder.DecorationHeightScale)과 같다

        /// <summary>parent 좌표 그대로 쓰는 빈 층(그림자처럼 다른 것보다 먼저 그릴 것을 담는다).</summary>
        internal static RectTransform CreateLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            return rect;
        }

        private static Sprite Load(string name)
        {
            if (_sprites.TryGetValue(name, out var cached) && cached != null) return cached; // 파괴됐으면 다시 만든다
            var texture = Resources.Load<Texture2D>(DecorPath + name);
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0f));
            _sprites[name] = sprite;
            return sprite;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // ---------- 코드로 그리는 텍스처 ----------

        private static Texture2D _butterfly, _glow, _cloud;

        /// <summary>날개 네 장(위가 크고 아래가 작음) + 가운데 어두운 몸통. 흰색이라 RawImage.color로 색을 입힌다.</summary>
        internal static Texture2D Butterfly
        {
            get
            {
                if (_butterfly != null) return _butterfly;
                const int w = 16, h = 12;
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f; // -1..1, 위가 +
                    float ax = Mathf.Abs(u);
                    bool upper = Ellipse(ax - 0.5f, v - 0.3f, 0.48f, 0.55f);
                    bool lower = Ellipse(ax - 0.4f, v + 0.45f, 0.35f, 0.4f);
                    bool body = ax < 0.1f && v > -0.8f && v < 0.7f;
                    Color c = upper ? Color.white
                        : lower ? new Color(0.82f, 0.82f, 0.82f, 1f) // 아래 날개는 조금 어둡게(위·아래가 구분되게)
                        : Color.clear;
                    if (body) c = new Color(0.15f, 0.1f, 0.08f, 1f);
                    pixels[y * w + x] = c;
                }
                return _butterfly = MakeTexture(w, h, pixels, "BattleButterfly", FilterMode.Point);
            }
        }

        /// <summary>가운데 밝은 심 + 부드럽게 퍼지는 빛(흰색).</summary>
        internal static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int size = 32;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                    float core = Mathf.Clamp01(1f - d * 3.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(halo * 0.7f + core * 1.2f));
                }
                return _glow = MakeTexture(size, size, pixels, "BattleGlowMote", FilterMode.Bilinear);
            }
        }

        /// <summary>가장자리가 아주 부드러운 둥근 덩어리(구름 그림자).</summary>
        internal static Texture2D Cloud
        {
            get
            {
                if (_cloud != null) return _cloud;
                const int size = 64;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.25f, 1f, d));
                }
                return _cloud = MakeTexture(size, size, pixels, "BattleCloudShadow", FilterMode.Bilinear);
            }
        }

        private static bool Ellipse(float x, float y, float rx, float ry) => (x * x) / (rx * rx) + (y * y) / (ry * ry) <= 1f;

        private static Texture2D MakeTexture(int w, int h, Color[] pixels, string name, FilterMode filter)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }

    /// <summary>꽃밭 사이를 구불구불 오가는 나비. 날갯짓은 가로 크기를 빠르게 접었다 폈다 해서 보인다.
    /// Time.deltaTime이라 전투 일시정지·배속을 따른다.</summary>
    public class BattleButterflies : MonoBehaviour
    {
        private const int Count = 6;
        private const float MinSpeed = 35f, MaxSpeed = 60f;
        private static readonly Color[] Colors =
        {
            Color.white, new Color(1f, 0.92f, 0.5f), new Color(0.65f, 0.82f, 1f), new Color(1f, 0.68f, 0.35f),
        };

        private class Butterfly
        {
            public RectTransform Rect;
            public Vector2 Position, Target;
            public float Speed, FlapRate, Phase, Facing = 1f;
        }

        private readonly List<Butterfly> _flies = new List<Butterfly>();
        private List<Vector2> _spots;
        private Vector2 _size;

        public void Init(Vector2 fieldSize, List<Vector2> spots)
        {
            _size = fieldSize;
            _spots = spots;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Butterfly", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.sizeDelta = new Vector2(16f, 12f); // 나비 텍스처와 1:1(픽셀이 뭉개지지 않게)
                var image = go.GetComponent<RawImage>();
                image.texture = BattleDecor.Butterfly;
                image.color = Colors[Random.Range(0, Colors.Length)];
                image.raycastTarget = false;
                var fly = new Butterfly
                {
                    Rect = rect,
                    Position = Near(spots[Random.Range(0, spots.Count)]) + Vector2.up * 30f,
                    Speed = Random.Range(MinSpeed, MaxSpeed),
                    FlapRate = Random.Range(16f, 22f),
                    Phase = Random.Range(0f, 10f),
                };
                fly.Target = NextTarget();
                _flies.Add(fly);
            }
        }

        private Vector2 Near(Vector2 spot) => spot + Random.insideUnitCircle * 50f;

        /// <summary>대부분 꽃밭 위로, 가끔 판 아무 데나.</summary>
        private Vector2 NextTarget() => Random.value < 0.75f
            ? Near(_spots[Random.Range(0, _spots.Count)]) + Vector2.up * Random.Range(20f, 60f)
            : new Vector2(Random.Range(0f, _size.x), Random.Range(40f, _size.y));

        private void Update()
        {
            float dt = Time.deltaTime, time = Time.time;
            bool hidden = BattleLighting.HideButterflies; // 비·눈·밤엔 숨는다
            foreach (var fly in _flies)
            {
                fly.Rect.gameObject.SetActive(!hidden);
                var toTarget = fly.Target - fly.Position;
                if (toTarget.magnitude < 16f) fly.Target = NextTarget();
                var dir = toTarget.normalized;
                var side = new Vector2(-dir.y, dir.x) * Mathf.Sin(time * 3f + fly.Phase) * 0.8f; // 구불구불
                fly.Position += (dir + side).normalized * fly.Speed * dt;
                if (Mathf.Abs(dir.x) > 0.2f) fly.Facing = Mathf.Sign(dir.x);

                float flap = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(time * fly.FlapRate + fly.Phase));
                fly.Rect.localScale = new Vector3(flap * fly.Facing, 1f, 1f);
                fly.Rect.anchoredPosition = fly.Position + new Vector2(0f, Mathf.Sin(time * 6f + fly.Phase) * 3f);
            }
        }
    }

    /// <summary>숲 가장자리·꽃밭 위에서 은은히 깜빡이며 떠도는 빛 가루(낮이라 작고 옅게).
    /// 떠돌기·깜빡임은 메인화면 반딧불(TitleFireflies)과 같은 방식.</summary>
    public class BattleGlowMotes : MonoBehaviour
    {
        private const int Count = 12;
        private const float MaxAlpha = 0.55f;
        private const float Leash = 50f; // 제자리에서 이만큼 넘게 벗어나면 끌어당긴다
        private static readonly Color[] Colors = { new Color(0.9f, 1f, 0.55f), new Color(1f, 0.93f, 0.6f) };

        private class Mote
        {
            public RawImage Image;
            public Vector2 Home, Position;
            public float Speed, Size, Period, Phase, WanderA, WanderB;
            public Color Color;
        }

        private readonly List<Mote> _motes = new List<Mote>();

        public void Init(Vector2 fieldSize, List<Vector2> spots)
        {
            for (int i = 0; i < Count; i++)
            {
                // 반은 숲 가장자리, 반은 꽃밭 위
                var home = i % 2 == 0
                    ? new Vector2(Random.Range(0f, fieldSize.x), fieldSize.y + Random.Range(-60f, 50f))
                    : spots[Random.Range(0, spots.Count)] + Random.insideUnitCircle * 60f + Vector2.up * 30f;
                var go = new GameObject("GlowMote", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                var image = go.GetComponent<RawImage>();
                image.texture = BattleDecor.Glow;
                image.raycastTarget = false;
                _motes.Add(new Mote
                {
                    Image = image,
                    Home = home,
                    Position = home,
                    Speed = Random.Range(8f, 16f),
                    Size = Random.Range(10f, 18f),
                    Period = Random.Range(2f, 5f),
                    Phase = Random.value,
                    WanderA = Random.Range(0f, 100f),
                    WanderB = Random.Range(0f, 100f),
                    Color = Colors[Random.Range(0, Colors.Length)],
                });
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime, time = Time.time;
            foreach (var m in _motes)
            {
                float k = Mathf.Repeat(time / m.Period + m.Phase, 1f);
                float glow = Mathf.Sin(k * Mathf.PI);
                glow *= glow;

                float angle = Mathf.Sin(time * 0.37f + m.WanderA) * 2.4f + Mathf.Sin(time * 0.71f + m.WanderB) * 1.3f;
                var velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * m.Speed;
                var away = m.Position - m.Home;
                if (away.magnitude > Leash) velocity -= away.normalized * m.Speed * 1.5f;
                m.Position += velocity * dt;

                var rect = m.Image.rectTransform;
                rect.anchoredPosition = m.Position;
                float s = m.Size * (0.75f + 0.35f * glow);
                rect.sizeDelta = new Vector2(s, s);
                var c = m.Color;
                c.a = MaxAlpha * glow;
                m.Image.color = c;
            }
        }
    }

    /// <summary>큰 구름 그림자가 화면을 왼쪽에서 오른쪽으로 아주 천천히 지나간다(유닛·숲 위에도 드리운다). 햇빛이 약하면 옅어진다.</summary>
    public class BattleCloudShadows : MonoBehaviour
    {
        private const int Count = 3;
        private const float Alpha = 0.12f;

        private class Shadow
        {
            public RectTransform Rect;
            public RawImage Image;
            public float Speed;
        }

        private readonly List<Shadow> _shadows = new List<Shadow>();
        private RectTransform _area;

        private void Start()
        {
            _area = (RectTransform)transform;
            var size = _area.rect.size;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("CloudShadow", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.sizeDelta = new Vector2(Random.Range(700f, 1100f), Random.Range(380f, 560f));
                // 처음엔 화면 곳곳에 고르게(서로 겹치지 않게 가로로 나눠서)
                rect.anchoredPosition = new Vector2(size.x * (i + Random.Range(0.1f, 0.9f)) / Count, Random.Range(size.y * 0.2f, size.y * 0.85f));
                var image = go.GetComponent<RawImage>();
                image.texture = BattleDecor.Cloud;
                image.color = new Color(0f, 0.03f, 0.02f, Alpha);
                image.raycastTarget = false;
                _shadows.Add(new Shadow { Rect = rect, Image = image, Speed = Random.Range(14f, 24f) });
            }
        }

        private void Update()
        {
            if (_area == null) return;
            var size = _area.rect.size;
            float alpha = Alpha * BattleLighting.Sunshine; // 해가 없으면(밤·흐림) 구름 그림자도 없다
            foreach (var s in _shadows)
            {
                s.Image.color = new Color(0f, 0.03f, 0.02f, alpha);
                var pos = s.Rect.anchoredPosition;
                pos.x += s.Speed * Time.deltaTime;
                float half = s.Rect.sizeDelta.x * 0.5f;
                if (pos.x - half > size.x) // 오른쪽으로 나가면 왼쪽 밖에서 다시
                {
                    pos.x = -half;
                    pos.y = Random.Range(size.y * 0.2f, size.y * 0.85f);
                }
                s.Rect.anchoredPosition = pos;
            }
        }
    }
}
