using System.Linq;
using GN3.Mercenaries;
using GN3.Quests;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GN3.UI
{
    /// <summary>
    /// 테스트용 단축키(에디터·개발 빌드에서만 MainMenuBootstrapper가 만든다).
    /// F9: 마을에 있는(파견 중 아닌) 용병 전원의 체력을 최대치의 30%씩 깎는다 — 치료 아이템·여관 회복 시험용.
    /// </summary>
    public class DebugHotkeys : MonoBehaviour
    {
        public const float DamageRatio = 0.3f;

        /// <summary>체력을 최대치의 DamageRatio만큼 깎는다. 테스트용이라 죽이지는 않는다(최소 1).</summary>
        public static int Damage(Mercenary merc)
        {
            int max = merc.CurrentStats.MaxHealth;
            int before = merc.CurrentHealth;
            merc.SetHealth(Mathf.Max(1, before - Mathf.CeilToInt(max * DamageRatio)));
            return before - merc.CurrentHealth;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f9Key.wasPressedThisFrame) return;

            var mercs = PlayerParty.Instance.Members
                .Where(m => m.IsAlive && !ExpeditionLog.Instance.IsOnExpedition(m)).ToList();
            foreach (var merc in mercs) Damage(merc);
            ToastLog.Show(mercs.Count > 0
                ? $"[테스트] {mercs.Count}명 체력 -{Mathf.RoundToInt(DamageRatio * 100)}%"
                : "[테스트] 마을에 용병이 없습니다");
        }
    }
}
