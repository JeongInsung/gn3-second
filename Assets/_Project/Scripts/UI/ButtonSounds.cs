using System.Collections.Generic;
using GN3.Save;
using UnityEngine;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 모든 씬의 버튼에 누르는 소리를 단다. 0.25초마다 화면의 버튼을 훑어 아직 안 단 버튼의 onClick에 소리를 붙인다
    /// (UIThemeApplier처럼 인스턴스 ID로 기억 — 목록 줄처럼 수시로 다시 만드는 버튼도 곧 붙는다).
    /// onClick은 누를 수 있는 버튼에서만 불리므로 꺼진 버튼은 소리가 안 난다.
    /// 판·바깥 어두운 막처럼 보이지 않는 클릭용 버튼(transition None)은 빼서, 바깥을 눌러 닫을 때는 조용하다.
    /// 버튼마다 다른 소리는 ButtonSound를 붙여 정한다(예: 저장 버튼). 실제로 불러왔을 때(SaveSystem.OnLoaded)도 저장·불러오기 소리.
    /// </summary>
    public class ButtonSounds : MonoBehaviour
    {
        public const string ClickSound = "버튼 클릭";
        public const string SaveLoadSound = "저장 불러오기";
        private const float ScanInterval = 0.25f;

        private readonly HashSet<int> _hooked = new HashSet<int>();
        private float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRunner()
        {
            var go = new GameObject(nameof(ButtonSounds), typeof(ButtonSounds));
            DontDestroyOnLoad(go);
        }

        private void OnEnable() => SaveSystem.OnLoaded += PlayLoaded;
        private void OnDisable() => SaveSystem.OnLoaded -= PlayLoaded;

        private static void PlayLoaded() => GameAudio.PlaySfx(SaveLoadSound);

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = ScanInterval;
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!_hooked.Add(button.GetInstanceID())) continue;
                if (button.transition == Selectable.Transition.None) continue; // 보이지 않는 클릭용(판·바깥 막)
                var b = button;
                button.onClick.AddListener(() =>
                {
                    var custom = b != null ? b.GetComponent<ButtonSound>() : null;
                    string sound = custom != null ? custom.Sound : ClickSound;
                    if (!string.IsNullOrEmpty(sound)) GameAudio.PlaySfx(sound);
                });
            }
        }
    }

    /// <summary>이 버튼만 다른 소리를 낸다(ButtonSounds가 읽는다). Sound = Resources/Sfx의 이름, 빈 값이면 소리 없음.</summary>
    public class ButtonSound : MonoBehaviour
    {
        public string Sound;
    }
}
