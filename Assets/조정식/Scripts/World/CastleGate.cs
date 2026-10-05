using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 파견대가 드나드는 성문. 지나갈 사람(Pass로 등록)이 성문 가까이 오면 열림 프레임(0 닫힘 → 마지막 활짝)을 재생해 열고,
    /// 모두 지나가 멀어지거나 사라지면 잠시 뒤 거꾸로 재생해 닫는다 → 출발·귀환 모두 "다가오면 열어 주는" 느낌.
    /// 프레임과 자리는 에디터 메뉴 "GN3/Pixel/성문 열림 애니메이션 만들기"가 넣는다(성벽 그룹의 가장 아래 성문).
    /// 이 오브젝트는 발밑(성문 아래 가운데)에 있는 정렬 부모 아래의 그림이다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CastleGate : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float framesPerSecond = 10f;
        [Tooltip("지나갈 사람이 문간에서 이 거리(유닛) 안에 들어오면 연다")]
        [SerializeField] private float openDistance = 1.5f;
        [Tooltip("마지막 사람이 멀어진 뒤 닫기 시작할 때까지(초)")]
        [SerializeField] private float closeDelay = 1f;
        [Tooltip("성문 발밑 기준 각 지점(유닛): 안쪽 바닥 / 문간 / 성벽 바로 밖 / 사라지는 곳")]
        [SerializeField] private float insideOffset = 1.5f;
        [SerializeField] private float doorwayOffset = 0.25f;
        [SerializeField] private float outsideOffset = -0.6f;
        [SerializeField] private float farOffset = -1.6f;

        /// <summary>파견 출입용 성문(씬에 하나). 없으면 null → 출발대는 예전처럼 화면 아래 출구에서 사라진다.</summary>
        public static CastleGate Expedition { get; private set; }

        private readonly List<Transform> _passers = new List<Transform>();
        private readonly HashSet<Transform> _passed = new HashSet<Transform>(); // 한 번이라도 문간 가까이 왔던 사람
        private SpriteRenderer _renderer;
        private float _frame;          // 0 ~ 마지막 프레임
        private float _lastNearTime = float.NegativeInfinity;

        // Domain Reload가 꺼져 있어도 Play마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => Expedition = null;

        public void Configure(Sprite[] openFrames)
        {
            frames = openFrames;
            GetComponent<SpriteRenderer>().sprite = frames[0];
        }

        private float BaseX => transform.parent != null ? transform.parent.position.x : transform.position.x;
        private float BaseY => transform.parent != null ? transform.parent.position.y : transform.position.y;
        private Vector2 At(float offsetY) => new Vector2(BaseX, BaseY + offsetY);

        public Vector2 Inside => At(insideOffset);
        public Vector2 Doorway => At(doorwayOffset);
        public Vector2 Outside => At(outsideOffset);
        public Vector2 Far => At(farOffset);

        /// <summary>마을 안 → 성벽 밖으로 걸어 나갈 때 지나는 지점들(안쪽 → 문간 → 밖 → 사라지는 곳).</summary>
        public Vector2[] OutwardSteps => new[] { Inside, Doorway, Outside, Far };
        /// <summary>성벽 밖 → 마을 안으로 들어올 때 지나는 지점들.</summary>
        public Vector2[] InwardSteps => new[] { Far, Outside, Doorway, Inside };

        /// <summary>이 사람이 성문을 지나간다(가까이 오면 열린다). 지나가 멀어지거나 사라지면 저절로 목록에서 빠진다.</summary>
        public void Pass(Transform walker)
        {
            if (walker != null && !_passers.Contains(walker)) _passers.Add(walker);
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length == 0) return;
            Expedition = this;
            _renderer.sprite = frames[0];
        }

        private void OnDestroy()
        {
            if (Expedition == this) Expedition = null;
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0) return;

            bool near = false;
            Vector2 doorway = Doorway;
            for (int i = _passers.Count - 1; i >= 0; i--)
            {
                var walker = _passers[i];
                if (walker == null || !walker.gameObject.activeInHierarchy)
                {
                    _passed.Remove(walker);
                    _passers.RemoveAt(i);
                    continue;
                }
                float distance = Vector2.Distance(walker.position, doorway);
                if (distance <= openDistance)
                {
                    near = true;
                    _passed.Add(walker);
                }
                else if (_passed.Contains(walker) && distance > openDistance * 2f)
                {
                    // 문을 지나 멀어졌다(귀환해서 마을로 들어간 사람 등)
                    _passed.Remove(walker);
                    _passers.RemoveAt(i);
                }
            }
            if (near) _lastNearTime = Time.time;

            bool open = Time.time - _lastNearTime < closeDelay;
            float last = frames.Length - 1;
            _frame = Mathf.MoveTowards(_frame, open ? last : 0f, framesPerSecond * Time.deltaTime);
            _renderer.sprite = frames[Mathf.Clamp(Mathf.RoundToInt(_frame), 0, frames.Length - 1)];
        }
    }
}
