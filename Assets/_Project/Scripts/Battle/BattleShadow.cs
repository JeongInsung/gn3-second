using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 화면(오버레이 UI)의 해 그림자. 마을 ProjectedShadow와 같은 모양: 원본 그림을 발밑 선에 붙인 채
    /// 위로 갈수록 해 반대쪽으로 밀어(높이 × heightScale × 그림자 벡터) 기울인다. 발밑 줄은 본체와 같은 자리라 그림자가 떨어지지 않는다.
    /// 그림자 전용 층(원본보다 먼저 그려지는 층)에 두고 매 프레임 원본 Image의 그림·자리·크기·뒤집기를 따라간다.
    /// 방향·진하기는 BattleLighting.Current(마을과 같은 하늘 계산), 칠은 GN3/UIProjectedShadow(겹쳐도 한 번만 어둡게).
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class BattleShadow : BaseMeshEffect
    {
        private const float AlphaThreshold = 0.5f; // 셰이더가 버리는 알파와 같다(이보다 옅은 픽셀은 그림자가 없다)

        private static Material _material;
        private static readonly Dictionary<Sprite, float> _bottoms = new Dictionary<Sprite, float>();
        private static readonly Dictionary<Texture2D, Color32[]> _pixels = new Dictionary<Texture2D, Color32[]>();

        private Image _source;
        private Image _image;
        private Transform _groundAnchor;
        private float _heightScale;

        // 다시 그릴지 판단용 마지막 상태
        private Vector2 _lastDir;
        private Vector3 _lastPosition;
        private Vector2 _lastSize;
        private Sprite _lastSprite;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            _bottoms.Clear();
            _pixels.Clear();
        }

        /// <summary>
        /// source 그림의 그림자를 layer에 만든다. groundAnchor를 주면 그 위치의 높이를 발밑 선으로 쓴다
        /// (용병처럼 파츠 여러 장이 같은 발밑을 쓰게). 없으면 그림에서 보이는 맨 아래가 발밑 선.
        /// </summary>
        public static BattleShadow Attach(Image source, RectTransform layer, float heightScale, Transform groundAnchor = null)
        {
            if (source == null || layer == null) return null;
            var go = new GameObject(source.name + " Shadow", typeof(RectTransform), typeof(Image), typeof(BattleShadow));
            go.transform.SetParent(layer, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.material = Material;
            var shadow = go.GetComponent<BattleShadow>();
            shadow._source = source;
            shadow._image = image;
            shadow._heightScale = heightScale;
            shadow._groundAnchor = groundAnchor;
            shadow.Follow();
            return shadow;
        }

        /// <summary>
        /// 유닛 그림자: 파츠(용병 팔·몸·머리 / 몬스터 한 장)마다 그림자를 만들어 모두 유닛 발밑(root)을 기준선으로 쓴다.
        /// 발밑 동그란 그림자(blob)는 해 그림자가 진할수록 옅어진다.
        /// </summary>
        public static void AttachUnit(Transform root, IEnumerable<Image> parts, Image blob)
        {
            BattleLighting.RegisterBlob(blob);
            if (root != null && BattleLighting.UnitLabelLayer != null) root.gameObject.AddComponent<BattleUnitLabels>();
            var layer = BattleLighting.UnitShadowLayer;
            if (layer == null || parts == null) return;
            foreach (var part in parts) Attach(part, layer, 0.8f, root);
        }

        private static Material Material
        {
            get
            {
                if (_material != null) return _material;
                var shader = Resources.Load<Shader>("Shaders/UIProjectedShadow");
                if (shader == null)
                {
                    Debug.LogWarning("[BattleShadow] Resources/Shaders/UIProjectedShadow를 찾지 못했습니다.");
                    return null;
                }
                return _material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
        }

        private void LateUpdate()
        {
            if (_source == null)
            {
                Destroy(gameObject);
                return;
            }
            Follow();
        }

        /// <summary>원본 Image의 그림·자리·크기·뒤집기·진하기를 따라가고, 바뀌었으면 메시를 다시 만든다.</summary>
        private void Follow()
        {
            var rect = (RectTransform)transform;
            var src = _source.rectTransform;
            var parent = rect.parent;

            _image.sprite = _source.sprite;
            _image.preserveAspect = _source.preserveAspect;
            _image.type = Image.Type.Simple;
            rect.pivot = src.pivot;
            rect.sizeDelta = src.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.position = src.position;
            rect.rotation = src.rotation;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
            Vector3 srcScale = src.lossyScale;
            rect.localScale = new Vector3(SafeDiv(srcScale.x, parentScale.x), SafeDiv(srcScale.y, parentScale.y), 1f);

            var sky = BattleLighting.Current;
            float alpha = sky.ShadowAlpha * _source.color.a * _source.canvasRenderer.GetInheritedAlpha();
            _image.color = new Color(0f, 0f, 0f, alpha);
            _image.enabled = _source.isActiveAndEnabled && _source.sprite != null && alpha > 0.002f;

            if (sky.ShadowDir != _lastDir || rect.position != _lastPosition || rect.sizeDelta != _lastSize || _image.sprite != _lastSprite || _groundAnchor != null)
            {
                _lastDir = sky.ShadowDir;
                _lastPosition = rect.position;
                _lastSize = rect.sizeDelta;
                _lastSprite = _image.sprite;
                _image.SetVerticesDirty();
            }
        }

        private static float SafeDiv(float a, float b) => Mathf.Abs(b) < 1e-5f ? a : a / b;

        /// <summary>
        /// Image가 만든 사각형을 발밑 선에서 위·아래 두 장으로 나누고(발밑 아래는 제자리), 위 장의 꼭짓점을 높이만큼 민다.
        /// 사각형 한 장을 그대로 기울이면 발밑 아래 투명 여백 기준으로 기울어 그림자 발이 본체에서 조금 떠서 나눈다.
        /// </summary>
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _source == null || vh.currentVertCount != 4) return;

            var verts = new List<UIVertex>(4);
            for (int i = 0; i < 4; i++)
            {
                var v = new UIVertex();
                vh.PopulateUIVertex(ref v, i);
                verts.Add(v);
            }
            // Simple Image: 0 왼아래, 1 왼위, 2 오른위, 3 오른아래
            float yMin = verts[0].position.y, yMax = verts[1].position.y;
            float xMin = verts[0].position.x, xMax = verts[3].position.x;
            Vector2 uvMin = verts[0].uv0, uvMax = verts[2].uv0;
            if (yMax - yMin < 0.01f) return;

            float baseY = BaseLineLocal(yMin, yMax);
            baseY = Mathf.Clamp(baseY, yMin, yMax);
            float baseT = (baseY - yMin) / (yMax - yMin);
            float baseV = Mathf.Lerp(uvMin.y, uvMax.y, baseT);

            // 그림자 벡터를 이 그림의 로컬 좌표로: 층 좌표 높이 × 비율 × 방향, 로컬 축 배율(뒤집기 포함)로 나눈다.
            Vector3 scale = transform.localScale;
            Vector2 dir = BattleLighting.Current.ShadowDir * _heightScale;
            float sy = Mathf.Abs(scale.y) < 1e-5f ? 1f : scale.y;
            float sx = Mathf.Abs(scale.x) < 1e-5f ? 1f : scale.x;
            Vector2 Shift(float y)
            {
                float height = Mathf.Max(y - baseY, 0f) * sy; // 층 좌표 높이
                return new Vector2(height * dir.x / sx, height * dir.y / sy);
            }

            var color = verts[0].color;
            vh.Clear();
            // 발밑 아래(대개 투명 여백): 제자리
            AddQuad(vh, color,
                new Vector2(xMin, yMin), new Vector2(xMin, baseY), new Vector2(xMax, baseY), new Vector2(xMax, yMin),
                new Vector2(uvMin.x, uvMin.y), new Vector2(uvMin.x, baseV), new Vector2(uvMax.x, baseV), new Vector2(uvMax.x, uvMin.y));
            // 발밑 위: 높이만큼 해 반대쪽으로
            Vector2 top = Shift(yMax);
            AddQuad(vh, color,
                new Vector2(xMin, baseY), new Vector2(xMin, yMax) + top, new Vector2(xMax, yMax) + top, new Vector2(xMax, baseY),
                new Vector2(uvMin.x, baseV), new Vector2(uvMin.x, uvMax.y), new Vector2(uvMax.x, uvMax.y), new Vector2(uvMax.x, baseV));
        }

        private static void AddQuad(VertexHelper vh, Color32 color, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int start = vh.currentVertCount;
            vh.AddVert(p0, color, uv0);
            vh.AddVert(p1, color, uv1);
            vh.AddVert(p2, color, uv2);
            vh.AddVert(p3, color, uv3);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        /// <summary>발밑 선(이 그림자 로컬 y). 기준점이 있으면 그 높이, 없으면 그림에서 보이는 맨 아래.</summary>
        private float BaseLineLocal(float yMin, float yMax)
        {
            if (_groundAnchor != null) return transform.InverseTransformPoint(_groundAnchor.position).y;
            var sprite = _image.sprite;
            return sprite == null ? yMin : Mathf.Lerp(yMin, yMax, BottomFraction(sprite));
        }

        /// <summary>그림에서 알파가 기준 이상인 가장 아래 줄의 높이 비율(0 = 맨 아래). 그림마다 한 번 GPU에서 읽어 둔다.</summary>
        private static float BottomFraction(Sprite sprite)
        {
            if (_bottoms.TryGetValue(sprite, out float cached)) return cached;
            float fraction = 0f;
            var texture = sprite.texture;
            var pixels = ReadPixels(texture);
            if (pixels != null)
            {
                var r = sprite.textureRect;
                int x0 = Mathf.FloorToInt(r.x), x1 = Mathf.CeilToInt(r.xMax), y0 = Mathf.FloorToInt(r.y), y1 = Mathf.CeilToInt(r.yMax);
                byte threshold = (byte)(AlphaThreshold * 255f);
                for (int y = y0; y < y1; y++)
                {
                    bool found = false;
                    for (int x = x0; x < x1; x++)
                        if (pixels[y * texture.width + x].a >= threshold) { found = true; break; }
                    if (found)
                    {
                        fraction = (y - y0) / Mathf.Max(1f, y1 - y0);
                        break;
                    }
                }
            }
            _bottoms[sprite] = fraction;
            return fraction;
        }

        /// <summary>읽기 금지 텍스처도 GPU에서 복사해 읽는다(텍스처마다 한 번).</summary>
        private static Color32[] ReadPixels(Texture2D texture)
        {
            if (texture == null) return null;
            if (_pixels.TryGetValue(texture, out var cached)) return cached;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels32();
            Destroy(copy);
            _pixels[texture] = pixels;
            return pixels;
        }
    }

    /// <summary>
    /// 유닛의 이름·체력 막대처럼 그림이 아닌 것을 빛 막 위 층(BattleLighting.UnitLabelLayer)으로 옮겨, 밤·비에도 어두워지지 않게 한다
    /// (마을 이름표가 조명을 안 받는 것과 같다). 옮긴 것은 유닛 자리·크기·투명도를 매 프레임 따라가는 대리 칸에 담는다.
    /// 유닛 그림(Body)·발밑 그림자·고리는 그대로 둔다. 유닛을 만든 뒤(첫 프레임)에 옮긴다 — 이름·막대는 이 컴포넌트를 붙인 뒤에 만들어진다.
    /// </summary>
    public class BattleUnitLabels : MonoBehaviour
    {
        private static readonly HashSet<string> Stay = new HashSet<string> { "Body", "Shadow", "LeaderRing", "TargetRing" };

        private RectTransform _root;
        private CanvasGroup _rootGroup;
        private RectTransform _proxy;
        private CanvasGroup _proxyGroup;

        private void Start()
        {
            var layer = BattleLighting.UnitLabelLayer;
            if (layer == null) return;
            _root = (RectTransform)transform;
            _rootGroup = GetComponent<CanvasGroup>();

            var go = new GameObject(name + " Labels", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(layer, false);
            _proxy = (RectTransform)go.transform;
            _proxyGroup = go.GetComponent<CanvasGroup>();
            _proxyGroup.blocksRaycasts = _rootGroup == null || _rootGroup.blocksRaycasts;
            Follow();

            var move = new List<Transform>();
            foreach (Transform child in transform)
                if (!Stay.Contains(child.name)) move.Add(child);
            foreach (var child in move) child.SetParent(_proxy, false); // 로컬 배치(유닛 발 기준)는 그대로
        }

        private void LateUpdate()
        {
            if (_proxy != null) Follow();
        }

        private void Follow()
        {
            _proxy.anchorMin = _root.anchorMin;
            _proxy.anchorMax = _root.anchorMax;
            _proxy.pivot = _root.pivot;
            _proxy.sizeDelta = _root.sizeDelta;
            _proxy.anchoredPosition = _root.anchoredPosition;
            _proxy.localScale = _root.localScale;
            _proxy.localRotation = _root.localRotation;
            if (_rootGroup != null) _proxyGroup.alpha = _rootGroup.alpha;
            bool active = gameObject.activeInHierarchy;
            if (_proxy.gameObject.activeSelf != active) _proxy.gameObject.SetActive(active);
        }

        private void OnDisable()
        {
            if (_proxy != null) _proxy.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_proxy != null) Destroy(_proxy.gameObject);
        }
    }
}
