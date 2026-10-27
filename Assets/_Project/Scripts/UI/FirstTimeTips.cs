using UnityEngine;

namespace GN3.UI
{
    /// <summary>
    /// 처음 여는 창의 짧은 안내: 튜토리얼(TutorialGuide)이 다루지 않는 시스템을 그 창을 처음 열 때 한 번만 알려 준다.
    /// 우편함에 안내 편지로 남기고(다시 읽을 수 있게) 토스트로 알린다. 본 것은 PlayerPrefs에 적어 게임 저장과 상관없이 한 번만.
    /// 설정 창의 [안내 다시 보기]가 모두 지운다(ResetAll).
    /// </summary>
    public static class FirstTimeTips
    {
        private const string Prefix = "gn3.tip.";

        public const string Weapons = "weapons", Storage = "storage", Hospital = "hospital", Health = "health",
            Rivals = "rivals", Training = "training", BattleLoot = "battleLoot";

        private static readonly string[] All = { Weapons, Storage, Hospital, Health, Rivals, Training, BattleLoot };

        /// <summary>그 안내를 아직 안 봤으면 한 번 보여 준다.</summary>
        public static void Show(string key, string title, string body)
        {
            if (!Application.isPlaying || PlayerPrefs.GetInt(Prefix + key, 0) == 1) return;
            PlayerPrefs.SetInt(Prefix + key, 1);
            PlayerPrefs.Save();
            Mailbox.Post(MailKind.Notice, $"안내: {title}", body);
            ToastLog.Show($"새 안내: {title} — 우편함(L)에서 읽을 수 있습니다", false);
        }

        /// <summary>본 안내를 모두 지운다(설정 창 [안내 다시 보기]). 다음에 그 창을 열면 다시 뜬다.</summary>
        public static void ResetAll()
        {
            foreach (var key in All) PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }
    }
}
