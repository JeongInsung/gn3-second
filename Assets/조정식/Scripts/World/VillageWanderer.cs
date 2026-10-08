using System.Collections.Generic;
using GN3.CharacterAnim;
using UnityEngine;
using UnityEngine.Rendering;

namespace GN3.World
{
    /// <summary>
    /// 파츠 조합 캐릭터 한 명이 마을 안에서 랜덤 지점으로 걷다가 잠깐 멈춰 서기를 반복한다.
    /// 외형은 ComposedCharacter의 파츠별 출처 캐릭터에서 idle/walk 시트를 불러 그대로 재생한다
    /// (모든 파츠 시트가 같은 256x256 좌표계라 파츠 렌더러를 전부 (0,0)에 두고 프레임만 바꾸면 맞물린다).
    /// </summary>
    public class VillageWanderer : MonoBehaviour
    {
        private const string IdleClip = "idle";
        private const string WalkClip = "walk";

        // 파츠 시트(PPU 64, 캔버스 4유닛, 캐릭터 약 1.9유닛)를 마을 건물(집 한 채 약 2.5유닛) 크기에 맞춘다.
        private const float CharacterScale = 0.3f;
        private const float PixelsPerUnit = 64f;
        private const float FootOffsetPx = 58f; // pivot(캔버스 아래)에서 발까지
        private const bool SheetFacesRight = true;

        private const int SortingOrder = 1; // 건물·장식과 같은 order → Renderer2D의 Y축 정렬로 앞뒤가 정해진다
        private const float MinIdleSeconds = 1.5f;
        private const float MaxIdleSeconds = 5f;
        private const float MinWalkDistance = 0.4f;
        private const float MaxWalkDistance = 1.5f;
        private const float WalkSpeed = 0.5f;
        private const int WalkAttempts = 6;
        private const float MaxDetourRatio = 2f; // 경로가 직선거리의 이 배수를 넘으면(건물을 크게 빙 돎) 다른 목표를 고른다
        private const float ShadowHeightScale = 0.5f; // 그림자 길이 비율(건물과 같은 값). 높이면 길어진다

        // 친한 동료 따라 걷기(VillagePartyPresenter가 Follow로 붙인다)
        private const float FollowRepathInterval = 0.4f; // 걷는 중 이 간격마다 리더가 움직였는지 본다
        private const float FollowRepathMove = 0.2f;     // 리더가 이만큼 움직였으면 길을 다시 찾는다
        private const float FollowCloseEnough = 0.15f;   // 자리(리더 옆)와 이만큼 가까우면 걷지 않고 쉰다
        private const float FollowCatchUpDistance = 0.35f; // 이보다 멀면 빨리 걸어 따라잡는다
        private const float FollowCatchUpSpeed = 1.25f;
        private const float FollowIdleMin = 0.3f;
        private const float FollowIdleMax = 0.8f;

