using GN3.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GN3.UI
{
    /// <summary>스페이스바로도 "하루 지나기"와 동일하게 GameClock을 1일 진행시킨다. DayControlBar에 붙는다.
    /// 프로젝트가 Player Settings에서 Input System 패키지만 쓰도록 돼 있어(레거시 UnityEngine.Input 비활성)
    /// 새 Input System API를 사용한다.</summary>
    public class DayAdvanceInput : MonoBehaviour
    {
        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                GameClock.AdvanceDay();
        }
    }
}
