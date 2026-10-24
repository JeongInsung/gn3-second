using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// [건설] 창 [장식품]으로 놓은 소: 한동안 서 있다가(다리를 모은 그림) 놓인 자리 둘레(반경 Roam)를 느릿느릿 걷는다.
    /// 구조(DecorTemplateBuilder가 만든다): 루트(놓는 자리) → Home(걷는 몸, 로컬 위치만 바뀜) → Visual(SortingGroup) → 그림, Home 아래 그림자.
    /// 그림자는 VillagePenCow처럼 sourceOverride·groundAnchor라 길찾기 장애물이 되지 않는다(움직이는 동물이 길을 막지 않게).
    /// </summary>
    public class VillageDecorCow : MonoBehaviour
    {
        private const float Roam = 0.4f;
        private const float WalkSpeed = 0.09f;      // VillagePenCow와 같음
        private const float FramesPerSecond = 3f;
        private const float MinIdleSeconds = 2f;
        private const float MaxIdleSeconds = 7f;
        private const float MinWalkDistance = 0.15f;

        [SerializeField] private Transform home;
        [SerializeField] private SpriteRenderer art;
        [SerializeField] private Sprite[] walkFrames; // 오른쪽을 보고 걷는 8프레임
        [SerializeField] private Sprite idleSprite;

        private Vector2 _target;
        private bool _walking;
        private float _idleTimer;
        private float _frameTime;

        public void Configure(Transform homeTransform, SpriteRenderer renderer, Sprite[] frames, Sprite idle)
        {
            home = homeTransform;
            art = renderer;
            walkFrames = frames;
            idleSprite = idle;
        }

        private void OnEnable()
        {
            if (art == null) return;
            art.flipX = Random.value < 0.5f;
            StartIdle();
        }

        private void StartIdle()
        {
            _walking = false;
            _idleTimer = Random.Range(MinIdleSeconds, MaxIdleSeconds);
            if (idleSprite != null) art.sprite = idleSprite;
        }

        private void StartWalk()
        {
            _target = Random.insideUnitCircle * Roam;
            if (Vector2.Distance(home.localPosition, _target) < MinWalkDistance) { StartIdle(); return; }
            _walking = true;
            _frameTime = 0f;
            float dx = _target.x - home.localPosition.x;
            if (Mathf.Abs(dx) > 0.01f) art.flipX = dx < 0f; // 시트는 오른쪽을 본다
        }

        private void Update()
        {
            if (home == null || art == null || walkFrames == null || walkFrames.Length == 0) return;
            // 옮기는 중(미리보기를 든 동안)에는 제자리에 서 있는다
            if (BuildPlacement.HeldSlot != null && BuildPlacement.HeldSlot.Transform == transform)
            {
                if (_walking) StartIdle();
                return;
            }
            if (!_walking)
            {
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f) StartWalk();
                return;
            }

            Vector2 next = Vector2.MoveTowards(home.localPosition, _target, WalkSpeed * Time.deltaTime);
            home.localPosition = new Vector3(next.x, next.y, home.localPosition.z);
            _frameTime += Time.deltaTime;
            art.sprite = walkFrames[Mathf.FloorToInt(_frameTime * FramesPerSecond) % walkFrames.Length];
            if (next == _target) StartIdle();
        }
    }
}
