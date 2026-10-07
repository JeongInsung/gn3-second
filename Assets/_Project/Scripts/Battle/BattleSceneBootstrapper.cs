using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.Battle
{
    /// <summary>
    /// BattleScene 전용 부트스트랩(씬 파일은 비워두고 코드로만 채우는 프로젝트 컨벤션).
    /// BattleSceneContext.Pending에 담겨온 파견대로 BattleUIController를 띄운다.
    ///
    /// 주의: [RuntimeInitializeOnLoadMethod(AfterSceneLoad)]는 "씬이 로드될 때마다"가 아니라
    /// Play(또는 빌드) 시작 직후 딱 한 번만 실행된다 - MainScene에서 Play를 시작한 뒤 런타임에
    /// SceneManager.LoadScene("BattleScene")으로 넘어오는 경우에는 다시 불리지 않는다
    /// (DesignSceneBootstrapper는 DesignScene을 직접 열어 Play하는 용도라 이 문제가 드러나지 않았음).
    /// 그래서 씬 전환 자체를 감지하는 SceneManager.sceneLoaded 이벤트를 쓴다.
    /// </summary>
    public static class BattleSceneBootstrapper
    {
        private const string TargetSceneName = "BattleScene";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Subscribe()
        {
            // Domain Reload가 꺼져 있어도(또는 켜져 있어도) 매 Play 진입마다 정확히 한 번만 구독되도록
            // 빼고 다시 더한다(이미 없으면 빼기는 아무 일도 안 함).
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != TargetSceneName) return;

            var expedition = BattleSceneContext.Pending;
            if (expedition == null)
            {
                Debug.LogWarning("[BattleSceneBootstrapper] BattleSceneContext.Pending이 비어 있어 전투를 시작할 수 없습니다. " +
                    "메인 씬에서 \"직접 진행\"으로 들어와야 합니다.");
                return;
            }

            var controller = new GameObject("BattleUIController", typeof(BattleUIController)).GetComponent<BattleUIController>();
            controller.Init(expedition);
        }
    }
}
