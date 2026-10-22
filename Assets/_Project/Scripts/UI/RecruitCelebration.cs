using System.Collections.Generic;
using GN3.Mercenaries;
using GN3.Traits;
using GN3.World;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 계약이 성사된 순간의 축하 카드: "영입 완료!"(재계약은 "재계약 완료!") + 전신 초상·이름·계약 조건.
    /// 카드가 튀어나오고, 붉은 "계약" 도장이 찍히며 금빛 반짝이가 퍼지고, 뒤에서 금빛 빛줄기가 돈다. 짧은 팡파르.
    /// 클릭·ESC·4초 뒤 자동으로 닫힌다. 게임이 멈춰 있어도 돌게 unscaled 시간을 쓴다.
    /// 정렬 5: HUD(3)·중요 알림(4) 위, 일시정지 메뉴(6) 아래(타이틀·튜토리얼과 같지만 동시에 뜨지 않는다). ContractNegotiationPanel.Sign이 띄운다.
    /// 소리: Resources/Sfx/영입 파일이 있으면 그것, 없으면 코드로 만든 도·미·솔·도 아르페지오.
    /// </summary>
    public class RecruitCelebration : MonoBehaviour
    {
        private const string SoundName = "영입";
        private const float CardWidth = 560f, CardHeight = 400f;
        private const float PopSeconds = 0.35f, StampAt = 0.35f, StampSeconds = 0.18f;
        private const float AutoCloseSeconds = 4f, FadeSeconds = 0.35f;
        private const int SparkCount = 16;

        private static RecruitCelebration _open;
        private static AudioClip _fanfare;
        private static Texture2D _rays;

        private CanvasGroup _group;
        private RectTransform _card, _rayRect, _stamp;
        private readonly List<(RectTransform rect, RawImage image, Vector2 velocity)> _sparks = new List<(RectTransform, RawImage, Vector2)>();
        private float _time;
        private bool _stamped, _closing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _open = null;

        public static void Show(Mercenary merc, ContractOffer offer, bool renewal)
        {
            if (merc == null) return;
            if (_open != null) Destroy(_open.gameObject);
            var go = new GameObject(nameof(RecruitCelebration), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(RecruitCelebration));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // 중요 알림(4) 위: 방금 맺은 계약을 대기 중인 알림이 가리지 않게
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _open = go.GetComponent<RecruitCelebration>();
            _open.Build(merc, offer, renewal);
            EscapeCloser.Register(go, _open.Close, WindowRole.Popup);
            PlaySound();
        }

        private static void PlaySound()
        {
            if (GameAudio.HasSfx(SoundName)) GameAudio.PlaySfx(SoundName);
            else GameAudio.PlayClip(Fanfare, 0.8f);
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            _time = Mathf.Max(_time, AutoCloseSeconds); // 바로 흐려지기 시작
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;

            // 카드: 0.6 → 1.08 → 1
            float k = Mathf.Clamp01(_time / PopSeconds);
            float scale = k < 0.7f ? Mathf.Lerp(0.6f, 1.08f, k / 0.7f) : Mathf.Lerp(1.08f, 1f, (k - 0.7f) / 0.3f);
            _card.localScale = Vector3.one * scale;
            _rayRect.localRotation = Quaternion.Euler(0f, 0f, -_time * 18f);
            _rayRect.localScale = Vector3.one * (0.9f + 0.05f * Mathf.Sin(_time * 3f));

            // 도장: 크게 나타나 찍히듯 줄어든다 → 그 순간 반짝이
            if (_time >= StampAt)
            {
                float s = Mathf.Clamp01((_time - StampAt) / StampSeconds);
                _stamp.gameObject.SetActive(true);
                _stamp.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, s * s);
                _stamp.GetComponent<CanvasGroup>().alpha = Mathf.Lerp(0f, 1f, s * 3f);
                if (s >= 1f && !_stamped)
                {
                    _stamped = true;
                    BurstSparks();
                }
            }
            for (int i = 0; i < _sparks.Count; i++)
            {
                var (rect, image, velocity) = _sparks[i];
                rect.anchoredPosition += velocity * dt;
                _sparks[i] = (rect, image, velocity * (1f - 2.2f * dt));
                var c = image.color;
                c.a = Mathf.Max(0f, c.a - dt * 1.1f);
                image.color = c;
            }

            // 자동으로 닫힘(또는 클릭·ESC 뒤): 흐려지며 사라진다
            if (_time >= AutoCloseSeconds)
            {
                _group.alpha = 1f - Mathf.Clamp01((_time - AutoCloseSeconds) / FadeSeconds);
                _group.blocksRaycasts = false;
                if (_group.alpha <= 0f) Destroy(gameObject);
            }
        }

        private void BurstSparks()
        {
            for (int i = 0; i < SparkCount; i++)
            {
                var go = new GameObject("Spark", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(_card.parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchoredPosition = _stamp.anchoredPosition + _card.anchoredPosition;
                float size = Random.Range(18f, 34f);
                rect.sizeDelta = new Vector2(size, size);
                var image = go.GetComponent<RawImage>();
                image.texture = GN3.Battle.BattleDecor.Glow;
                image.color = new Color(1f, Random.Range(0.75f, 0.92f), Random.Range(0.35f, 0.6f), 1f);
                image.raycastTarget = false;
                float angle = (i + Random.value * 0.5f) / SparkCount * Mathf.PI * 2f;
                _sparks.Add((rect, image, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(260f, 420f)));
            }
        }

        // ---------- 화면 ----------

        private void Build(Mercenary merc, ContractOffer offer, bool renewal)
        {
            _group = GetComponent<CanvasGroup>();

            // 어두운 막: 누르면 닫힌다
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(transform, false);
            var dimRect = (RectTransform)dim.transform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var dimButton = dim.GetComponent<Button>();
            dimButton.transition = Selectable.Transition.None; // 버튼 소리 없이
            dimButton.onClick.AddListener(Close);

            // 뒤에서 도는 금빛 빛줄기
            var rays = new GameObject("Rays", typeof(RectTransform), typeof(RawImage));
            rays.transform.SetParent(transform, false);
            _rayRect = (RectTransform)rays.transform;
            _rayRect.sizeDelta = new Vector2(1100f, 1100f);
            var rayImage = rays.GetComponent<RawImage>();
            rayImage.texture = Rays;
            rayImage.color = new Color(1f, 0.82f, 0.4f, 0.55f);
            rayImage.raycastTarget = false;

            // 카드(누르면 닫힌다 — 판 전체가 클릭 영역)
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(transform, false);
            _card = (RectTransform)card.transform;
            _card.sizeDelta = new Vector2(CardWidth, CardHeight);
            var cardImage = card.GetComponent<Image>();
            cardImage.sprite = UITheme.Panel;
            cardImage.type = Image.Type.Sliced;
            var cardButton = card.GetComponent<Button>();
            cardButton.transition = Selectable.Transition.None;
            cardButton.onClick.AddListener(Close);
            var c = _card.transform;

            var title = Label(c, renewal ? "재계약 완료!" : "영입 완료!", 44, new Color(1f, 0.82f, 0.35f), TextAnchor.MiddleCenter,
                new Vector2(0f, -22f), new Vector2(CardWidth - 40f, 60f), center: true);
            title.font = UITheme.BoldFont;
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            var portrait = CharacterPortraitUI.Create(c, merc.Appearance, 170, PortraitCrop.FullBody);
            var portraitRect = (RectTransform)portrait.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0f, 1f);
            portraitRect.anchoredPosition = new Vector2(34f, -96f);
            portraitRect.sizeDelta = new Vector2(170f, 170f);

            float x = 230f, w = CardWidth - x - 30f;
            var name = Label(c, $"{GradeTable.RichLabel(merc.Grade)} {merc.Name}", 30, UITheme.BodyText, TextAnchor.MiddleLeft, new Vector2(x, -104f), new Vector2(w, 40f));
            name.font = UITheme.BoldFont;
            name.supportRichText = true;
            Label(c, $"{merc.Class.ClassName} · {merc.Age}세 · 전투력 {merc.CombatPower}", UITheme.BodySize, UITheme.MutedText, TextAnchor.MiddleLeft, new Vector2(x, -150f), new Vector2(w, 26f));
            Label(c, $"성격: {PersonalityTable.Get(merc.Personality).Label}", UITheme.BodySize, UITheme.MutedText, TextAnchor.MiddleLeft, new Vector2(x, -178f), new Vector2(w, 26f));

            // 계약 조건
            string terms = $"주급 {offer.WeeklyWage}G · {ContractTuning.LengthLabel(offer.LengthDays)}({GameCalendar.Format(GameClock.CurrentDay + offer.LengthDays)}까지)";
            string extras = (offer.SigningFee > 0 ? $"계약금 {offer.SigningFee}G" : "") +
                            (offer.WinBonus > 0 ? (offer.SigningFee > 0 ? " · " : "") + $"성공 보너스 {offer.WinBonus}G" : "");
            Label(c, terms + (extras.Length > 0 ? "\n" + extras : ""), UITheme.BodySize, UITheme.BodyText, TextAnchor.UpperLeft, new Vector2(34f, -290f), new Vector2(CardWidth - 68f, 52f));

            Label(c, "클릭하면 닫힘", UITheme.SmallSize, UITheme.MutedText, TextAnchor.MiddleCenter, new Vector2(0f, -CardHeight + 34f), new Vector2(CardWidth, 22f), center: true);

            // "계약" 도장(붉은 원) — 오른쪽 아래 비스듬히
            var stamp = new GameObject("Stamp", typeof(RectTransform), typeof(RawImage), typeof(CanvasGroup));
            stamp.transform.SetParent(c, false);
            _stamp = (RectTransform)stamp.transform;
            _stamp.anchorMin = _stamp.anchorMax = new Vector2(0.5f, 0.5f);
            _stamp.anchoredPosition = new Vector2(CardWidth * 0.5f - 92f, -CardHeight * 0.5f + 100f);
            _stamp.sizeDelta = new Vector2(120f, 120f);
            _stamp.localRotation = Quaternion.Euler(0f, 0f, 14f);
            var stampImage = stamp.GetComponent<RawImage>();
            stampImage.texture = StampRing;
            stampImage.color = new Color(0.85f, 0.18f, 0.15f, 0.9f);
            stampImage.raycastTarget = false;
            var stampText = Label(_stamp, renewal ? "재계약" : "계약", renewal ? 26 : 34, new Color(0.85f, 0.18f, 0.15f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(120f, 120f), center: true);
            stampText.font = UITheme.BoldFont;
            var stampTextRect = stampText.rectTransform;
            stampTextRect.anchorMin = stampTextRect.anchorMax = stampTextRect.pivot = new Vector2(0.5f, 0.5f);
            stampTextRect.anchoredPosition = Vector2.zero;
            // 테마(UIThemeApplier)는 큰 글씨를 제목 주황으로 바꾸므로, 먼저 입힌 뒤(한 번만 입힌다) 이 카드의 색을 되돌린다.
            UIThemeApplier.ApplyNow(transform);
            title.color = new Color(1f, 0.82f, 0.35f);
            name.color = UITheme.BodyText;
            stampText.color = new Color(0.85f, 0.18f, 0.15f, 0.95f);
            stamp.SetActive(false);

            _card.localScale = Vector3.one * 0.6f;
        }

        private static Text Label(Transform parent, string content, int size, Color color, TextAnchor anchor, Vector2 topLeft, Vector2 box, bool center = false)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = center ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = box;
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = UITheme.Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        // ---------- 코드로 만드는 그림·소리 ----------

        /// <summary>가운데에서 뻗는 빛줄기 12개(가장자리로 갈수록 흐려짐).</summary>
        private static Texture2D Rays
        {
            get
            {
                if (_rays != null) return _rays;
                const int size = 256;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float beam = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 6f)), 6f);
                    float fade = Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, beam * fade);
                }
                _rays = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                _rays.SetPixels(pixels);
                _rays.Apply();
                return _rays;
            }
        }

        private static Texture2D _stampRing;

        /// <summary>도장 테두리: 굵은 원 두 겹(흰색, RawImage 색으로 칠한다).</summary>
        private static Texture2D StampRing
        {
            get
            {
                if (_stampRing != null) return _stampRing;
                const int size = 128;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool ring = (r > 0.86f && r < 0.98f) || (r > 0.74f && r < 0.79f);
                    pixels[y * size + x] = ring ? Color.white : Color.clear;
                }
                _stampRing = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                _stampRing.SetPixels(pixels);
                _stampRing.Apply();
                return _stampRing;
            }
        }

        /// <summary>도·미·솔·(높은)도 아르페지오에 마지막 화음이 울리는 짧은 팡파르(감쇠 사인파 + 배음 조금).</summary>
        private static AudioClip Fanfare
        {
            get
            {
                if (_fanfare != null) return _fanfare;
                const int rate = 44100;
                const float length = 1.1f;
                int samples = Mathf.CeilToInt(rate * length);
                var data = new float[samples];
                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                float[] starts = { 0f, 0.09f, 0.18f, 0.27f };
                for (int n = 0; n < notes.Length; n++)
                {
                    bool last = n == notes.Length - 1;
                    float decay = last ? 2.6f : 7f;
                    int from = Mathf.RoundToInt(starts[n] * rate);
                    for (int i = from; i < samples; i++)
                    {
                        float t = (i - from) / (float)rate;
                        float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(t * 200f);
                        float w = Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t);
                        data[i] += w * env * 0.22f;
                        // 마지막 음엔 아래 화음(도·미·솔)을 함께 울린다
                        if (last)
                            for (int h = 0; h < 3; h++)
                                data[i] += Mathf.Sin(2f * Mathf.PI * notes[h] * t) * env * 0.1f;
                    }
                }
                for (int i = 0; i < samples; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
                _fanfare = AudioClip.Create("RecruitFanfare", samples, 1, rate, false);
                _fanfare.SetData(data, 0);
                return _fanfare;
            }
        }
    }
}
