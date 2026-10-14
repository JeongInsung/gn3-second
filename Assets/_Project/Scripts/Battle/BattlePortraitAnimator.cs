using System;
using System.Collections.Generic;
using GN3.CharacterAnim;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.Battle
{
    /// <summary>
    /// 전투 씬에서 파츠 조합 캐릭터를 애니메이션(대기·걷기·검격)으로 재생한다. CharacterPortraitUI와 같은
    /// 크롭(FullBody)·레이어링 방식을 쓰지만 고정된 프레임 1장이 아니라 idle/walk/slash 클립의 프레임을
    /// 실시간으로 갈아 끼운다. 클립마다 있는 파츠가 다를 수 있어서(검격만 weapon/slash_fx가 있는 식)
    /// 처음에 세 클립에 쓰일 수 있는 파츠 레이어를 전부 만들어두고, 그 클립에 없는 파츠는 매 프레임
    /// 레이어 자체를 꺼서 안 보이게 한다(Image.sprite를 null로 비우면 흰 사각형이 뜨는 Unity 기본 동작을 피함).
    /// 외형(idle로 조합)에 출처가 없는 파츠(검·검광)는 몸 캐릭터 폴더의 것을 쓴다.
    /// 배경 슬롯 이미지가 없어 CharacterPortraitUI와 달리 포트레이트 자리에 네모난 칸이 보이지 않는다.
    /// </summary>
    public class BattlePortraitAnimator : MonoBehaviour
    {
        private const float CanvasSize = 256f;
        // CharacterPortraitUI.PortraitCrop.FullBody와 같은 크롭 영역(여백 포함 전신).
        private const float CropCx = 131f, CropCy = 136f, CropSize = 136f;
        private const int DefaultFps = 6;

        private readonly Dictionary<string, Image> _layers = new Dictionary<string, Image>();
        private IReadOnlyDictionary<string, string> _partSources;
        private string _bodyCharacter;
        private string _clip = "";
        private int _frame;
        private float _timer;
        private int _fps = DefaultFps;
        private bool _loop = true;
        private bool _oneShotActive;
        private Action _onComplete;

        /// <param name="clip">true면 size 칸 밖을 잘라낸다(훈련소 창처럼 칸 안에 넣을 때). 전투 필드는 false —
        /// 검·검광이 칸 밖으로 크게 나가도 잘리지 않게.</param>
        public static GameObject Create(Transform parent, ComposedCharacter appearance, int size, bool clip = true)
        {
            var go = new GameObject("AnimatedPortrait", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            if (clip) go.AddComponent<RectMask2D>();
            go.transform.SetParent(parent, false);

            var layout = go.GetComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = size;
            layout.minHeight = layout.preferredHeight = size;
            layout.flexibleWidth = 0;

            var background = go.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f); // 배경 없음(요청: 네모난 칸 제거)
            background.raycastTarget = false;

            var animator = go.AddComponent<BattlePortraitAnimator>();
            animator._partSources = appearance.PartSources;
            animator._bodyCharacter = appearance.BodyCharacter;

            float scale = size / CropSize;
            float layerSize = CanvasSize * scale;
            var offset = new Vector2((CanvasSize / 2f - CropCx) * scale, (CropCy - CanvasSize / 2f) * scale);

            foreach (var part in PartClipManifest.DefaultZOrderBackToFront)
            {
                var layerGO = new GameObject(part, typeof(RectTransform), typeof(Image));
                layerGO.transform.SetParent(go.transform, false);
                var rect = (RectTransform)layerGO.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(layerSize, layerSize);
                rect.anchoredPosition = offset;

                var img = layerGO.GetComponent<Image>();
                img.raycastTarget = false;
                animator._layers[part] = img;
                layerGO.SetActive(false);
            }

            animator.PlayIdle();
            return go;
        }

        public void PlayIdle() => Play(CharacterAnimLibrary.DefaultClip, true);
        public void PlayWalk() => Play("walk", true);

        /// <summary>검격 1회 재생(비반복). 재생 중엔 PlayIdle/PlayWalk 요청을 무시해 끊기지 않는다.</summary>
        public void PlaySlash(Action onComplete = null) => Play(SlashClip, false, onComplete);

        /// <summary>검격을 계속 반복한다(훈련소 창의 훈련 중인 용병).</summary>
        public void PlaySlashLoop() => Play(SlashClip, true);

        private const string SlashClip = "slash";
        // 검광(slash_fx)이 크게 나오는 프레임 = 검이 맞는 순간. 지금 캐릭터 시트는 모두 12프레임 중 5번째(0부터).
        // (slash_fx 시트는 빈 프레임도 들어 있어 "레이어가 켜지는 순간"으로는 알 수 없다.)
        private const int SlashHitFrame = 5;

        /// <summary>검이 맞는 순간(검격 SlashHitFrame). 훈련소 허수아비가 받아 흔들린다.</summary>
        public event Action Hit;

        /// <summary>true면 실제 시간으로 재생한다(UI 창 — 일시정지로 timeScale이 0이어도 움직이게). 전투 씬은 false.</summary>
        public bool UseUnscaledTime { get; set; }

        private void Play(string clip, bool loop, Action onComplete = null)
        {
            if (_oneShotActive) return; // 검격 재생 중엔 대기/걷기 전환에 끼어들리지 않는다.
            if (_clip == clip && loop && _loop) return; // 이미 같은 반복 클립 중이면 그냥 둔다(매 프레임 다시 호출돼도 안 끊김).

            _clip = clip;
            _frame = 0;
            _timer = 0f;
            _loop = loop;
            _oneShotActive = !loop;
            _onComplete = onComplete;

            var manifest = CharacterAnimLibrary.LoadManifest(_bodyCharacter, clip);
            _fps = manifest != null && manifest.fps > 0 ? manifest.fps : DefaultFps;
            ApplyFrame();
        }

        private void Update()
        {
            float frameDuration = 1f / Mathf.Max(1, _fps);
            _timer += UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_timer < frameDuration) return;
            _timer -= frameDuration;
            _frame++;

            int count = FrameCount();
            if (_frame >= count)
            {
                if (_loop)
                {
                    _frame = 0;
                }
                else
                {
                    _frame = Mathf.Max(0, count - 1);
                    ApplyFrame();
                    _oneShotActive = false;
                    var callback = _onComplete;
                    _onComplete = null;
                    callback?.Invoke();
                    return;
                }
            }
            ApplyFrame();
        }

        /// <summary>몸통(torso) 프레임 수를 기준으로 삼는다 - pixel-anim-tool 산출물은 같은 클립이면 모든 파츠가 같은 프레임 수.</summary>
        private int FrameCount()
        {
            var frames = CharacterAnimLibrary.LoadFrames(_bodyCharacter, _clip, CharacterPartId.Torso);
            return frames.Length > 0 ? frames.Length : 1;
        }

        private void ApplyFrame()
        {
            foreach (var kv in _layers)
            {
                string part = kv.Key;
                var layer = kv.Value;
                Sprite sprite = null;
                // 외형은 idle로 조합돼 검격에만 있는 검·검광(weapon/slash_fx)의 출처가 없다 → 몸 캐릭터 것을 쓴다(손 위치가 맞는다).
                if (!_partSources.TryGetValue(part, out var sourceChar)) sourceChar = _bodyCharacter;
                if (!string.IsNullOrEmpty(sourceChar))
                    sprite = CharacterAnimLibrary.LoadFrame(sourceChar, _clip, part, _frame);

                if (sprite == null)
                {
                    layer.gameObject.SetActive(false);
                    continue;
                }
                layer.sprite = sprite;
                layer.gameObject.SetActive(true);
            }
            if (_outlineOn) SyncOutline();
            if (_clip == SlashClip && _frame == SlashHitFrame) Hit?.Invoke();
        }

        // ---------- 호버 테두리 ----------
        // 마을 캐릭터(VillageWanderer)와 같은 방식: 파츠마다 흰 실루엣 사본 8장을 8방향으로 조금씩 밀어 모든 파츠 뒤에
        // 깔아 바깥 테두리만 보이게 하고, 켜져 있는 동안 밝기가 숨쉬듯 오르내린다. 처음 켤 때 만든다(안 쓰는 창은 비용 없음).

        private const float OutlinePixels = 2f;     // UI 단위
        private const float OutlinePulseSpeed = 3f; // 마을 건물·캐릭터와 같은 속도
        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, 0.7071f), new Vector2(-0.7071f, -0.7071f),
        };
        private static Material _outlineMaterial;

        private GameObject _outlineRoot;
        private readonly Dictionary<string, Image[]> _outlineCopies = new Dictionary<string, Image[]>();
        private bool _outlineOn;

        /// <summary>흰 테두리를 켜고 끈다(배치 단계 마우스 호버). 매 프레임 불러도 바뀔 때만 일한다.</summary>
        public void SetOutline(bool on)
        {
            if (_outlineOn == on) return;
            if (on && _outlineRoot == null && !BuildOutline()) return;
            _outlineOn = on;
            _outlineRoot.SetActive(on);
            if (on) SyncOutline();
        }

        private bool BuildOutline()
        {
            if (_outlineMaterial == null)
            {
                var shader = Resources.Load<Shader>("UISilhouette");
                if (shader == null)
                {
                    Debug.LogWarning("[BattlePortraitAnimator] Resources/UISilhouette 셰이더를 찾지 못해 호버 테두리가 안 보입니다.");
                    return false;
                }
                _outlineMaterial = new Material(shader);
            }

            _outlineRoot = new GameObject("Outline", typeof(RectTransform));
            _outlineRoot.transform.SetParent(transform, false);
            _outlineRoot.transform.SetAsFirstSibling(); // 모든 파츠 뒤
            var rootRect = (RectTransform)_outlineRoot.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            foreach (var kv in _layers)
            {
                if (kv.Key == "slash_fx") continue; // 검광은 테두리 없이
                var source = (RectTransform)kv.Value.transform;
                var copies = new Image[OutlineDirections.Length];
                for (int i = 0; i < copies.Length; i++)
                {
                    var go = new GameObject($"{kv.Key}_outline{i}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(_outlineRoot.transform, false);
                    var rect = (RectTransform)go.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = source.sizeDelta;
                    rect.anchoredPosition = source.anchoredPosition + OutlineDirections[i] * OutlinePixels;
                    var img = go.GetComponent<Image>();
                    img.material = _outlineMaterial;
                    img.raycastTarget = false;
                    copies[i] = img;
                }
                _outlineCopies[kv.Key] = copies;
            }
            return true;
        }

        /// <summary>테두리 사본을 지금 프레임의 파츠 그림·표시 여부에 맞춘다.</summary>
        private void SyncOutline()
        {
            foreach (var kv in _outlineCopies)
            {
                var layer = _layers[kv.Key];
                bool visible = layer.gameObject.activeSelf;
                foreach (var copy in kv.Value)
                {
                    copy.gameObject.SetActive(visible);
                    if (visible) copy.sprite = layer.sprite;
                }
            }
        }

        private void LateUpdate()
        {
            if (!_outlineOn) return;
            float brightness = Mathf.Lerp(0.75f, 1f, (Mathf.Sin(Time.unscaledTime * OutlinePulseSpeed) + 1f) * 0.5f);
            var color = new Color(1f, 1f, 1f, brightness);
            foreach (var copies in _outlineCopies.Values)
                foreach (var copy in copies) copy.color = color;
        }
    }
}
