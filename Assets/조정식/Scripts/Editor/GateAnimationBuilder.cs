using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// "열리는 중세 성문" 8프레임 시트(4열×2행, 0 = 닫힘 → 7 = 활짝 열림)를 성벽 그룹의 **가장 아래 성문**에 맞춰 굽고
    /// CastleGate(파견대가 다가오면 열고 지나가면 닫는 성문)를 붙인다(굽기 공통 처리: SheetAnimationBuilder.BakeFrames).
    /// 루프 애니메이션이 아니라 CastleGate가 상황에 따라 프레임을 앞뒤로 재생한다.
    /// AI 시트는 프레임마다 탑·깃발까지 다시 그려 꿈틀거리므로, 문 둘레 밖은 8프레임 중앙값으로 고정하고 문 쪽만 프레임마다 바꾼다.
    /// 성벽을 다시 둘러쳐도(WallLayoutBuilder) 구워 둔 프레임을 다시 붙이므로 이 메뉴는 시트를 바꿨을 때만 누르면 된다.
    /// </summary>
    public static class GateAnimationBuilder
    {
        private const string OutputName = "성문 열림";
        private const string GatePieceName = "성벽_성문";

        // 프레임 안에서 문이 움직이는 영역(가로·세로 비율, 아래 왼쪽 0). 활짝 열린 문짝이 깃발 쪽까지 접히므로 깃발 폭까지 넉넉히.
        private const float DoorXMin = 0.24f, DoorXMax = 0.76f, DoorYMax = 0.62f;

        [MenuItem("GN3/Pixel/성문 열림 애니메이션 만들기")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            var group = WallLayoutBuilder.FindGroup(scene);
            var gate = FindBottomGate(group);
            if (gate == null)
            {
                Debug.LogError("[GateAnimation] 성벽 그룹에서 성문을 찾지 못했습니다. 먼저 'GN3/Village/성벽 둘러치기'를 실행하세요.");
                return;
            }
            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
            {
                Debug.LogError("[GateAnimation] 직교(Orthographic) Main Camera가 필요합니다.");
                return;
            }

            var sprites = SheetAnimationBuilder.BakeFrames(new SheetAnimationSettings
            {
                LogTag = "GateAnimation",
                SheetPath = "Assets/조정식/Animations/Gate/열리는 중세 성문 스프라이트.png",
                AnimationFolder = "Assets/조정식/Animations/Gate",
                OutputName = OutputName,
                Anchor = SheetAnchor.BottomCenter,
                PostProcess = LockOutsideDoor,
            }, gate, out _);

            Attach(gate, sprites);
            var village = group.transform.parent != null ? group.transform.parent.gameObject : null;
            if (village != null) WallLayoutBuilder.ApplyGroupToPrefab(village, group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var rect = sprites[0].rect;
            Debug.Log($"[GateAnimation] {sprites.Length}프레임 {rect.width}x{rect.height}px 성문 열림을 '{gate.transform.parent.name}'(발밑 {gate.transform.parent.position})에 붙였습니다.");
        }

        /// <summary>구워 둔 열림 프레임이 있으면 이 성문에 붙인다(성벽을 다시 놓을 때 WallLayoutBuilder가 부른다).</summary>
        internal static void AttachBakedFrames(SpriteRenderer gate)
        {
            var sprites = SheetAnimationBuilder.LoadBakedFrames(OutputName);
            if (gate != null && sprites != null) Attach(gate, sprites);
        }

        /// <summary>성벽 그룹에서 발밑이 가장 낮은 성문(파견대가 드나드는 아래 성문)의 그림.</summary>
        private static SpriteRenderer FindBottomGate(GameObject group)
        {
            if (group == null) return null;
            return group.GetComponentsInChildren<SpriteRenderer>(true)
                .Where(r => r.transform.parent != null && r.transform.parent.name == GatePieceName)
                .OrderBy(r => r.transform.parent.position.y)
                .FirstOrDefault();
        }

        /// <summary>
        /// 닫힌 0프레임을 놓고 CastleGate에 프레임을 넣는다. 구운 프레임은 피벗이 발밑 가운데라
        /// 그림을 정렬 부모(발밑 가운데) 자리에 그대로 두면 기존 성문과 같은 곳에 선다.
        /// </summary>
        private static void Attach(SpriteRenderer gate, Sprite[] sprites)
        {
            gate.transform.localPosition = Vector3.zero;
            var castleGate = gate.GetComponent<CastleGate>();
            if (castleGate == null) castleGate = gate.gameObject.AddComponent<CastleGate>();
            castleGate.Configure(sprites);
            // 프리팹 인스턴스에서 바꾼 값은 기록해야 오버라이드로 남아 프리팹에 Apply된다(안 하면 저장 때 원래 그림으로 돌아갔다).
            PrefabUtility.RecordPrefabInstancePropertyModifications(gate);
            PrefabUtility.RecordPrefabInstancePropertyModifications(gate.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(castleGate);
            EditorUtility.SetDirty(gate);
            EditorUtility.SetDirty(castleGate);
        }

        /// <summary>문 둘레 밖(탑·깃발 윗부분·성벽)은 8프레임 중앙값으로 고정해 문만 움직이게 한다.</summary>
        private static void LockOutsideDoor(Color[][] frames, int width, int height)
        {
            var still = SheetAnimationBuilder.Median(frames, width, height);
            int xMin = Mathf.RoundToInt(width * DoorXMin), xMax = Mathf.RoundToInt(width * DoorXMax);
            int yMax = Mathf.RoundToInt(height * DoorYMax);
            foreach (var frame in frames)
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    bool door = x >= xMin && x < xMax && y < yMax;
                    if (!door) frame[y * width + x] = still[y * width + x];
                }
        }
    }
}
