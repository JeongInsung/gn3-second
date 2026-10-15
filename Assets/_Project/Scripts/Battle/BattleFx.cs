using System.Collections;
using System.Collections.Generic;
using GN3.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 화면 연출(보이는 것만, 전투 판정·수치와 무관): 데미지 숫자 팝, 타격 스파크, 화면 흔들림,
    /// 배치 구역(그라데이션 + 흐르는 점선), 승리·패배 배너, 비네트·떨어지는 잎·숲 그늘.
    /// 코루틴은 BattleUIController가 StartCoroutine으로 돌린다. 텍스처는 코드로 한 번 그려 캐시한다.
    /// </summary>
    public static class BattleFx
    {
        // ---------- 타격 ----------

        /// <summary>데미지 숫자: 1.5배로 튀어나왔다 줄고, 살짝 포물선으로 떠오르며 흐려진다. big이면 크게.</summary>
        public static IEnumerator DamageNumber(RectTransform layer, Vector2 position, string text, Color color, bool big)
        {
            var go = new GameObject("DamageNumber", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
            go.transform.SetParent(layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = new Vector2(220f, 44f);

            var label = go.GetComponent<Text>();
            label.font = UITheme.BoldFont;
            label.fontSize = big ? 30 : 22;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.color = color;
            label.text = text;
            label.raycastTarget = false;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = big ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

            var group = go.GetComponent<CanvasGroup>();
            float drift = Random.Range(-18f, 18f);
            float duration = big ? 1.1f : 0.9f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float k = t / duration;
                float pop = k < 0.12f ? Mathf.Lerp(1.5f, 1f, k / 0.12f) : 1f;
                rect.localScale = Vector3.one * pop * (big ? 1.1f : 1f);
                float rise = 48f * (1f - (1f - k) * (1f - k)); // 처음 빠르게, 끝에서 느리게
                rect.anchoredPosition = position + new Vector2(drift * k, rise);
                group.alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
                yield return null;
            }
            if (go != null) Object.Destroy(go);
        }

        /// <summary>맞은 자리에서 커지며 사라지는 고리.</summary>
        public static IEnumerator HitSpark(RectTransform layer, Vector2 position, Color color)
        {
            var go = new GameObject("HitSpark", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.sprite = BattlePlaceholderSprites.Ring;
            image.raycastTarget = false;

            const float duration = 0.22f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float k = t / duration;
                float size = Mathf.Lerp(20f, 90f, 1f - (1f - k) * (1f - k));
                rect.sizeDelta = new Vector2(size, size);
                image.color = new Color(color.r, color.g, color.b, 1f - k);
                yield return null;
            }
            if (go != null) Object.Destroy(go);
        }

        private static bool _shaking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _shaking = false;
            _vignette = _leaf = _dash = null;
            _barPlate = _barPlateGold = null; // Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다
        }

        /// <summary>대상들을 함께 흔들고 원래 자리로 돌려놓는다(판과 바깥 잔디를 같이 흔들어 이음새가 벌어지지 않게). 이미 흔드는 중이면 건너뛴다.</summary>
        public static IEnumerator Shake(RectTransform[] targets, float strength, float duration)
        {
            if (_shaking) yield break;
            _shaking = true;
            var origins = new Vector2[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) origins[i] = targets[i].anchoredPosition;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float fade = 1f - t / duration;
                var offset = Random.insideUnitCircle * strength * fade;
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] != null) targets[i].anchoredPosition = origins[i] + offset;
                yield return null;
            }
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) targets[i].anchoredPosition = origins[i];
            _shaking = false;
        }

        // ---------- 배치 구역 ----------

        private static readonly Color DeployColor = new Color(0.55f, 0.95f, 0.55f);

        /// <summary>필드 왼쪽 배치 구역: 왼쪽이 밝고 경계로 갈수록 옅어지는 바탕 + 위아래로 흐르며 숨쉬는 점선 경계 + 글자. 클릭은 막지 않는다.</summary>
        public static GameObject CreateDeployZone(RectTransform groundLayer, float width)
        {
            var zone = new GameObject("DeployZone", typeof(RectTransform), typeof(RawImage));
            zone.transform.SetParent(groundLayer, false);
            var rect = (RectTransform)zone.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(width, 0f);
            var fill = zone.GetComponent<RawImage>();
            fill.texture = HorizontalFade(0.16f, 0.04f);
            fill.color = DeployColor;
            fill.raycastTarget = false;

            var edge = new GameObject("Edge", typeof(RectTransform), typeof(RawImage), typeof(DeployEdge));
            edge.transform.SetParent(zone.transform, false);
            var edgeRect = (RectTransform)edge.transform;
            edgeRect.anchorMin = new Vector2(1f, 0f);
            edgeRect.anchorMax = Vector2.one;
            edgeRect.pivot = new Vector2(1f, 0.5f);
            edgeRect.offsetMin = new Vector2(-4f, 0f);
            edgeRect.offsetMax = Vector2.zero;
            var dashes = edge.GetComponent<RawImage>();
            dashes.texture = Dash;
            dashes.raycastTarget = false;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(zone.transform, false);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.offsetMin = new Vector2(0f, -36f);
            labelRect.offsetMax = new Vector2(0f, -10f);
            var text = label.GetComponent<Text>();
            text.font = UITheme.Font;
            text.fontSize = 18;
            text.alignment = TextAnchor.UpperCenter;
            text.color = new Color(0.8f, 1f, 0.8f, 0.9f);
            text.text = "배치 구역";
            text.raycastTarget = false;
            label.AddComponent<Outline>().effectColor = new Color(0f, 0.15f, 0f, 0.6f);
            return zone;
        }

        /// <summary>흐려지며 사라진 뒤 지운다.</summary>
        public static IEnumerator FadeOutAndDestroy(GameObject go, float duration)
        {
            if (go == null) yield break;
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>(); // Unity 객체는 ??로 비교하면 안 된다
            group.blocksRaycasts = false;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                group.alpha = 1f - t / duration;
                yield return null;
            }
            if (go != null) Object.Destroy(go);
        }

        // ---------- 승리·패배 ----------

        /// <summary>화면 가운데 큰 배너(뒤를 살짝 어둡게)를 잠깐 띄웠다가 지운다. 실제 시간으로 잰다.</summary>
        public static IEnumerator ResultBanner(Transform canvas, string title, Color color)
        {
            var dim = new GameObject("ResultBanner", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            dim.transform.SetParent(canvas, false);
            dim.transform.SetAsLastSibling();
            var dimRect = (RectTransform)dim.transform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var label = new GameObject("Title", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(dim.transform, false);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(900f, 140f);
            rect.anchoredPosition = new Vector2(0f, 60f);
            var text = label.GetComponent<Text>();
            text.font = UITheme.BoldFont;
            text.fontSize = 88;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = color;
            text.text = title;
            var outline = label.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);

            var group = dim.GetComponent<CanvasGroup>();
            const float duration = 1.4f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (dim == null) yield break;
                float k = t / duration;
                group.alpha = k < 0.12f ? k / 0.12f : k > 0.82f ? (1f - k) / 0.18f : 1f;
                rect.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(1.4f, 1f, k / 0.15f) : 1f);
                yield return null;
            }
            if (dim != null) Object.Destroy(dim);
        }

        // ---------- 독 ----------

        /// <summary>독 피해 한 번: 초록 거품 몇 개가 몸에서 피어올라 사라진다.</summary>
        public static IEnumerator PoisonPuff(RectTransform layer, Vector2 position)
        {
            const int count = 5;
            const float duration = 0.8f;
            var bubbles = new List<(RectTransform rect, RawImage image, Vector2 drift)>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("PoisonBubble", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(layer, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.anchoredPosition = position + new Vector2(Random.Range(-22f, 22f), Random.Range(-14f, 14f));
                float size = Random.Range(10f, 18f);
                rect.sizeDelta = new Vector2(size, size);
                var image = go.GetComponent<RawImage>();
                image.texture = BattleDecor.Glow;
                image.color = new Color(0.55f, 0.95f, 0.3f, 0.9f);
                image.raycastTarget = false;
                bubbles.Add((rect, image, new Vector2(Random.Range(-14f, 14f), Random.Range(40f, 70f))));
            }
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                foreach (var (rect, image, drift) in bubbles)
                {
                    if (rect == null) continue;
                    rect.anchoredPosition += drift * Time.deltaTime;
                    var c = image.color;
                    c.a = 0.9f * (1f - k);
                    image.color = c;
                }
                yield return null;
            }
            foreach (var (rect, _, _) in bubbles)
                if (rect != null) Object.Destroy(rect.gameObject);
        }

        /// <summary>날아가는 포자가 흘리는 작은 가루 한 점(살짝 떨어지며 사라진다).</summary>
        public static IEnumerator SporeMote(RectTransform layer, Vector2 position)
        {
            var go = new GameObject("SporeMote", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = position + Random.insideUnitCircle * 6f;
            float size = Random.Range(6f, 10f);
            rect.sizeDelta = new Vector2(size, size);
            var image = go.GetComponent<RawImage>();
            image.texture = BattleDecor.Glow;
            image.raycastTarget = false;
            const float duration = 0.5f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                rect.anchoredPosition += new Vector2(0f, -20f * Time.deltaTime);
                image.color = new Color(0.8f, 1f, 0.35f, 0.8f * (1f - t / duration));
                yield return null;
            }
            if (go != null) Object.Destroy(go);
        }

        /// <summary>중독된 몸에서 계속 피어오르는 초록 거품 한 알(흔들리며 떠올라 사라진다).</summary>
        public static IEnumerator PoisonBubble(RectTransform layer, Vector2 position)
        {
            var go = new GameObject("PoisonBubble", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = position + new Vector2(Random.Range(-20f, 20f), Random.Range(-10f, 10f));
            float size = Random.Range(9f, 15f);
            var image = go.GetComponent<RawImage>();
            image.texture = BattleDecor.Glow;
            image.raycastTarget = false;
            float phase = Random.value * 6f;
            const float duration = 0.9f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                float k = t / duration;
                rect.anchoredPosition += new Vector2(Mathf.Sin(phase + t * 9f) * 18f, 55f) * Time.deltaTime;
                rect.sizeDelta = Vector2.one * size * (0.6f + 0.6f * k);
                image.color = new Color(0.45f, 1f, 0.3f, 0.95f * (1f - k * k));
                yield return null;
            }
            if (go != null) Object.Destroy(go);
        }

        /// <summary>중독 꼬리표: 체력 막대 왼쪽의 초록 판에 "독 3"(남은 횟수). 보스 꼬리표처럼 숨 쉬듯 커졌다 작아진다.
        /// 유닛 루트(발 기준)에 단다 — BattleUnitLabels가 빛 막 위로 옮긴다. 처음엔 꺼져 있다.</summary>
        public static Text CreatePoisonTag(Transform unitRoot, Vector2 position)
        {
            var go = new GameObject("PoisonTag", typeof(RectTransform), typeof(Image), typeof(BossTagPulse));
            go.transform.SetParent(unitRoot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(46f, 22f);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.3f, 0.68f, 0.12f); // 청동 판은 초록을 입히면 거의 검게 보여 단색 판에 테두리만
            image.raycastTarget = false;
            var border = go.AddComponent<Outline>();
            border.effectColor = new Color(0.08f, 0.2f, 0.04f, 1f);
            border.effectDistance = new Vector2(2f, -2f);

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<Text>();
            text.font = UITheme.BoldFont;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;
            labelGo.AddComponent<Outline>().effectColor = new Color(0f, 0.2f, 0f, 0.95f);
            go.SetActive(false);
            return text;
        }

        // ---------- 보스 표시 ----------

        /// <summary>보스 머리 위 "BOSS" 꼬리표: 붉은 판(청동 테두리)에 흰 굵은 글씨, 숨 쉬듯 살짝 커졌다 작아진다.
        /// 유닛 루트(발 기준)에 y 높이로 단다 — 이름·막대처럼 BattleUnitLabels가 빛 막 위로 옮긴다.</summary>
        public static void CreateBossTag(Transform unitRoot, float y)
        {
            var go = new GameObject("BossTag", typeof(RectTransform), typeof(Image), typeof(BossTagPulse));
            go.transform.SetParent(unitRoot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(64f, 22f);
            var image = go.GetComponent<Image>();
            image.sprite = BarPlate(false); // 체력 막대와 같은 청동 베벨 판
            image.type = Image.Type.Sliced;
            image.color = new Color(1f, 0.45f, 0.4f);
            image.raycastTarget = false;

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = labelGo.GetComponent<Text>();
            text.text = "BOSS";
            text.font = UITheme.BoldFont;
            text.fontSize = 14;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            labelGo.AddComponent<Outline>().effectColor = new Color(0.25f, 0f, 0f, 0.9f);
        }

        // ---------- 분위기 ----------

        /// <summary>판 바로 위(유닛 위, 아래 패널·결과창 아래)에 비네트와 떨어지는 잎을 깐다. 클릭은 막지 않는다.</summary>
        public static RectTransform BuildAtmosphere(RectTransform field)
        {
            var root = field.parent;
            if (root == null) return null;
            var atmosphere = new GameObject("Atmosphere", typeof(RectTransform));
            atmosphere.transform.SetParent(root, false);
            atmosphere.transform.SetSiblingIndex(field.GetSiblingIndex() + 1);
            var rect = (RectTransform)atmosphere.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var leaves = new GameObject("Leaves", typeof(RectTransform), typeof(BattleLeaves));
            leaves.transform.SetParent(atmosphere.transform, false);
            var leavesRect = (RectTransform)leaves.transform;
            leavesRect.anchorMin = Vector2.zero;
            leavesRect.anchorMax = Vector2.one;
            leavesRect.offsetMin = leavesRect.offsetMax = Vector2.zero;

            var vignette = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage));
            vignette.transform.SetParent(atmosphere.transform, false);
            var vignetteRect = (RectTransform)vignette.transform;
            vignetteRect.anchorMin = Vector2.zero;
            vignetteRect.anchorMax = Vector2.one;
            vignetteRect.offsetMin = vignetteRect.offsetMax = Vector2.zero;
            var image = vignette.GetComponent<RawImage>();
            image.texture = Vignette;
            image.raycastTarget = false;
            return rect;
        }

        // ---------- 머리 위 막대 판 ----------

        private static Sprite _barPlate, _barPlateGold;

        /// <summary>체력·공격 대기 막대를 담는 작은 판(UITheme 버튼·판과 같은 결): 검은 외곽선 1px → 청동 베벨 2px(위·왼쪽 밝게) → 어두운 선 1px → 숯빛 안쪽.
        /// gold면 분대장용 금빛 베벨. 9-slice(테두리 BarPlateBorder px).</summary>
        public const int BarPlateBorder = 4;

        public static Sprite BarPlate(bool gold)
        {
            if (gold) return _barPlateGold != null ? _barPlateGold : _barPlateGold = BuildBarPlate(new Color(1f, 0.85f, 0.4f), new Color(0.6f, 0.42f, 0.12f), "BattleBarPlateGold");
            return _barPlate != null ? _barPlate : _barPlate = BuildBarPlate(UITheme.BronzeLight, UITheme.BronzeDark, "BattleBarPlate");
        }

        private static Sprite BuildBarPlate(Color light, Color dark, string name)
        {
            const int size = 14, border = BarPlateBorder;
            var outline = new Color(0.03f, 0.02f, 0.02f, 1f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                bool lit = y >= size - 1 - edge || x <= edge; // 위·왼쪽 변
                Color bevel = lit ? light : dark;
                Color c = edge == 0 ? outline
                    : edge == 1 ? bevel
                    : edge == 2 ? Color.Lerp(bevel, dark, 0.45f) // 안쪽으로 조금 어둡게(UITheme.Bevel과 같은 결)
                    : edge == 3 ? outline
                    : UITheme.InsetFill;
                c.a = 1f;
                pixels[y * size + x] = c;
            }
            var texture = MakeTexture(size, size, pixels, TextureWrapMode.Clamp, name);
            texture.filterMode = FilterMode.Point; // 1px 테두리가 번지지 않게
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            return sprite;
        }

        // ---------- 코드로 그리는 텍스처 ----------

        private static Texture2D _vignette, _leaf, _dash;

        /// <summary>가운데 투명 → 모서리 검정(최대 알파 0.45).</summary>
        private static Texture2D Vignette
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int size = 128;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Sqrt(2f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d)) * 0.45f;
                    pixels[y * size + x] = new Color(0f, 0f, 0f, a);
                }
                return _vignette = MakeTexture(size, size, pixels, TextureWrapMode.Clamp, "BattleVignette");
            }
        }

        private static Texture2D _arrow;

        /// <summary>
        /// 고블린 궁수의 화살(오른쪽을 향함, 꼬리에 속도선). 날아가는 방향으로 돌려 그린다.
        /// Resources/Battle/arrow(다운로드한 "오른쪽으로 날아가는 픽셀 화살"을 여백 잘라 112×15로 구움)를 쓰고,
        /// 없으면 코드로 그린 작은 화살(흰 깃 · 나무 자루 · 쇠 촉).
        /// </summary>
        internal static Texture2D Arrow
        {
            get
            {
                if (_arrow != null) return _arrow;
                var art = Resources.Load<Texture2D>("Battle/arrow");
                if (art != null) return _arrow = art;
                const int w = 20, h = 5, mid = 2;
                var wood = new Color(0.55f, 0.36f, 0.2f, 1f);
                var iron = new Color(0.85f, 0.87f, 0.9f, 1f);
                var feather = new Color(0.95f, 0.93f, 0.88f, 1f);
                var outline = new Color(0.12f, 0.08f, 0.06f, 1f);
                var pixels = new Color[w * h];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
                void Set(int x, int y, Color c) { if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = c; }
                for (int x = 3; x < 16; x++) Set(x, mid, wood);                 // 자루
                for (int x = 0; x < 4; x++) { Set(x, mid + 1, feather); Set(x, mid - 1, feather); Set(x, mid, feather); } // 깃
                Set(0, mid + 2, feather); Set(0, mid - 2, feather);
                for (int x = 16; x < 20; x++) Set(x, mid, iron);                // 촉
                Set(16, mid + 1, iron); Set(16, mid - 1, iron); Set(17, mid + 1, outline); Set(17, mid - 1, outline);
                _arrow = MakeTexture(w, h, pixels, TextureWrapMode.Clamp, "BattleArrow");
                _arrow.filterMode = FilterMode.Point; // 픽셀 화살이 번지지 않게
                return _arrow;
            }
        }

        /// <summary>잎: 끝이 뾰족한 작은 타원(흰색, Image.color로 색을 입힌다).</summary>
        internal static Texture2D Leaf
        {
            get
            {
                if (_leaf != null) return _leaf;
                const int w = 16, h = 10;
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float halfWidth = 1f - u * u;                 // 양 끝이 뾰족
                    float inside = Mathf.Clamp01((halfWidth - Mathf.Abs(v)) * 4f);
                    float vein = Mathf.Abs(v) < 0.12f ? 0.8f : 1f; // 가운데 잎맥
                    pixels[y * w + x] = new Color(vein, vein, vein, inside);
                }
                return _leaf = MakeTexture(w, h, pixels, TextureWrapMode.Clamp, "BattleLeaf");
            }
        }

        /// <summary>세로 점선 한 칸(위 절반 칠함). RawImage uvRect로 반복·흐름.</summary>
        private static Texture2D Dash
        {
            get
            {
                if (_dash != null) return _dash;
                const int w = 4, h = 16;
                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = y >= h / 2 ? Color.white : Color.clear;
                return _dash = MakeTexture(w, h, pixels, TextureWrapMode.Repeat, "BattleDash");
            }
        }

        private static Texture2D HorizontalFade(float left, float right)
        {
            const int w = 64;
            var pixels = new Color[w];
            for (int x = 0; x < w; x++) pixels[x] = new Color(1f, 1f, 1f, Mathf.Lerp(left, right, x / (w - 1f)));
            return MakeTexture(w, 1, pixels, TextureWrapMode.Clamp, "BattleDeployFade");
        }

        private static Texture2D MakeTexture(int w, int h, Color[] pixels, TextureWrapMode wrap, string name)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = wrap,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }

    /// <summary>전투 몬스터 그림 종류. 근접 적은 검 고블린, 원거리 적은 고블린 궁수.</summary>
    public enum MonsterKind
    {
        Sword,
        Archer,
        Wolf,
        Mushroom,
        GoblinRider,
    }

    /// <summary>
    /// 전투 몬스터 그림: Resources/Battle/Monsters의 고블린 시트(4열×2행, 투명 배경, 오른쪽을 본다)를 붙은 Image에 넘긴다.
    /// 종류(MonsterKind)마다 시트·프레임 수·속도가 다르다(Sheets 표).
    ///  - 평소엔 숨쉬기를, 움직이는 동안(SetMoving)엔 걷기를 반복한다(유닛마다 시작 프레임이 달라 다 같이 움직이지 않는다).
    ///    걷기 시트가 없는 종류는 숨쉬기를 빠르게 돌리고 UI가 통통 튀게 한다(IsHopping).
    ///  - PlayAttack()을 받으면 공격을 한 번 재생하고 돌아간다. 맞는(검이 닿는·화살을 놓는) 순간은 AttackHitDelay.
    ///  - 쓰러지면(SetDead) 쓰러짐(무릎 꿇기 4장)을 한 번 재생하고 마지막 장에 멈춘다.
    /// Time.deltaTime이라 전투 일시정지·배속을 따른다.
    ///
    /// 시트는 Tools/clean_monster_sheet.py로 정리한 것: 원본(칸 443.5px, 검·활이 옆 칸으로 넘어감)을 프레임별로 모아
    /// 칸 544px 정수 격자에 사방 여백을 두고 다시 그렸다(옆 칸 그림이 삐져나오지 않고, 넘던 끝도 잘리지 않는다).
    /// 공격·쓰러짐 시트는 쓰지 않는 칸을 빼고 비운 칸을 끝에 두었고, 발 높이를 숨쉬기 시트에 맞춰 구웠다(바뀔 때 발이 튀지 않게).
    /// </summary>
    public class BattleMonsterSprite : MonoBehaviour
    {
        private const int Columns = 4, Rows = 2;
        private const float CellSize = 544f;    // 정리된 시트의 칸(clean_monster_sheet.py CELL)
        private const float SourceCell = 443.5f; // 원본 칸 — 그림 크기·발 위치는 이 칸 기준으로 잰 값
        private const float Padding = (CellSize - SourceCell) / 2f;
        private const float DefaultFeetOffset = 0.05f; // 발이 원본 칸 아래에서 이 비율만큼 떠 있다(그림자 위에 세우려고 그만큼 내린다). 고블린 실측
        private const string Folder = "Battle/Monsters/";

        private class Sheets
        {
            public string Idle, Walk, Attack, Die; // Walk가 null이면 이동 중 숨쉬기를 WalkFps로 + 통통 튀기
            public float IdleFps, WalkFps;
            public int AttackFrames, AttackHitFrame;
            public float AttackFps;
            public int DieFrames;
            public float DieFps;
            public float FeetOffset = DefaultFeetOffset; // 발이 원본 칸 아래에서 떠 있는 비율(시트마다 다르다)
            public float SizeScale = 1f;                  // 전투 화면 크기 배율(BattleUIController의 MonsterSize에 곱한다)
        }

        private static readonly Dictionary<MonsterKind, Sheets> Kinds = new Dictionary<MonsterKind, Sheets>
        {
            // 검 고블린: 공격 = 왼손 검술 7장(원본 왼쪽 아래 칸 뺌), 3번째 장에서 내려 벤다. 쓰러짐 = 원본 윗줄 4장.
            [MonsterKind.Sword] = new Sheets
            {
                Idle = "goblin_idle", Walk = "goblin_walk", Attack = "goblin_attack", Die = "goblin_die",
                IdleFps = 8f, WalkFps = 10f, AttackFrames = 7, AttackHitFrame = 3, AttackFps = 16f, DieFrames = 4, DieFps = 8f,
                SizeScale = GoblinSizeScale,
            },
            // 고블린 궁수: 공격 = 사격 7장(원본 아래줄 두 번째, 화살만 따로 날아가는 칸을 뺌), 5번째 장 다음에 활을 놓는다.
            // 걷기 = 8장(원본 윗줄 발 높이가 17px 낮아 맞춰 구움). 쓰러짐 = 원본 윗줄 4장.
            [MonsterKind.Archer] = new Sheets
            {
                Idle = "goblin_archer_idle", Walk = "goblin_archer_walk", Attack = "goblin_archer_attack", Die = "goblin_archer_die",
                IdleFps = 8f, WalkFps = 10f, AttackFrames = 7, AttackHitFrame = 5, AttackFps = 12f, DieFrames = 4, DieFps = 8f,
                SizeScale = GoblinSizeScale,
            },
            // 늑대(다이어울프·얼음늑대): 대기 8장 · 달리기 8장 · 물기 8장(3·4번째 장 으르렁 → 5번째 장 앞으로 뛰어 문다).
            // 고블린처럼 오른쪽을 보는 그림 그대로 구웠다(적 방향은 UI가 Facing으로 뒤집는다).
            // 쓰러짐 8장(서 있다 엎드려 눕는다) — bake_monster_set.py --ground로 아랫줄도 발밑에 맞춰 구웠다.
            // 발이 원본 칸 아래 11.7% 위에 있다(대기 시트 실측).
            [MonsterKind.Wolf] = new Sheets
            {
                Idle = "wolf_idle", Walk = "wolf_walk", Attack = "wolf_attack", Die = "wolf_die",
                IdleFps = 8f, WalkFps = 12f, AttackFrames = 8, AttackHitFrame = 4, AttackFps = 14f, DieFrames = 8, DieFps = 10f,
                FeetOffset = 0.117f,
            },
            // 독버섯: 걷기 8장 · 포자 공격 8장(움츠렸다가 5번째 장에서 포자를 터뜨린다) · 쓰러짐 8장(갓 밑으로 주저앉는다).
            // 원거리 몹: 뒷줄에서 연두 포자를 쏘고(FieldBattleSession), 맞히면 독(PoisonSporePassive)을 건다.
            // 대기 시트는 없어 걷기 첫 장을 숨 쉬듯 늘였다 줄인 8장을 구웠다(bake_monster_set.py --idle).
            // 공격·쓰러짐은 그림 크기가 달라 걷기 첫 장 크기·발밑에 맞춰 구웠다. 갓이 커서 칸을 많이 채우므로 작게 그린다.
            [MonsterKind.Mushroom] = new Sheets
            {
                Idle = "mushroom_idle", Walk = "mushroom_walk", Attack = "mushroom_attack", Die = "mushroom_die",
                IdleFps = 6f, WalkFps = 10f, AttackFrames = 8, AttackHitFrame = 4, AttackFps = 12f, DieFrames = 8, DieFps = 10f,
                FeetOffset = 0.054f, SizeScale = 0.72f,
            },
            // 고블린 늑대기수: 대기 8장 · 걷기 8장 · 공격 8장(3번째 장에 검을 치켜들고 4번째 장에 늑대가 뛰어오르며 벤다)
            // · 쓰러짐 8장(늑대가 엎드리고 고블린이 엎어진다). 공격·쓰러짐 시트가 작게 그려져 대기 크기에 맞춰 키우면 칸을 넘어
            // 모두 0.85배로 구웠고(bake_monster_set.py --shrink 0.85) 화면 크기는 SizeScale로 되돌린다.
            [MonsterKind.GoblinRider] = new Sheets
            {
                Idle = "goblin_rider_idle", Walk = "goblin_rider_walk", Attack = "goblin_rider_attack", Die = "goblin_rider_die",
                IdleFps = 8f, WalkFps = 16f, AttackFrames = 8, AttackHitFrame = 3, AttackFps = 14f, DieFrames = 8, DieFps = 10f, // 걷기는 빨리 달려 16fps
                FeetOffset = 0.02f, SizeScale = 1f / 0.85f,
            },
        };

        /// <summary>적 이름으로 그림 종류: 늑대기수는 늑대 탄 고블린, 늑대 계열(늑대·울프)은 늑대, 버섯은 독버섯,
        /// 그 밖엔 근접 검 고블린 / 원거리 고블린 궁수.</summary>
        public static MonsterKind KindFor(string name, bool melee) =>
            name != null && name.Contains("늑대기수") ? MonsterKind.GoblinRider // 이름에 "늑대"도 있어 늑대보다 먼저
            : IsWolf(name) ? MonsterKind.Wolf
            : name != null && name.Contains("버섯") ? MonsterKind.Mushroom
            : melee ? MonsterKind.Sword : MonsterKind.Archer;

        private const float GoblinSizeScale = 0.8f; // 고블린(검·궁수)은 용병보다 조금 작게 — 예전 크기는 커 보였다

        /// <summary>전투 화면 크기 배율(종류별). BattleUIController가 몬스터 칸 크기에 곱한다.</summary>
        public static float SizeScale(MonsterKind kind) => Kinds[kind].SizeScale;

        public static bool IsWolf(string name) => name != null && (name.Contains("늑대") || name.Contains("울프"));

        private static readonly Dictionary<string, Sprite[]> _cache = new Dictionary<string, Sprite[]>();

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _cache.Clear();

        private Image _image;
        private Sheets _sheets = Kinds[MonsterKind.Sword];
        private float _time;
        private float _attackTime = -1f; // 0 이상이면 공격 재생 중
        private float _dieTime;          // 쓰러진 뒤 흐른 시간
        private bool _moving;
        private bool _dead;

        public MonsterKind Kind { get; private set; } = MonsterKind.Sword;

        /// <summary>검 고블린 숨쉬기 8장(몬스터 그림이 있는지 확인용). 시트가 없으면 null.</summary>
        public static Sprite[] Frames() => Slice(Kinds[MonsterKind.Sword].Idle);

        /// <summary>몬스터 그림 칸을 놓는다. size = 원본 칸 기준 크기(보스 배율 포함). 여백까지 담게 키우고, 발이 그림자 위에 오게 내린다.</summary>
        public static void Layout(RectTransform rect, float size, MonsterKind kind = MonsterKind.Sword)
        {
            float scale = size / SourceCell;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(CellSize * scale, CellSize * scale);
            rect.anchoredPosition = new Vector2(0f, -(Padding + SourceCell * Kinds[kind].FeetOffset) * scale);
        }

        /// <summary>시트를 8장으로 자른다(시트마다 한 번). 시트가 없거나 name이 null이면 null.</summary>
        private static Sprite[] Slice(string name)
        {
            if (name == null) return null;
            if (_cache.TryGetValue(name, out var cached) && cached[0] != null) return cached; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>(Folder + name);
            if (texture == null) return null;
            float w = texture.width / (float)Columns, h = texture.height / (float)Rows;
            var sprites = new Sprite[Columns * Rows];
            for (int i = 0; i < sprites.Length; i++)
            {
                int col = i % Columns, row = i / Columns;
                // 텍스처 좌표는 아래가 0이라 윗줄이 가장 큰 y.
                sprites[i] = Sprite.Create(texture, new Rect(col * w, (Rows - 1 - row) * h, w, h), new Vector2(0.5f, 0f));
            }
            _cache[name] = sprites;
            return sprites;
        }

        /// <summary>그림 종류를 정한다(만든 직후 한 번).</summary>
        public void Init(MonsterKind kind)
        {
            Kind = kind;
            _sheets = Kinds[kind];
            // 시트를 미리 잘라 둔다: 첫 공격·첫 쓰러짐에서 큰 시트를 읽느라 한 프레임이 길어지면
            // 그만큼 동작이 건너뛰어 화살이 활을 놓는 장면을 지나서 나가 보인다.
            Slice(_sheets.Walk);
            Slice(_sheets.Attack);
            Slice(_sheets.Die);
            _time = Random.Range(0f, Columns * Rows / _sheets.IdleFps);
            Show();
        }

        /// <summary>공격 시작부터 맞는 순간(검이 닿음·화살을 놓음)까지(초, 전투 시간). 피격 연출·화살 발사를 이만큼 늦춘다.</summary>
        public float AttackHitDelay => _sheets.AttackHitFrame / _sheets.AttackFps;
        /// <summary>공격 동작 전체 길이(초). 그동안 그 몬스터는 움직이지 않는다(FieldUnit.AttackAnimSeconds).</summary>
        public float AttackSeconds => _sheets.AttackFrames / _sheets.AttackFps;
        /// <summary>쓰러짐 동작 길이(초). 흐려지기는 이 뒤에 시작한다.</summary>
        public float DieSeconds => _sheets.DieFrames / _sheets.DieFps;
        /// <summary>쓰러짐 시트가 있으면 그림이 직접 쓰러지므로 옆으로 눕히지 않는다.</summary>
        public bool HasDie => Slice(_sheets.Die) != null;
        /// <summary>걷기 그림이 없어 이동 중엔 UI가 통통 튀게 한다.</summary>
        public bool IsHopping => _sheets.Walk == null;

        /// <summary>공격을 처음부터 한 번 재생한다(공격 시트가 없으면 대기 그대로).</summary>
        public void PlayAttack()
        {
            if (_dead || Slice(_sheets.Attack) == null) return;
            _attackTime = 0f;
            Show();
        }

        /// <summary>움직이는 동안 걷기, 멈추면 숨쉬기.</summary>
        public void SetMoving(bool moving) => _moving = moving;

        /// <summary>쓰러졌다: 쓰러짐 동작을 한 번 재생하고 마지막 장에 멈춘다(시트가 없으면 지금 프레임에 멈춤).
        /// 매 프레임 불려도 처음 한 번만 시작한다.</summary>
        public void SetDead()
        {
            if (_dead) return;
            _dead = true;
            _attackTime = -1f;
            _dieTime = 0f;
            Show();
        }

        private void Awake()
        {
            _image = GetComponent<Image>();
            _time = Random.Range(0f, Columns * Rows / _sheets.IdleFps);
            Show();
        }

        private void Update()
        {
            if (_dead)
            {
                if (_dieTime < DieSeconds)
                {
                    _dieTime += Time.deltaTime;
                    Show();
                }
                return;
            }
            _time += Time.deltaTime;
            if (_attackTime >= 0f)
            {
                _attackTime += Time.deltaTime;
                if (_attackTime * _sheets.AttackFps >= _sheets.AttackFrames) _attackTime = -1f; // 끝 → 대기로
            }
            Show();
        }

        private void Show()
        {
            if (_image == null) return;
            if (_dead)
            {
                var die = Slice(_sheets.Die);
                if (die != null) _image.sprite = die[Mathf.Min(Mathf.FloorToInt(_dieTime * _sheets.DieFps), _sheets.DieFrames - 1)];
                return; // 시트가 없으면 쓰러진 순간 그림 그대로
            }
            if (_attackTime >= 0f)
            {
                var attack = Slice(_sheets.Attack);
                if (attack != null)
                {
                    _image.sprite = attack[Mathf.Min(Mathf.FloorToInt(_attackTime * _sheets.AttackFps), _sheets.AttackFrames - 1)];
                    return;
                }
            }
            var idle = Slice(_sheets.Idle);
            if (_moving)
            {
                var walk = Slice(_sheets.Walk) ?? idle; // 걷기 그림이 없으면 숨쉬기를 빠르게
                if (walk != null)
                {
                    _image.sprite = walk[Mathf.FloorToInt(_time * _sheets.WalkFps) % walk.Length];
                    return;
                }
            }
            if (idle != null) _image.sprite = idle[Mathf.FloorToInt(_time * _sheets.IdleFps) % idle.Length];
        }

        /// <summary>시험·디버그용: 지금 공격 동작 중인지 / 걷는 중인지.</summary>
        public bool IsAttacking => _attackTime >= 0f;
        public bool IsWalking => _moving && !_dead && _attackTime < 0f;
    }

    /// <summary>보스 꼬리표가 숨 쉬듯 살짝 커졌다 작아진다(전투 일시정지와 상관없이).</summary>
    public class BossTagPulse : MonoBehaviour
    {
        private void Update() => transform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * 4f));
    }

    /// <summary>배치 구역 점선 경계: 위아래로 천천히 흐르고 밝기가 숨쉬듯 오르내린다.</summary>
    public class DeployEdge : MonoBehaviour
    {
        private const float DashLength = 24f;   // 점선 한 칸(칠한 곳 + 빈 곳) 높이
        private const float ScrollSpeed = 0.6f; // 칸/초
        private RawImage _image;
        private RectTransform _rect;

        private void Awake()
        {
            _image = GetComponent<RawImage>();
            _rect = (RectTransform)transform;
        }

        private void Update()
        {
            float height = Mathf.Max(1f, _rect.rect.height);
            _image.uvRect = new Rect(0f, Time.time * ScrollSpeed, 1f, height / DashLength);
            float glow = 0.55f + 0.3f * (Mathf.Sin(Time.time * 2.5f) + 1f) * 0.5f;
            _image.color = new Color(0.65f, 1f, 0.65f, glow);
        }
    }

    /// <summary>화면 위에서 바람 따라 오른쪽 아래로 흘러가는 잎. 좌우로 흔들리며 돈다. 일시정지(timeScale 0)면 멈춘다.</summary>
    public class BattleLeaves : MonoBehaviour
    {
        private const int Count = 18;
        private static readonly Color[] Colors =
        {
            new Color(0.55f, 0.8f, 0.25f, 0.85f),
            new Color(0.35f, 0.62f, 0.2f, 0.85f),
            new Color(0.85f, 0.75f, 0.25f, 0.85f),
            new Color(0.7f, 0.85f, 0.35f, 0.85f),
        };

        private class Leaf
        {
            public RectTransform Rect;
            public RawImage Image;
            public Color Color;     // 원래 색(빛 막 위에 있어 BattleLighting.LightColor를 직접 곱한다)
            public Vector2 Position;
            public Vector2 Velocity;
            public float Phase, SwayAmount, Spin, Angle;
        }

        private readonly Leaf[] _leaves = new Leaf[Count];
        private RectTransform _area;

        private void Start()
        {
            _area = (RectTransform)transform;
            var size = _area.rect.size;
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Leaf", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                float scale = Random.Range(1f, 1.6f);
                rect.sizeDelta = new Vector2(24f, 15f) * scale;
                var image = go.GetComponent<RawImage>();
                image.texture = BattleFx.Leaf;
                var color = Colors[Random.Range(0, Colors.Length)];
                image.color = color;
                image.raycastTarget = false;
                _leaves[i] = new Leaf { Rect = rect, Image = image, Color = color };
                Respawn(_leaves[i], size, scatter: true);
            }
        }

        private void Respawn(Leaf leaf, Vector2 size, bool scatter)
        {
            // 처음엔 화면 곳곳에, 이후엔 왼쪽 바깥이나 위쪽 바깥에서 들어온다.
            leaf.Position = scatter
                ? new Vector2(Random.Range(0f, size.x), Random.Range(size.y * 0.2f, size.y))
                : Random.value < 0.5f
                    ? new Vector2(-20f, Random.Range(size.y * 0.35f, size.y))
                    : new Vector2(Random.Range(0f, size.x * 0.7f), size.y + 20f);
            leaf.Velocity = new Vector2(Random.Range(55f, 110f), -Random.Range(25f, 55f));
            leaf.Phase = Random.Range(0f, Mathf.PI * 2f);
            leaf.SwayAmount = Random.Range(10f, 25f);
            leaf.Spin = Random.Range(-160f, 160f);
            leaf.Angle = Random.Range(0f, 360f);
        }

        private void Update()
        {
            var size = _area.rect.size;
            float dt = Time.deltaTime;
            var light = BattleLighting.LightColor; // 밤·비엔 잎도 함께 어둡게
            foreach (var leaf in _leaves)
            {
                if (leaf == null) continue;
                leaf.Image.color = new Color(leaf.Color.r * light.r, leaf.Color.g * light.g, leaf.Color.b * light.b, leaf.Color.a);
                leaf.Phase += dt * 2.2f;
                leaf.Position += leaf.Velocity * dt;
                leaf.Angle += leaf.Spin * dt;
                leaf.Rect.anchoredPosition = leaf.Position + new Vector2(0f, Mathf.Sin(leaf.Phase) * leaf.SwayAmount);
                // 뒤집히듯 돌게 가로 폭을 함께 줄였다 늘린다
                leaf.Rect.localScale = new Vector3(Mathf.Cos(leaf.Phase * 1.3f), 1f, 1f);
                leaf.Rect.localRotation = Quaternion.Euler(0f, 0f, leaf.Angle);
                if (leaf.Position.x > size.x + 30f || leaf.Position.y < -30f) Respawn(leaf, size, scatter: false);
            }
        }
    }
}
