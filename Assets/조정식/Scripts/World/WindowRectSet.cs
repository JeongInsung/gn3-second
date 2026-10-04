using System.Collections.Generic;
using UnityEngine;

namespace GN3.World
{
    /// <summary>
    /// 건물 그림별 창문 위치. 창 유리를 색만으로 찾으면 파란 슬레이트 지붕까지 잡혀서, 창이 있는 사각형은 사람이 정하고
    /// 그 안의 유리 픽셀만 WindowGlowBuilder가 자동으로 고른다. 좌표는 스프라이트 기준 0~1(왼쪽 아래가 0)이라
    /// 다시 구워 픽셀 크기가 바뀌어도 그대로 맞는다.
    /// </summary>
    [CreateAssetMenu(menuName = "GN3/Window Rect Set", fileName = "WindowRects")]
    public class WindowRectSet : ScriptableObject
    {
        [System.Serializable]
        public class Building
        {
            [Tooltip("Buildings 폴더 원본 PNG 이름(확장자 제외)")]
            public string sourceName;
            [Tooltip("창 사각형(0~1, 왼쪽 아래 기준). 판자로 막힌 창은 넣지 않는다.")]
            public List<Rect> windows = new List<Rect>();
            [Tooltip("windows 중 화덕 불꽃 사각형의 번호(0부터). 창 불빛 대신 ForgeFire(일렁이는 불꽃·깜빡이는 조명·불티)가 맡는다.")]
            public List<int> forgeIndices = new List<int>();
        }

        public List<Building> buildings = new List<Building>();

        public Building Find(string sourceName) => buildings.Find(b => b.sourceName == sourceName);
    }
}
