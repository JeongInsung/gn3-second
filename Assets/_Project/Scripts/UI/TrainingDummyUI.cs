using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 훈련소 창의 허수아비. Resources/Training/training_dummy(4열×2행, 칸 384×512, 투명 배경)를 8장으로 잘라 쓴다.
    /// 0 = 똑바로 섬, 1~2 = 맞아서 기울어짐, 3~7 = 흔들리며 돌아옴. Hit()을 받으면 1→7을 한 번 재생하고 0에서 멈춘다.
    /// 시트의 허수아비는 오른쪽으로 기울어진다 — 용병이 왼쪽에서 베어 오른쪽으로 밀려나는 모습(원본 방향 그대로 쓴다).
    /// 일시정지 중에도 움직이게 실제 시간으로 재생한다.
    /// </summary>
    public class TrainingDummyUI : MonoBehaviour
    {
        private const string SheetPath = "Training/training_dummy";
        private const int Columns = 4;
        private const int Rows = 2;
        private const float Fps = 14f;

        private static Sprite[] _frames;

        // Domain Reload가 꺼져 있어도 Play마다 비운다(Sprite.Create로 만든 스프라이트는 Play가 끝나면 파괴된다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _frames = null;

        private Image _image;
        private int _frame;
        private float _timer;
        private bool _playing;

        public static TrainingDummyUI Create(Transform parent, float height)
        {
            var go = new GameObject("TrainingDummy", typeof(RectTransform), typeof(Image), typeof(TrainingDummyUI));
            go.transform.SetParent(parent, false);
            var frames = Frames();
            var rect = (RectTransform)go.transform;
            float aspect = frames != null ? frames[0].rect.width / frames[0].rect.height : 0.75f;
            rect.sizeDelta = new Vector2(height * aspect, height);

            var dummy = go.GetComponent<TrainingDummyUI>();
            dummy._image = go.GetComponent<Image>();
            dummy._image.raycastTarget = false;
            dummy._image.preserveAspect = true;
            dummy.Show(0);
            return dummy;
        }

        /// <summary>맞았다: 기울어졌다 흔들리며 돌아오는 동작을 처음부터 재생한다.</summary>
        public void Hit()
        {
            if (Frames() == null) return;
            _playing = true;
            _timer = 0f;
            Show(1);
        }

        private void Update()
        {
            if (!_playing) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < 1f / Fps) return;
            _timer -= 1f / Fps;
            if (_frame + 1 >= Columns * Rows)
            {
                _playing = false;
                Show(0);
                return;
            }
            Show(_frame + 1);
        }

        private void Show(int frame)
        {
            _frame = frame;
            var frames = Frames();
            _image.enabled = frames != null;
            if (frames != null) _image.sprite = frames[frame];
        }

        /// <summary>시트를 8장으로 자른다(한 번만). 시트가 없으면 null.</summary>
        private static Sprite[] Frames()
        {
            if (_frames != null && _frames[0] != null) return _frames; // 파괴됐으면 다시 자른다
            var texture = Resources.Load<Texture2D>(SheetPath);
            if (texture == null) return null;
            float w = texture.width / (float)Columns;
            float h = texture.height / (float)Rows;
            _frames = new Sprite[Columns * Rows];
            for (int i = 0; i < _frames.Length; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                // 텍스처 좌표는 아래가 0이라 윗줄(0~3)이 y = h.
                var rect = new Rect(col * w, (Rows - 1 - row) * h, w, h);
                _frames[i] = Sprite.Create(texture, rect, new Vector2(0.5f, 0f));
            }
            return _frames;
        }
    }
}
