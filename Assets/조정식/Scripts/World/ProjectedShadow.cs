using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 부모 물체의 그림을 그대로 받아 "GN3/ProjectedShadow" 셰이더로 바닥에 눕혀 그리는 그림자.
    /// 길이·방향·진하기는 DayNightCycle이 전역으로 정하고, 여기서는 물체 발밑 높이(_BaseY)만 넣는다.
    /// 부모 그림이 애니메이션으로 바뀌어도(분수) 매 프레임 따라간다.
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

        private void OnEnable()
        {
            _lastBaseY = float.NaN;
            Sync();
        }

        // 부모가 바뀌면(에디터에서 만들자마자 부모에 붙이는 경우 포함) 다시 찾는다.
        private void OnTransformParentChanged()
        {
            _source = null;
            _lastBaseY = float.NaN;
            Sync();
        }

        private void LateUpdate() => Sync();

        private void Sync()
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
            if (Mathf.Approximately(baseY, _lastBaseY) && Mathf.Approximately(heightScale, _lastHeightScale)
                && Mathf.Approximately(liftHeight, _lastLift)) return;
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
            var vertices = renderer.sprite.vertices;
            float minY = float.MaxValue;
            foreach (var v in vertices)
                minY = Mathf.Min(minY, renderer.transform.TransformPoint(v).y);
            return vertices.Length > 0 ? minY : renderer.bounds.min.y;
        }
    }
}
