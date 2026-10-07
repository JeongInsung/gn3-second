using GN3.Quests;

namespace GN3.Battle
{
    /// <summary>
    /// MainScene에서 BattleScene으로 넘어가기 직전에 "어느 파견대를 전투시킬지"를 담아두는 static 전달 상자.
    /// 이 프로젝트는 Enter Play Mode Options에서 Domain Reload가 꺼져 있어(GameClock/ExpeditionLog와 같은 사정),
    /// 씬을 전환해도 static 값은 그대로 유지된다 - SceneManager.LoadScene 전에 Pending만 채우면 된다.
    /// </summary>
    public static class BattleSceneContext
    {
        public static Expedition Pending;
    }
}
