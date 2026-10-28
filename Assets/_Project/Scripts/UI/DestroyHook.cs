using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 오브젝트가 사라질 때 할 일(정적 이벤트 구독 풀기 등). 코드로 만든 HUD 글자처럼 따로 MonoBehaviour가 없는 곳에 붙인다 —
    /// 씬을 다시 열 때마다(전투 뒤·불러오기) 정적 이벤트 구독이 쌓이지 않게.
    /// </summary>
    public class DestroyHook : MonoBehaviour
    {
        private System.Action _onDestroy;

        public static void Attach(GameObject target, System.Action onDestroy)
        {
            if (target == null || onDestroy == null) return;
            var hook = target.AddComponent<DestroyHook>();
            hook._onDestroy = onDestroy;
        }

        private void OnDestroy() => _onDestroy?.Invoke();
    }
}
