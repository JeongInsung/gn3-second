using System.Collections.Generic;
using System.Linq;
using GN3.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GN3.EditorTools
{
    /// <summary>
    /// 성벽 그룹의 **가장 아래 성문**에 열림 프레임 8장(0 = 닫힘 → 7 = 활짝 열림)을 만들어 굽고
    /// CastleGate(파견대가 다가오면 열고 지나가면 닫는 성문)를 붙인다.
    /// AI 시트는 프레임마다 탑·깃발·아치까지 새로 그려 성문 전체가 꿈틀거렸다. 그래서 정지 그림(성벽_성문.png)에서
    /// 문짝 자리(아치 안 나무·쇠 부분)만 찾아, 문짝을 경첩 쪽으로 접어 가며 열린 자리는 투명하게 비운다(뒤 바닥이 보인다).
    /// 문짝 자리 밖 픽셀은 모든 프레임이 원본과 같아 문만 움직인다. 굽기는 SheetAnimationBuilder.BakeStrip.
    /// 루프 애니메이션이 아니라 CastleGate가 상황에 따라 프레임을 앞뒤로 재생한다.
    /// 성벽을 다시 둘러쳐도(WallLayoutBuilder) 구워 둔 프레임을 다시 붙이므로 이 메뉴는 성문 그림을 바꿨을 때만 누르면 된다.
    /// </summary>
    public static class GateAnimationBuilder
    {
        private const string OutputName = "성문 열림";
        private const string GatePieceName = "성벽_성문";
        private const string GateSourcePath = "Assets/조정식/성벽/성벽_성문.png";
        private const int FrameCount = 8;
        private const float OpenAngle = 85f;      // 마지막 프레임에서 문짝이 돌아간 각도(정면에서 본 폭 = cos)
        private const float MinLeafWidth = 0.12f; // 활짝 열려도 경첩 쪽에 남는 문짝 두께(원래 폭 비율)
        private const float OpenShade = 0.35f;    // 활짝 열린 문짝이 어두워지는 정도
        private const int MaxGap = 4;             // 문짝 줄을 넓혀 갈 때 건너뛰는 문 색 아닌 픽셀(쇠띠·고리 틈)

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
            var source = AssetDatabase.LoadAssetAtPath<Sprite>(GateSourcePath);
            if (source == null)
            {
                Debug.LogError($"[GateAnimation] {GateSourcePath} 스프라이트가 없습니다.");
                return;
            }

            var texture = SheetAnimationBuilder.LoadReadable(GateSourcePath);
            int width = texture.width, height = texture.height;
            var pixels = texture.GetPixels();
            Object.DestroyImmediate(texture);
            if (!FindDoor(pixels, width, height, out var door))
            {
                Debug.LogError("[GateAnimation] 성문 그림에서 문짝(아치 안 나무 부분)을 찾지 못했습니다.");
                return;
            }

            var frames = new List<Texture2D>();
            for (int i = 0; i < FrameCount; i++)
            {
                var frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
                frame.SetPixels(DrawFrame(pixels, width, door, i / (FrameCount - 1f)));
                frame.Apply();
                frames.Add(frame);
            }

            // 정지 성문(WallLayoutBuilder가 원본 스프라이트 크기로 놓음)과 같은 월드 크기·발밑 가운데 피벗 → 같은 자리에 겹친다.
            float screenPixelsPerUnit = PixelBaker.ReferenceScreenHeight / (cam.orthographicSize * 2f);
            float scale = source.bounds.size.x * screenPixelsPerUnit / width;
            int frameWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int frameHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            var sprites = SheetAnimationBuilder.BakeStrip(frames, OutputName, frameWidth, frameHeight, screenPixelsPerUnit, new Vector2(0.5f, 0f));

            Attach(gate, sprites);
            var village = group.transform.parent != null ? group.transform.parent.gameObject : null;
            if (village != null) WallLayoutBuilder.ApplyGroupToPrefab(village, group);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[GateAnimation] 문짝 x {door.Left}~{door.Right}, y {door.Bottom}~{door.Top}(원본 px) → {sprites.Length}프레임 {frameWidth}x{frameHeight}px 성문 열림을 " +
                      $"'{gate.transform.parent.name}'(발밑 {gate.transform.parent.position})에 붙였습니다.");
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

        private class Door
        {
            public int Left, Right, Bottom, Top; // 경첩 x(왼·오른), 문짝 줄 y 범위(텍스처 좌표, 아래 0)
            public int[] RowLeft, RowRight;      // 줄(y - Bottom)마다 문짝 자리 [RowLeft, RowRight] → 아치 모양
        }

        private static bool IsDoorColor(Color c)
        {
            if (c.a < 0.5f) return false;
            bool wood = c.r > c.g + 0.047f && c.g > c.b + 0.02f;
            bool iron = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.3f;
            return wood || iron;
        }

        /// <summary>
        /// 그림 가운데에서 문 색(나무·쇠)이 빽빽한 줄들을 문짝 높이로 보고, 줄마다 가운데에서 좌우로 문 색을 따라 넓혀 문짝 자리를 잡는다.
        /// 경첩은 아래 절반 줄들의 중앙값. 위에서 아래로 갈수록 넓어지기만 하게(아치) 다듬어 튀는 줄을 없앤다.
        /// </summary>
        private static bool FindDoor(Color[] pixels, int width, int height, out Door door)
        {
            door = null;
            int center = width / 2;
            float Density(int y)
            {
                int count = 0;
                for (int x = center - 30; x <= center + 30; x++)
                    if (IsDoorColor(pixels[y * width + x])) count++;
                return count / 61f;
            }
            int Scan(int y, int step)
            {
                int last = center, gap = 0;
                for (int x = center; x >= 0 && x < width; x += step)
                {
                    if (IsDoorColor(pixels[y * width + x])) { last = x; gap = 0; }
                    else if (++gap > MaxGap) break;
                }
                return last;
            }

            int bottom = -1;
            for (int y = 0; y < height; y++)
                if (Density(y) >= 0.7f) { bottom = y; break; }
            if (bottom < 0) return false;
            // 쇠띠 줄은 문 색이 듬성하므로 위로 6줄 안에 빽빽한 줄이 있으면 계속 문짝으로 본다.
            int top = bottom;
            while (top + 1 < height && Enumerable.Range(1, 6).Any(j => top + j < height && Density(top + j) >= 0.5f)) top++;
            while (top > bottom && Density(top) < 0.5f) top--;
            if (top - bottom < 10) return false;

            int rows = top - bottom + 1;
            var left = new int[rows];
            var right = new int[rows];
            for (int i = 0; i < rows; i++)
            {
                left[i] = Scan(bottom + i, -1);
                right[i] = Scan(bottom + i, 1);
            }
            int half = rows / 2;
            int hingeLeft = left.Take(half).OrderBy(v => v).ElementAt(half / 2);
            int hingeRight = right.Take(half).OrderBy(v => v).ElementAt(half / 2);
            for (int i = rows - 1; i >= 0; i--)
            {
                left[i] = Mathf.Max(hingeLeft, left[i]);
                right[i] = Mathf.Min(hingeRight, right[i]);
                if (i < rows - 1)
                {
                    left[i] = Mathf.Min(left[i], left[i + 1]);
                    right[i] = Mathf.Max(right[i], right[i + 1]);
                }
            }
            door = new Door { Left = hingeLeft, Right = hingeRight, Bottom = bottom, Top = top, RowLeft = left, RowRight = right };
            return true;
        }

        /// <summary>open(0 닫힘 ~ 1 활짝)만큼 문짝을 경첩 쪽으로 접은 프레임. 열린 자리는 투명, 문짝 자리 밖은 원본 그대로.</summary>
        private static Color[] DrawFrame(Color[] source, int width, Door door, float open)
        {
            var frame = (Color[])source.Clone();
            if (open <= 0f) return frame;
            float leaf = Mathf.Max(MinLeafWidth, Mathf.Cos(open * OpenAngle * Mathf.Deg2Rad));
            float shade = 1f - OpenShade * open;
            float middle = (door.Left + door.Right) * 0.5f;
            int rows = door.Top - door.Bottom + 1;
            for (int i = 0; i < rows; i++)
            {
                int y = door.Bottom + i, rowLeft = door.RowLeft[i], rowRight = door.RowRight[i];
                for (int x = rowLeft; x <= rowRight; x++)
                {
                    // 접힌 문짝 위 이 픽셀이 원래 문짝의 어느 열이었는지(경첩에서 거리 ÷ 폭 비율)
                    bool leftSide = x < middle;
                    float sourceX = leftSide ? door.Left + (x - door.Left) / leaf : door.Right - (door.Right - x) / leaf;
                    bool onLeaf = leftSide ? sourceX < middle : sourceX >= middle;
                    int sx = Mathf.FloorToInt(sourceX);
                    if (onLeaf && sx >= rowLeft && sx <= rowRight)
                    {
                        var c = source[y * width + sx];
                        frame[y * width + x] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                    }
                    else frame[y * width + x] = Color.clear; // 열린 자리는 비워 뒤 바닥이 보이게
                }
            }
            return frame;
        }
    }
}
