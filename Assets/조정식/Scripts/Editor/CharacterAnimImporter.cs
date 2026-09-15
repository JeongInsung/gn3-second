using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace GN3.EditorTools
{
    /// <summary>
    /// Assets/조정식/Resources/CharacterAnim 아래에 놓인 파츠 스프라이트 시트(256x256 프레임, 가로 1행)를
    /// 자동으로 Sprite(Multiple)로 잘라 임포트하고, 캐릭터 폴더 목록(characters.txt)을 갱신한다.
    ///
    /// 사용법: pixel-anim-tool "💾 저장" 결과 폴더(예: 귀족옷/parts_idle_sheets/…)를 그대로
    /// Assets/조정식/Resources/CharacterAnim/ 에 복사하면 끝. parts_{clip}_sheets 폴더는 {clip}으로 자동 정리되고
    /// 시장/DesignScene에 바로 등장한다. 여러 개를 한 번에 넣을 땐 메뉴 GN3/CharacterAnim/저장 폴더에서 캐릭터 가져오기.
    /// </summary>
    public class CharacterAnimImporter : AssetPostprocessor
    {
        private const string RootFolder = "Assets/조정식/Resources/CharacterAnim";
        private const string CatalogPath = RootFolder + "/characters.txt";
        private const string LastImportDirPrefKey = "GN3.CharacterAnim.LastImportDir";
        private const int FrameSize = 256;
        private const int PixelsPerUnit = 64;

        // 툴이 내보내는 클립 폴더명 (parts_idle_sheets → idle)
        private static readonly Regex ToolClipFolderPattern = new Regex(@"^parts_(.+)_sheets$");

        private static bool _normalizeScheduled;

        private static bool IsCharacterAnimAsset(string path) =>
            path.Replace('\\', '/').StartsWith(RootFolder + "/");

        private void OnPreprocessTexture()
        {
            if (!IsCharacterAnimAsset(assetPath) || !assetPath.EndsWith(".png")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096; // 12프레임 시트 = 3072px 폭

            if (!TryReadPngSize(assetPath, out int width, out int height))
            {
                Debug.LogWarning($"[CharacterAnimImporter] PNG 크기를 읽지 못해 슬라이스를 건너뜀: {assetPath}");
                return;
            }

            int cols = Mathf.Max(1, width / FrameSize);
            int rows = Mathf.Max(1, height / FrameSize);
            string baseName = Path.GetFileNameWithoutExtension(assetPath);

            var rects = new List<SpriteRect>(cols * rows);
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    // 텍스처 좌표는 좌하단 기준이라 위쪽 행부터 순서를 매기려면 y를 뒤집는다.
                    rects.Add(new SpriteRect
                    {
                        name = $"{baseName}_{index:00}",
                        spriteID = GUID.Generate(),
                        rect = new Rect(col * FrameSize, height - (row + 1) * FrameSize, FrameSize, FrameSize),
                        alignment = SpriteAlignment.BottomCenter,
                        pivot = new Vector2(0.5f, 0f),
                    });
                    index++;
                }
            }

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects.ToArray());
            provider.Apply();
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(IsCharacterAnimAsset);
            if (!touched || _normalizeScheduled) return;

            // 임포트 도중에는 MoveAsset을 호출할 수 없으므로 한 프레임 뒤에 정리한다.
            _normalizeScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _normalizeScheduled = false;
                NormalizeDroppedFolders();
                RegenerateCatalog();
            };
        }

        /// <summary>툴 저장 형식 그대로 드롭된 parts_{clip}_sheets 폴더를 {clip}으로 rename 한다.</summary>
        private static void NormalizeDroppedFolders()
        {
            if (!AssetDatabase.IsValidFolder(RootFolder)) return;

            bool movedAny = false;
            foreach (var characterFolder in AssetDatabase.GetSubFolders(RootFolder))
            {
                foreach (var clipFolder in AssetDatabase.GetSubFolders(characterFolder))
                {
                    string folderName = Path.GetFileName(clipFolder);
                    var match = ToolClipFolderPattern.Match(folderName);
                    if (!match.Success) continue;

                    string target = $"{characterFolder}/{match.Groups[1].Value}";
                    if (AssetDatabase.IsValidFolder(target))
                    {
                        Debug.LogWarning($"[CharacterAnimImporter] {target} 가 이미 있어 {clipFolder} 를 정리하지 못했습니다. 둘 중 하나를 지워주세요.");
                        continue;
                    }

                    string error = AssetDatabase.MoveAsset(clipFolder, target);
                    if (string.IsNullOrEmpty(error))
                    {
                        movedAny = true;
                        Debug.Log($"[CharacterAnimImporter] 폴더 정규화: {clipFolder} → {target}");
                    }
                    else
                    {
                        Debug.LogWarning($"[CharacterAnimImporter] 폴더 이동 실패 {clipFolder}: {error}");
                    }
                }
            }

            if (movedAny) AssetDatabase.SaveAssets();
        }

        [MenuItem("GN3/CharacterAnim/캐릭터 목록(characters.txt) 재생성")]
        private static void RegenerateCatalog()
        {
            string rootAbsolute = Path.GetFullPath(RootFolder);
            if (!Directory.Exists(rootAbsolute)) return;

            var names = Directory.GetDirectories(rootAbsolute)
                .Select(Path.GetFileName)
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToList();

            string content = string.Join("\n", names) + "\n";
            string catalogAbsolute = Path.GetFullPath(CatalogPath);
            if (File.Exists(catalogAbsolute) && File.ReadAllText(catalogAbsolute) == content) return;

            File.WriteAllText(catalogAbsolute, content);
            AssetDatabase.ImportAsset(CatalogPath);
            Debug.Log($"[CharacterAnimImporter] characters.txt 갱신: {names.Count}개 ({string.Join(", ", names)})");
        }

        /// <summary>
        /// 툴 저장 폴더(예: Downloads\저장\저장)를 골라 그 안의 캐릭터 폴더를 전부 Resources/CharacterAnim 으로 복사한다.
        /// 각 캐릭터의 parts_*_sheets 안 PNG/manifest.json만 가져오고(최상위 리그 json 제외), 같은 이름은 덮어쓴다.
        /// </summary>
        [MenuItem("GN3/CharacterAnim/저장 폴더에서 캐릭터 가져오기...")]
        private static void ImportFromExternalFolder()
        {
            string lastDir = EditorPrefs.GetString(LastImportDirPrefKey, "");
            string sourceRoot = EditorUtility.OpenFolderPanel("캐릭터 저장 폴더 선택 (하위에 캐릭터 폴더들)", lastDir, "");
            if (string.IsNullOrEmpty(sourceRoot)) return;
            EditorPrefs.SetString(LastImportDirPrefKey, sourceRoot);

            string rootAbsolute = Path.GetFullPath(RootFolder);
            Directory.CreateDirectory(rootAbsolute);

            // 고른 폴더 자체가 캐릭터 폴더(parts_*_sheets를 직접 가짐)면 그 하나만, 아니면 하위 캐릭터 폴더들을 가져온다.
            var characterDirs = GetToolClipDirs(sourceRoot).Count > 0
                ? new[] { sourceRoot }
                : Directory.GetDirectories(sourceRoot);

            int characterCount = 0, fileCount = 0;
            foreach (var characterDir in characterDirs)
            {
                string characterName = Path.GetFileName(characterDir);
                var clipDirs = GetToolClipDirs(characterDir);
                if (clipDirs.Count == 0) continue; // 캐릭터 폴더가 아님(_archive 등)

                foreach (var clipDir in clipDirs)
                {
                    string clip = ToolClipFolderPattern.Match(Path.GetFileName(clipDir)).Groups[1].Value;
                    string targetDir = Path.Combine(rootAbsolute, characterName, clip);
                    Directory.CreateDirectory(targetDir);

                    foreach (var file in Directory.GetFiles(clipDir))
                    {
                        string fileName = Path.GetFileName(file);
                        if (!fileName.EndsWith(".png") && fileName != "manifest.json") continue;
                        File.Copy(file, Path.Combine(targetDir, fileName), overwrite: true);
                        fileCount++;
                    }
                }
                characterCount++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[CharacterAnimImporter] {sourceRoot} 에서 캐릭터 {characterCount}개, 파일 {fileCount}개 가져옴 → {RootFolder}");
        }

        private static List<string> GetToolClipDirs(string characterDir) =>
            Directory.GetDirectories(characterDir)
                .Where(d => ToolClipFolderPattern.IsMatch(Path.GetFileName(d)))
                .ToList();

        /// <summary>PNG IHDR 청크(오프셋 16~23)에서 폭/높이를 읽는다. 텍스처 임포트 전이라 Texture2D를 쓸 수 없다.</summary>
        private static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[24];
                if (stream.Read(header, 0, 24) < 24) return false;
                if (header[0] != 0x89 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G') return false;

                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
