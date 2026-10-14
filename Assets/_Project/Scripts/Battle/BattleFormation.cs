using System.Collections.Generic;
using UnityEngine;

namespace GN3.Battle
{
    /// <summary>
    /// 배치 단계에서 정한 용병별 자리(필드 좌표). 다음 직접 전투에서 같은 용병이 나오면 그 자리에서 시작한다.
    /// 플레이하는 동안만 기억한다(저장 파일에는 넣지 않음). Domain Reload가 꺼져 있어 Play 시작 때 비운다.
    /// </summary>
    public static class BattleFormation
    {
        private static readonly Dictionary<string, Vector2> _positions = new Dictionary<string, Vector2>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => _positions.Clear();

        public static bool TryGet(string mercId, out Vector2 position) => _positions.TryGetValue(mercId, out position);

        public static void Remember(string mercId, Vector2 position) => _positions[mercId] = position;
    }
}