        // 호버 테두리: 흰 실루엣을 8방향으로 이만큼(Body 로컬) 밀어 파츠 뒤에 그린다. 0.06 × 0.3 ≈ 화면 2px(1080p)
        private const float OutlineOffset = 0.06f;
        private const int OutlineSortingOrder = -1; // 파츠(0~) 뒤
        private const float OutlinePulseSpeed = 3f;  // SelectableBuilding과 같은 숨쉬는 밝기
        // 마우스 판정 범위(발 기준 월드): 몸통 폭·키
        private const float PickHalfWidth = 0.14f;
        private const float PickHeight = 0.6f;

        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, 0.7071f), new Vector2(-0.7071f, -0.7071f),
        };

        private static Material _outlineMaterial;

        public static readonly List<VillageWanderer> All = new List<VillageWanderer>();

        // Domain Reload가 꺼져 있어도 Play마다 목록을 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => All.Clear();

        /// <summary>이 캐릭터가 누구인지 밖에서 붙여 두는 값(VillagePartyPresenter가 용병 Id를 넣는다).</summary>
        public string OwnerId { get; set; }
        public bool IsHighlighted { get; private set; }

        private class Clip
        {
            public Dictionary<string, Sprite[]> Frames = new Dictionary<string, Sprite[]>();
            public string[] ZOrder;
            public float Fps;
            public int FrameCount;
        }

        private readonly Dictionary<string, SpriteRenderer> _renderers = new Dictionary<string, SpriteRenderer>();
        private readonly Dictionary<string, SpriteRenderer[]> _outlines = new Dictionary<string, SpriteRenderer[]>();
        private Transform _body;
        private GameObject _outlineRoot;
        private Transform _shadows;
        private Material _shadowMaterial;
        private Clip _idle;
        private Clip _walk;
        private Clip _current;
        private float _frameTime;

        private VillageNavGrid _grid;
        private System.Random _rng;
        private bool _walking;
        private float _idleTimer;
        private readonly List<Vector2> _path = new List<Vector2>();
        private int _pathIndex;
        private bool _entering;
        private System.Action _onEntered;
        private VillageWanderer _leader;
        private Vector2 _followOffset;
        private Vector2 _leaderPosAtPath;
        private float _repathTimer;
        private float _speedMultiplier = 1f;
        private bool _scriptedWalk; // 문에서 나오기·마을로 걸어 들어오기: 따라가기로 끊지 않는다

        /// <summary>따라 걷고 있는 동료(없으면 null).</summary>
        public VillageWanderer Leader => _leader;

        /// <summary>건물(여관)로 걸어 들어가는 중. 이때는 마우스로 고를 수 없다.</summary>
        public bool IsEntering => _entering;

        /// <summary>
        /// 구조: 루트(발 위치) 아래 Visual(SortingGroup) → Body(축소·뒤집기) → 파츠, 그리고 그룹 밖 Shadows → 파츠별 그림자.
        /// 그림자를 SortingGroup 안에 두면 그룹 order(1)로 묶여 건물 위에 그려지므로 밖에 둔다.
        /// shadowMaterial이 null이면 그림자를 만들지 않는다.
        /// </summary>
        public void Init(ComposedCharacter appearance, VillageNavGrid grid, System.Random rng, Material shadowMaterial)
        {
            _grid = grid;
            _rng = rng;
            _shadowMaterial = shadowMaterial;

            var visual = new GameObject("Visual", typeof(SortingGroup));
            visual.transform.SetParent(transform, false);
            visual.GetComponent<SortingGroup>().sortingOrder = SortingOrder;

            if (_shadowMaterial != null)
            {
                _shadows = new GameObject("Shadows").transform;
                _shadows.SetParent(transform, false);
            }

            _body = new GameObject("Body").transform;
            _body.SetParent(visual.transform, false);
            _body.localPosition = new Vector3(0f, -FootOffsetPx / PixelsPerUnit * CharacterScale, 0f);
            _body.localScale = Vector3.one * CharacterScale;

            _outlineRoot = new GameObject("Outline");
            _outlineRoot.transform.SetParent(_body, false);
            _outlineRoot.SetActive(false);

            _idle = LoadClip(appearance, IdleClip);
            _walk = LoadClip(appearance, WalkClip);

            StartIdle();
        }

        private static Clip LoadClip(ComposedCharacter appearance, string clipName)
        {
            var manifest = CharacterAnimLibrary.LoadManifest(appearance.BodyCharacter, clipName);
            var clip = new Clip
            {
                ZOrder = manifest?.ZOrderOrDefault ?? PartClipManifest.DefaultZOrderBackToFront,
                Fps = manifest != null && manifest.fps > 0 ? manifest.fps : 8f,
            };
            foreach (var kv in appearance.PartSources)
            {
                var frames = CharacterAnimLibrary.LoadFrames(kv.Value, clipName, kv.Key);
                if (frames.Length == 0) continue; // 이 클립에 없는 파츠(예: walk의 weapon)는 숨긴다
                clip.Frames[kv.Key] = frames;
                clip.FrameCount = Mathf.Max(clip.FrameCount, frames.Length);
            }
            return clip;
        }

        private void Play(Clip clip)
        {
            if (_current == clip) return;
            _current = clip;
            _frameTime = 0f;

            foreach (var renderer in _renderers.Values)
                renderer.enabled = false;
            foreach (var copies in _outlines.Values)
                foreach (var copy in copies) copy.enabled = false;

            int order = 0;
            foreach (var part in clip.ZOrder)
                if (clip.Frames.ContainsKey(part)) GetOrCreateRenderer(part).sortingOrder = order++;
            // z-order에 없는 파츠가 섞여 있으면 맨 앞에 그린다.
            foreach (var part in clip.Frames.Keys)
                if (System.Array.IndexOf(clip.ZOrder, part) < 0) GetOrCreateRenderer(part).sortingOrder = order++;

            foreach (var part in clip.Frames.Keys)
            {
                _renderers[part].enabled = true;
                foreach (var copy in _outlines[part]) copy.enabled = true;
            }
            ShowFrame(0);
        }

        private void ShowFrame(int frame)
        {
            foreach (var kv in _current.Frames)
            {
                var sprite = kv.Value[Mathf.Min(frame, kv.Value.Length - 1)];
                _renderers[kv.Key].sprite = sprite;
                if (!IsHighlighted) continue; // 테두리가 꺼져 있으면 그림 교체를 건너뛴다(켤 때 맞춘다)
                foreach (var copy in _outlines[kv.Key]) copy.sprite = sprite;
            }
        }

        private SpriteRenderer GetOrCreateRenderer(string part)
        {
            if (_renderers.TryGetValue(part, out var existing)) return existing;

            var go = new GameObject(part, typeof(SpriteRenderer));
            go.transform.SetParent(_body, false);
            var renderer = go.GetComponent<SpriteRenderer>();
            _renderers[part] = renderer;
            _outlines[part] = CreateOutlineCopies(part);

            if (_shadows != null)
            {
                // 모든 파츠가 발 위치를 같은 기준선으로 써야 팔·머리 그림자가 따로 꺾이지 않는다.
                var shadowGO = new GameObject(part, typeof(SpriteRenderer));
                shadowGO.transform.SetParent(_shadows, false);
                shadowGO.GetComponent<SpriteRenderer>().sharedMaterial = _shadowMaterial;
                var shadow = shadowGO.AddComponent<ProjectedShadow>();
                shadow.sourceOverride = renderer;
                shadow.groundAnchor = transform;
                shadow.heightScale = ShadowHeightScale;
            }
            return renderer;
        }

        /// <summary>파츠 하나의 흰 실루엣 사본 8장(8방향으로 조금씩 밀림). 모든 파츠 뒤에 있어 바깥 테두리만 보인다.</summary>
        private SpriteRenderer[] CreateOutlineCopies(string part)
        {
            if (_outlineMaterial == null)
            {
                var shader = Resources.Load<Shader>("SpriteSilhouette");
                if (shader == null) Debug.LogWarning("[VillageWanderer] Resources/SpriteSilhouette 셰이더를 찾지 못해 호버 테두리가 안 보입니다.");
                else _outlineMaterial = new Material(shader);
            }

            var copies = new SpriteRenderer[OutlineDirections.Length];
            for (int i = 0; i < copies.Length; i++)
            {
                var go = new GameObject($"{part}_outline{i}", typeof(SpriteRenderer));
                go.transform.SetParent(_outlineRoot.transform, false);
                go.transform.localPosition = OutlineDirections[i] * OutlineOffset;
                var copy = go.GetComponent<SpriteRenderer>();
                if (_outlineMaterial != null) copy.sharedMaterial = _outlineMaterial;
                copy.sortingOrder = OutlineSortingOrder;
                copy.enabled = false;
                copies[i] = copy;
            }
            return copies;
        }

        /// <summary>마우스가 올라가 있는 동안 흰 테두리를 켠다.</summary>
        public void SetHighlighted(bool highlighted)
        {
            if (IsHighlighted == highlighted) return;
            IsHighlighted = highlighted;
            if (_outlineRoot != null) _outlineRoot.SetActive(highlighted);
            if (highlighted && _current != null) ShowFrame(CurrentFrame());
        }

        /// <summary>월드 좌표가 이 캐릭터 몸(발 기준 사각형) 위인지.</summary>
        public bool ContainsPoint(Vector2 world)
        {
            Vector2 feet = transform.position;
            return Mathf.Abs(world.x - feet.x) <= PickHalfWidth && world.y >= feet.y && world.y <= feet.y + PickHeight;
        }

        /// <summary>월드 좌표 위에 있는 캐릭터 중 가장 앞(발이 가장 아래)인 것. 없으면 null.</summary>
        public static VillageWanderer Pick(Vector2 world)
        {
            VillageWanderer best = null;
            foreach (var wanderer in All)
                if (!wanderer.IsEntering && wanderer.ContainsPoint(world) && (best == null || wanderer.transform.position.y < best.transform.position.y))
                    best = wanderer;
            return best;
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            SetHighlighted(false);
        }

        private int CurrentFrame() =>
            _current.FrameCount > 0 ? (int)(_frameTime * _current.Fps) % _current.FrameCount : 0;

        private void StartIdle()
        {
            _walking = false;
            _scriptedWalk = false;
            _speedMultiplier = 1f;
            _idleTimer = _leader != null
                ? Mathf.Lerp(FollowIdleMin, FollowIdleMax, (float)_rng.NextDouble())
                : Mathf.Lerp(MinIdleSeconds, MaxIdleSeconds, (float)_rng.NextDouble());
            Play(_idle);
        }

        /// <summary>
        /// leader 옆(offset)을 따라 걷기 시작한다. 하던 걸음(문에서 나오기 등)은 마치고 나서 따라간다.
        /// 리더가 꺼지거나 사라지거나 건물로 들어가면 스스로 그만둔다.
        /// </summary>
        public void Follow(VillageWanderer leader, Vector2 offset)
        {
            if (leader == null || leader == this) return;
            _leader = leader;
            _followOffset = offset;
            if (!_walking && !_entering) _idleTimer = Mathf.Min(_idleTimer, FollowIdleMin);
        }

        public void StopFollowing()
        {
            _leader = null;
            _speedMultiplier = 1f;
        }

        private bool LeaderLost => _leader == null || !_leader.isActiveAndEnabled || _leader.IsEntering;

        /// <summary>리더 옆 자리까지 길을 찾아 걷는다. 이미 가까우면 잠깐 쉰다.</summary>
        private void StartFollowWalk()
        {
            Vector2 from = _grid.NearestWalkable(transform.position);
            Vector2 leaderPos = _leader.transform.position;
            Vector2 target = _grid.NearestWalkable(leaderPos + _followOffset);
            _leaderPosAtPath = leaderPos;
            _repathTimer = FollowRepathInterval;
            float distance = Vector2.Distance(from, target);
            if (distance < FollowCloseEnough || !_grid.TryFindPath(from, target, _path) || _path.Count == 0)
            {
                if (_walking) StartIdle();
                else _idleTimer = Mathf.Lerp(FollowIdleMin, FollowIdleMax, (float)_rng.NextDouble());
                return;
            }
            transform.position = new Vector3(from.x, from.y, transform.position.z);
            _speedMultiplier = distance > FollowCatchUpDistance ? FollowCatchUpSpeed : 1f;
            _pathIndex = 0;
            FaceTowards(_path[0]);
            _walking = true;
            Play(_walk);
        }

        /// <summary>랜덤 방향·거리의 목표 중 건물·장식을 돌아서 갈 수 있고 너무 빙 돌지 않는 곳을 골라 걷기 시작한다.</summary>
        private void StartWalk()
        {
            if (_leader != null)
            {
                StartFollowWalk();
                return;
            }
            Vector2 from = _grid.NearestWalkable(transform.position); // 막힌 칸 안에 있으면 먼저 빠져나온다
            transform.position = new Vector3(from.x, from.y, transform.position.z);

            for (int attempt = 0; attempt < WalkAttempts; attempt++)
            {
                float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Lerp(MinWalkDistance, MaxWalkDistance, (float)_rng.NextDouble());
                Vector2 target = from + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                if (!_grid.IsWalkable(target) || !_grid.TryFindPath(from, target, _path)) continue;
                if (PathLength(from) > distance * MaxDetourRatio) continue;

                _pathIndex = 0;
                FaceTowards(_path[0]);
                _walking = true;
                Play(_walk);
                return;
            }
            StartIdle(); // 갈 만한 곳이 없으면 조금 더 쉰다
        }

        /// <summary>
        /// 건물 문(door)에서 나와 바깥 지점(outside)까지 곧장 걸어 나온다. 문간은 막힌 칸이라 길찾기 없이 경로를 직접 넣는다.
        /// 도착하면 평소처럼 쉬었다가 돌아다닌다.
        /// </summary>
        public void ExitBuilding(Vector2 door, Vector2 outside)
        {
            _entering = false; // 들어가다 밤이 되어 꺼졌던 경우의 상태를 지운다
            _onEntered = null;
            transform.position = new Vector3(door.x, door.y, transform.position.z);
            _path.Clear();
            _path.Add(outside);
            _pathIndex = 0;
            FaceTowards(outside);
            _walking = true;
            _scriptedWalk = true;
            Play(_walk);
        }

        /// <summary>
        /// 시간이 건너뛰었을 때(진행) 다른 자리로 옮겨 놓는다. 하던 이동·들어가기를 지우고 무작위 방향으로 쉬는 것부터 다시 시작한다.
        /// </summary>
        public void PlaceAt(Vector2 position)
        {
            StopFollowing();
            _entering = false;
            _onEntered = null;
            _path.Clear();
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            float facing = _rng.NextDouble() < 0.5 ? 1f : -1f;
            _body.localScale = new Vector3(facing * CharacterScale, CharacterScale, CharacterScale);
            _current = null; // 같은 idle이어도 0프레임부터 다시
            StartIdle();
        }

        /// <summary>
        /// 건물 문 앞 바깥 지점(outside)까지 길을 찾아 걸어간 뒤 문(door)으로 들어간다. 문에 닿으면 onEntered를 부른다
        /// (부른 쪽이 캐릭터를 끈다). 길이 없으면 바로 onEntered.
        /// </summary>
        public void ReturnInto(Vector2 outside, Vector2 door, System.Action onEntered) =>
            WalkThenCall(outside, new[] { door }, onEntered);

        /// <summary>
        /// target(예: 마을 출구)까지 길을 찾아 걸어간 뒤 onArrived를 부른다(부른 쪽이 캐릭터를 지우거나 끈다).
        /// 막힌 칸(여관 문간 등)에 서 있으면 가장 가까운 바깥 칸으로 먼저 걸어 나온다. 걷는 동안은 마우스로 고를 수 없다.
        /// extraSteps가 있으면 target 다음에 길찾기 없이 그 지점들을 차례로 곧장 걷는다(성문을 지나 성벽 밖 등 걷기 격자 밖).
        /// </summary>
        public void WalkAndVanish(Vector2 target, System.Action onArrived, IList<Vector2> extraSteps = null) =>
            WalkThenCall(target, extraSteps, onArrived);

        private void WalkThenCall(Vector2 target, IList<Vector2> extraSteps, System.Action onArrived)
        {
            StopFollowing();
            Vector2 here = transform.position;
            Vector2 start = _grid.NearestWalkable(here);
            if (!_grid.TryFindPath(start, target, _path))
            {
                onArrived?.Invoke();
                return;
            }
            if (start != here) _path.Insert(0, start);
            if (extraSteps != null) _path.AddRange(extraSteps);
            _pathIndex = 0;
            _entering = true;
            _onEntered = onArrived;
            FaceTowards(_path[0]);
            _walking = true;
            Play(_walk);
        }

        /// <summary>from(예: 마을 출구)에 나타나 to까지 걸어 들어온 뒤 평소처럼 돌아다닌다.</summary>
        public void WalkInFrom(Vector2 from, Vector2 to) => WalkInFrom(new[] { from }, to);

        /// <summary>
        /// approach[0](예: 성벽 밖)에 나타나 나머지 지점을 길찾기 없이 곧장 걸어 들어온 뒤(성문 → 마을 출구),
        /// 마지막 지점에서 to까지 길을 찾아 걷고 평소처럼 돌아다닌다.
        /// </summary>
        public void WalkInFrom(IList<Vector2> approach, Vector2 to)
        {
            _entering = false;
            _onEntered = null;
            Vector2 from = approach[0], entry = approach[approach.Count - 1];
            transform.position = new Vector3(from.x, from.y, transform.position.z);
            if (!_grid.TryFindPath(_grid.NearestWalkable(entry), to, _path))
            {
                _path.Clear();
                _path.Add(to);
            }
            for (int i = approach.Count - 1; i >= 1; i--) _path.Insert(0, approach[i]);
            _pathIndex = 0;
            FaceTowards(_path[0]);
            _walking = true;
            _scriptedWalk = true;
            Play(_walk);
        }

        private float PathLength(Vector2 from)
        {
            float length = 0f;
            foreach (var point in _path)
            {
                length += Vector2.Distance(from, point);
                from = point;
            }
            return length;
        }

        private void FinishEntering()
        {
            _entering = false;
            _walking = false;
            var callback = _onEntered;
            _onEntered = null;
            callback?.Invoke();
        }

        private void FaceTowards(Vector2 point)
        {
            float dx = point.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.01f) return; // 거의 위아래로만 갈 땐 방향 유지
            float facing = (dx > 0f) == SheetFacesRight ? 1f : -1f;
            _body.localScale = new Vector3(facing * CharacterScale, CharacterScale, CharacterScale);
        }

        private void Update()
        {
            if (_current == null) return;

            if (_leader != null && LeaderLost) StopFollowing();
            if (_walking && _leader != null && !_entering && !_scriptedWalk)
            {
                _repathTimer -= Time.deltaTime;
                if (_repathTimer <= 0f)
                {
                    _repathTimer = FollowRepathInterval;
                    if (Vector2.Distance(_leader.transform.position, _leaderPosAtPath) > FollowRepathMove) StartFollowWalk();
                }
            }

            if (_walking)
            {
                Vector2 waypoint = _path[_pathIndex];
                Vector2 next = Vector2.MoveTowards(transform.position, waypoint, WalkSpeed * _speedMultiplier * Time.deltaTime);
                transform.position = new Vector3(next.x, next.y, transform.position.z);
                if (next == waypoint)
                {
                    _pathIndex++;
                    if (_pathIndex < _path.Count) FaceTowards(_path[_pathIndex]);
                    else if (_entering) FinishEntering();
                    else StartIdle();
                }
            }
            else
            {
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f) StartWalk();
            }

            if (_current.FrameCount > 0)
            {
                _frameTime += Time.deltaTime;
                ShowFrame(CurrentFrame());
            }

            if (IsHighlighted)
            {
                float brightness = Mathf.Lerp(0.75f, 1f, (Mathf.Sin(Time.unscaledTime * OutlinePulseSpeed) + 1f) * 0.5f);
                var color = new Color(1f, 1f, 1f, brightness);
                foreach (var copies in _outlines.Values)
                    foreach (var copy in copies) copy.color = color;
            }
        }
    }
}
