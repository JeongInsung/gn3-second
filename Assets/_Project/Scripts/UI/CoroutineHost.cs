using UnityEngine;

namespace GN3.UI
{
    /// <summary>패널이 닫혀 비활성화되어도 계속 실행돼야 하는 코루틴을 위한, 항상 활성 상태인 전역 호스트.</summary>
    public class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost _instance;

        public static CoroutineHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("CoroutineHost");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CoroutineHost>();
                }
                return _instance;
            }
        }
    }
}
