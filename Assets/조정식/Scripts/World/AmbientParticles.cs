using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 마을 공중에 떠다니는 것들: 낮엔 바람 따라 흘러가는 꽃잎·잎, 밤엔 깜빡이는 반딧불.
    /// 둘 다 화면 전체에 드문드문 나오고, DayNightCycle.NightLightFactor로 방출량을 낮/밤에 나눈다(해질녘엔 섞여 나온다).
    /// 파티클은 실행할 때 코드로 만든다. 재질만 에디터 메뉴 "GN3/World/마을 분위기(파티클·구름·새) 만들기"가 넣어 준다.
    /// </summary>
    public class AmbientParticles : MonoBehaviour
    {
        [Tooltip("낮 조명에 맞춰 어두워지는 재질(Sprite-Lit-Default). 꽃잎용")]
        [SerializeField] private Material litMaterial;
        [Tooltip("스스로 빛나는 재질(Sprite-Unlit-Default). 반딧불용")]
        [SerializeField] private Material unlitMaterial;

        [Header("꽃잎·잎 (낮)")]
        [SerializeField] private float petalsPerSecond = 1.2f;
        [Tooltip("바람 방향 이동 속도(유닛/초). 돌풍 세기가 곱해진다")]
        [SerializeField] private float petalSpeed = 0.45f;
        [SerializeField] private Color[] petalColors =
        {
            new Color(1f, 0.55f, 0.12f), // 주황 꽃잎
            new Color(1f, 0.82f, 0.15f), // 노랑 꽃잎
            new Color(1f, 0.98f, 0.92f), // 흰 꽃잎
            new Color(0.55f, 0.8f, 0.25f), // 연두 잎
        };

        [Header("반딧불 (밤)")]
        [SerializeField] private float firefliesPerSecond = 2f;
        [SerializeField] private Color fireflyColor = new Color(0.85f, 1f, 0.45f);

        [SerializeField] private int sortingOrder = 95; // 건물·캐릭터(1) 위, 구름 그림자(90) 위

        private ParticleSystem _petals;
        private ParticleSystem _fireflies;

        public void Configure(Material lit, Material unlit)
        {
            litMaterial = lit;
            unlitMaterial = unlit;
        }

        private void Start()
        {
            var cam = Camera.main;
            if (cam == null || litMaterial == null || unlitMaterial == null)
            {
                enabled = false;
                return;
            }
            float dot = PixelSprites.ArtDot(cam);
            var view = PixelSprites.ViewRect(cam);
            _petals = CreatePetals(view, dot);
            _fireflies = CreateFireflies(view, dot);
        }

        private void Update()
        {
            float night = DayNightCycle.NightLightFactor;
            float gust = VillageWind.Gust;

            var petalEmission = _petals.emission;
            petalEmission.rateOverTime = petalsPerSecond * (1f - night) * gust;
            var velocity = _petals.velocityOverLifetime; // 살아 있는 꽃잎 전부가 돌풍에 같이 밀린다
            Vector2 wind = VillageWind.Direction * petalSpeed * gust;
            velocity.x = wind.x;
            velocity.y = wind.y;

            var fireflyEmission = _fireflies.emission;
            fireflyEmission.rateOverTime = firefliesPerSecond * night;
        }

        private ParticleSystem CreatePetals(Rect view, float dot)
        {
            var ps = CreateSystem("Petals", view, PetalTexture(), litMaterial);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSize = dot * 2f; // 도트맵 2칸 = 도트 2개(크기를 섞으면 도트가 들쭉날쭉해진다)
            main.maxParticles = 60;
            main.startColor = DiscreteColors(petalColors);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.z = 0f;

            var noise = ps.noise; // 하늘하늘 흔들리며 떨어지는 느낌
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;
            noise.damping = true;

            SetAlphaOverLifetime(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f));
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateFireflies(Rect view, float dot)
        {
            var ps = CreateSystem("Fireflies", view, FireflyTexture(), unlitMaterial);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize = dot * 3f;
            main.maxParticles = 40;
            main.startColor = fireflyColor;

            var noise = ps.noise; // 제자리 근처를 느리게 맴돈다
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;

            // 사는 동안 두 번 켜졌다 꺼진다
            SetAlphaOverLifetime(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.1f, 0.4f),
                new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0.15f, 0.8f), new GradientAlphaKey(0f, 1f));
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateSystem(string name, Rect view, Texture2D texture, Material baseMaterial)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(view.center.x, view.center.y, 0f);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(view.width, view.height, 0f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = new Material(baseMaterial) { mainTexture = texture };
            renderer.sortingOrder = sortingOrder;
            return ps;
        }

        private static void SetAlphaOverLifetime(ParticleSystem ps, params GradientAlphaKey[] alphaKeys)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphaKeys);
            color.color = gradient;
        }

        /// <summary>색 목록 중 하나를 그대로 고르는 랜덤 색(섞인 중간색이 나오지 않게 Fixed 모드).</summary>
        private static ParticleSystem.MinMaxGradient DiscreteColors(Color[] colors)
        {
            var keys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) keys[i] = new GradientColorKey(colors[i], (i + 1f) / colors.Length);
            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        }

        // 흰색으로 그리고 파티클 색으로 물들인다.
        private static Texture2D PetalTexture() => PixelSprites.Texture(new[]
        {
            "ww",
            "w.",
        }, "w", new[] { Color.white });

        private static Texture2D FireflyTexture() => PixelSprites.Texture(new[]
        {
            ".h.",
            "hwh",
            ".h.",
        }, "wh", new[] { Color.white, new Color(1f, 1f, 1f, 0.35f) });
    }
}
