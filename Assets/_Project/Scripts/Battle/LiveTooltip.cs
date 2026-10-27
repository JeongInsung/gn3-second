using System;
using GN3.UI;
using UnityEngine;

namespace GN3.Battle
{
    /// <summary>같은 오브젝트의 툴팁(TooltipTrigger) 글을 매 프레임 Build로 다시 만든다(적 체력처럼 계속 바뀌는 값 — BattleUIController).</summary>
    [RequireComponent(typeof(TooltipTrigger))]
    public class LiveTooltip : MonoBehaviour
    {
        public Func<string> Build;
        private TooltipTrigger _tip;

        private void Awake() => _tip = GetComponent<TooltipTrigger>();

        private void Update()
        {
            if (Build != null && _tip != null) _tip.Text = Build();
        }
    }
}
