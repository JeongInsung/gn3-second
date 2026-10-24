using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 부모 물체의 그림을 그대로 받아 "GN3/ProjectedShadow" 셰이더로 바닥에 눕혀 그리는 그림자.
    /// 길이·방향·진하기는 DayNightCycle이 전역으로 정하고, 여기서는 물체 발밑 높이(_BaseY)만 넣는다.
    /// 부모 그림이 애니메이션으로 바뀌어도(분수) 따라간다. 마을이 넓어져 그림자가 수백 개라, 원본 그림·위치·크기·뒤집기가
    /// 지난 프레임과 같으면 아무것도 하지 않는다(가만히 있는 건물·나무·바위는 매 프레임 거의 공짜). 보이는 맨 아래 정점은 그림마다 한 번만 잰다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class ProjectedShadow : MonoBehaviour
    {
        private const int ShadowSortingOrder = 0; // 바닥(-1)보다 위, 건물·장식(1)보다 아래
        private static readonly int BaseYId = Shader.PropertyToID("_BaseY");
        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");

        [Tooltip("그림 세로 길이 중 실제 높이의 비율. 위에서 내려다보는 그림이라 건물은 절반 가까이가 지붕(깊이)이다. " +
                 "그림자 길이 = 그림 높이 × 이 값 × cot(태양 고도). " +
                 "벤치·분수처럼 위에서 똑바로 본 납작한 물체는 0으로 두고 Lift Height를 쓴다.")]
        [Range(0f, 1f)] public float heightScale = 0.8f;
        [Tooltip("납작한 물체(Height Scale 0)의 실제 높이(월드 유닛). 그림자가 기울지 않고 모양 그대로 이만큼×cot(고도) 옆에 떨어진다.")]
        [Min(0f)] public float liftHeight;
        [Tooltip("비워 두면 부모 그림을 따라간다. 지정하면 이 렌더러의 그림·위치·크기·켜짐을 따라간다(파츠 조합 캐릭터처럼 그림자를 그룹 밖에 둘 때).")]
        public SpriteRenderer sourceOverride;
        [Tooltip("비워 두면 그림의 보이는 맨 아래가 기준선. 지정하면 이 위치의 y(발밑)를 기준선으로 쓴다(여러 파츠가 같은 기준선을 쓰게).")]
        public Transform groundAnchor;
        private float _lastHeightScale = float.NaN;
        private float _lastLift = float.NaN;
        private static readonly int LiftId = Shader.PropertyToID("_Lift");

        private SpriteRenderer _shadow;
        private SpriteRenderer _source;
        private MaterialPropertyBlock _block;
        private float _lastBaseY = float.NaN;

        // 납작 그림자(Height Scale 0)는 Lift만큼 통째로 밀려서, 가는 물체(울타리 기둥)는 본체와 그림자 사이에 바닥이 보였다.
        // 0~Lift 사이를 FillSpacing 간격의 숨은 사본으로 촘촘히 메운다(셰이더 스텐실이 한 픽셀을 한 번만 칠해 진하기는 같다).
        // 저장되지 않는 사본이라 씬·프리팹은 그대로다.
        private const string ShadowChildName = "Shadow";
        private const string FillChildName = "Shadow Fill";
        private const float FillSpacing = 0.05f;     // 월드 유닛. 울타리 기둥 굵기보다 좁게
        private const float FillMaxLengthRatio = 0.8f; // DayNightCycle.maxShadowLengthRatio(가장 길게 밀릴 때)
        private readonly List<SpriteRenderer> _fills = new List<SpriteRenderer>();
        private Sprite _fillSprite;
        private bool _fillEnabled;

        // 지난 LateUpdate의 원본 상태 — 같으면 건너뛴다
        private bool _snapValid;
        private Sprite _snapSprite;
        private bool _snapFlipX, _snapFlipY, _snapEnabled;
        private Vector3 _snapPosition, _snapScale;
        private Quaternion _snapRotation;
        private float _snapAnchorY, _snapHeightScale, _snapLift;

        // 그림마다 정점(sprite.vertices는 부를 때마다 새 배열을 만든다)과 로컬 y 범위(최저, 최고)
        private static readonly Dictionary<Sprite, Vector2[]> SpriteVertices = new Dictionary<Sprite, Vector2[]>();
        private static readonly Dictionary<Sprite, Vector2> SpriteYRange = new Dictionary<Sprite, Vector2>();

        private void OnEnable()
        {
            _lastBaseY = float.NaN;
            _snapValid = false;
            Sync();
        }

        // 켜고 끄는 중에는 자식을 지울 수 없어(활성화 중 파괴 오류) 숨기기만 한다. 만들고 지우는 일은 LateUpdate에서.
        private void OnDisable()
        {
            foreach (var fill in _fills)
                if (fill != null) fill.enabled = false;
            _fillEnabled = false;
        }

        private void ClearFills()
        {
            // 스크립트 리로드로 목록을 잃어도 남은 사본이 쌓이지 않게 자식 이름으로 지운다.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name != FillChildName) continue;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _fills.Clear();
        }

        /// <summary>본 그림자("Shadow")가 납작 그림자면 0~Lift 사이 사본 개수. 쓸기 사본("Shadow Sweep k")·선 물체는 0.</summary>
        private int FillCount() =>
            name == ShadowChildName && heightScale <= 0f && liftHeight > 0f
                ? Mathf.CeilToInt(liftHeight * FillMaxLengthRatio / FillSpacing) - 1
                : 0;

        private void SyncFills(float baseY)
        {
            int count = FillCount();
            if (_fills.Count != count)
            {
                ClearFills();
                for (int i = 0; i < count; i++)
                {
                    var go = new GameObject(FillChildName, typeof(SpriteRenderer)) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(transform, false);
                    _fills.Add(go.GetComponent<SpriteRenderer>());
                }
            }
            for (int i = 0; i < _fills.Count; i++)
            {
                var fill = _fills[i];
                if (fill == null) continue;
                fill.sharedMaterial = _shadow.sharedMaterial;
                fill.sprite = _shadow.sprite;
                fill.flipX = _shadow.flipX;
                fill.flipY = _shadow.flipY;
                fill.sortingLayerID = _shadow.sortingLayerID;
                fill.sortingOrder = _shadow.sortingOrder;
                fill.enabled = _shadow.enabled;
                fill.GetPropertyBlock(_block);
                _block.SetFloat(BaseYId, baseY);
                _block.SetFloat(HeightScaleId, 0f);
                _block.SetFloat(LiftId, liftHeight * (i + 1) / (_fills.Count + 1));
                fill.SetPropertyBlock(_block);
            }
        }

        // 부모가 바뀌면(에디터에서 만들자마자 부모에 붙이는 경우 포함) 다시 찾는다.
        private void OnTransformParentChanged()
        {
            _source = null;
            _lastBaseY = float.NaN;
            _snapValid = false;
            Sync();
        }

        private void LateUpdate()
        {
            if (!Unchanged()) Sync(true);
        }

        /// <summary>원본 그림·뒤집기·켜짐·위치·회전·크기·기준선·값이 지난번과 같은가(같으면 다시 맞출 게 없다). 다르면 지금 상태를 적어 둔다.</summary>
        private bool Unchanged()
        {
            var source = sourceOverride != null ? sourceOverride : _source;
            if (source == null || _shadow == null) { _snapValid = false; return false; }
            var t = source.transform;
            Vector3 position = t.position, scale = t.lossyScale;
            Quaternion rotation = t.rotation;
            float anchorY = groundAnchor != null ? groundAnchor.position.y : 0f;
            bool same = _snapValid && _snapSprite == source.sprite && _snapFlipX == source.flipX && _snapFlipY == source.flipY
                        && _snapEnabled == source.enabled && _snapPosition == position && _snapRotation == rotation && _snapScale == scale
                        && _snapAnchorY == anchorY && _snapHeightScale == heightScale && _snapLift == liftHeight
                        && _fills.Count == FillCount();
            if (same) return true;
            _snapValid = true;
            _snapSprite = source.sprite;
            _snapFlipX = source.flipX;
            _snapFlipY = source.flipY;
            _snapEnabled = source.enabled;
            _snapPosition = position;
            _snapRotation = rotation;
            _snapScale = scale;
            _snapAnchorY = anchorY;
            _snapHeightScale = heightScale;
            _snapLift = liftHeight;
            return false;
        }

        private void Sync(bool canBuildFills = false)
        {
            if (_shadow == null) _shadow = GetComponent<SpriteRenderer>();
            if (sourceOverride != null) _source = sourceOverride;
            else if (_source == null && transform.parent != null) _source = transform.parent.GetComponent<SpriteRenderer>();
            _block ??= new MaterialPropertyBlock();
            if (_shadow == null || _source == null) return;

            if (sourceOverride != null)
            {
                transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
                transform.localScale = _source.transform.lossyScale;
                _shadow.enabled = _source.enabled;
            }
            if (_source.sprite == null) return;

            if (_shadow.sprite != _source.sprite) _shadow.sprite = _source.sprite;
            _shadow.flipX = _source.flipX;
            _shadow.flipY = _source.flipY;
            _shadow.sortingLayerID = _source.sortingLayerID;
            _shadow.sortingOrder = ShadowSortingOrder;

            float baseY = groundAnchor != null ? groundAnchor.position.y : VisibleBottomY(_source);
            bool unchanged = Mathf.Approximately(baseY, _lastBaseY) && Mathf.Approximately(heightScale, _lastHeightScale)
                             && Mathf.Approximately(liftHeight, _lastLift);
            if (canBuildFills && (!unchanged || _fills.Count != FillCount() || _fillSprite != _shadow.sprite || _fillEnabled != _shadow.enabled))
            {
                SyncFills(baseY);
                _fillSprite = _shadow.sprite;
                _fillEnabled = _shadow.enabled;
            }
            if (unchanged) return;
            _lastBaseY = baseY;
            _lastHeightScale = heightScale;
            _lastLift = liftHeight;
            _shadow.GetPropertyBlock(_block);
            _block.SetFloat(BaseYId, baseY);
            _block.SetFloat(HeightScaleId, heightScale);
            _block.SetFloat(LiftId, liftHeight);
            _shadow.SetPropertyBlock(_block);
        }

        /// <summary>
        /// 그림에서 실제로 보이는 부분의 맨 아래(월드 y). bounds는 투명 여백까지 포함해 그림자가 발밑에서 떠 보여서,
        /// 불투명 부분을 감싸는 스프라이트 메시(Tight) 정점의 최저점을 쓴다.
        /// </summary>
        private static float VisibleBottomY(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            if (!SpriteVertices.TryGetValue(sprite, out var vertices))
            {
                vertices = sprite.vertices;
                SpriteVertices[sprite] = vertices;
            }
            if (vertices.Length == 0) return renderer.bounds.min.y;

            var t = renderer.transform;
            if (t.rotation == Quaternion.identity)
            {
                // 돌지 않은 그림(거의 전부): 월드 y = 위치 + 로컬 y × 크기. 크기가 음수면 로컬 최고점이 맨 아래가 된다.
                if (!SpriteYRange.TryGetValue(sprite, out var range))
                {
                    range = new Vector2(float.MaxValue, float.MinValue);
                    foreach (var v in vertices) range = new Vector2(Mathf.Min(range.x, v.y), Mathf.Max(range.y, v.y));
                    SpriteYRange[sprite] = range;
                }
                float scaleY = t.lossyScale.y;
                return t.position.y + (scaleY >= 0f ? range.x : range.y) * scaleY;
            }
            float minY = float.MaxValue;
            foreach (var v in vertices)
                minY = Mathf.Min(minY, t.TransformPoint(v).y);
            return minY;
        }
    }
}
