using System.Linq;
using GN3.Battle;
using GN3.Quests;
using UnityEngine.SceneManagement;

namespace GN3.UI
{
    /// <summary>도착한 파견대의 자동 진행 / 직접 진행. 중요 알림창과 우편함이 같이 쓴다.</summary>
    public static class MailActions
    {
        public static bool CanFight(Expedition expedition) =>
            expedition != null && expedition.IsReady && ExpeditionLog.Instance.Active.Contains(expedition);

        /// <summary>재생 없이 바로 결과. 결과 편지(중요)가 우편함에 들어가 알림창이 이어서 띄운다.</summary>
        public static void AutoFight(Expedition expedition)
        {
            if (!CanFight(expedition)) return;
            ExpeditionBattle.Conclude(expedition, ExpeditionBattle.Simulate(expedition));
        }

        /// <summary>BattleSceneContext에 파견대를 담아 BattleScene으로. 결과는 그 씬의 결과 화면이 보여 준다.</summary>
        public static void DirectFight(Expedition expedition)
        {
            if (!CanFight(expedition)) return;
            BattleSceneContext.Pending = expedition;
            SceneManager.LoadScene("BattleScene");
        }
    }
}
