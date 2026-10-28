using GN3.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GN3.UI
{
    /// <summary>
    /// 마을 단축키: M 용병시장 · T 파티 구성 · Q 퀘스트 · L 우편함 · B 건설 · G 금고 장부 · H 의뢰 기록 · C 일정 · Y 업적 ·
    /// U 길드 · K 건강 · R 관계 · N 근처 길드 · J 도감. 열려 있으면 닫고, 아니면 연다.
    /// 배속(F)·일시정지(P)·진행(Space)은 TimeAdvanceController가 받는다. 중요 알림창(GamePause)·진행 연출 중·글자 입력 중에는 무시한다.
    /// MainMenuBootstrapper가 메뉴 줄을 만들 때 붙인다.
    /// </summary>
    public class VillageHotkeys : MonoBehaviour
    {
        private GameObject _market;
        private GameObject _party;
        private GameObject _quest;

        /// <summary>입력칸에 글자를 치는 중인가(단축키를 받지 않는다).</summary>
        public static bool IsTyping
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        public void Init(GameObject market, GameObject party, GameObject quest)
        {
            _market = market;
            _party = party;
            _quest = quest;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || GamePause.IsPaused || TimeAdvanceController.IsAdvancing || IsTyping) return;
            if (keyboard.mKey.wasPressedThisFrame) Toggle(_market);
            if (keyboard.tKey.wasPressedThisFrame) Toggle(_party);
            if (keyboard.qKey.wasPressedThisFrame) Toggle(_quest);
            if (keyboard.lKey.wasPressedThisFrame) MailboxPanel.Toggle();
            if (keyboard.bKey.wasPressedThisFrame) BuildMenuPanel.Toggle();
            if (keyboard.gKey.wasPressedThisFrame) GoldLedgerWindow.Toggle();
            if (keyboard.hKey.wasPressedThisFrame) QuestHistoryWindow.Toggle();
            if (keyboard.cKey.wasPressedThisFrame) CalendarPanel.Toggle();
            if (keyboard.yKey.wasPressedThisFrame) AchievementsPanel.Toggle();
            if (keyboard.uKey.wasPressedThisFrame && GuildPanel.Instance != null) GuildPanel.Instance.Toggle();
            if (keyboard.kKey.wasPressedThisFrame && HealthPanel.Instance != null) HealthPanel.Instance.Toggle();
            if (keyboard.rKey.wasPressedThisFrame && RelationsPanel.Instance != null) RelationsPanel.Instance.Toggle();
            if (keyboard.nKey.wasPressedThisFrame && RivalGuildsPanel.Instance != null) RivalGuildsPanel.Instance.Toggle();
            if (keyboard.jKey.wasPressedThisFrame && BestiaryPanel.Instance != null) BestiaryPanel.Instance.Toggle();
        }

        private static void Toggle(GameObject panel)
        {
            if (panel == null) return;
            if (panel.activeSelf) panel.SetActive(false);
            else PanelActivator.Open(panel);
        }
    }
}
