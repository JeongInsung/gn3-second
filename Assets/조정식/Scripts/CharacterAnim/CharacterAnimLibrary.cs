using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.CharacterAnim
{
    /// <summary>
    /// Resources/CharacterAnim/{캐릭터}/{클립}/{파츠}.png 구조의 파츠 시트를 로드/캐싱한다.
    /// 캐릭터 목록은 Resources가 디렉터리를 나열하지 못하므로 characters.txt(에디터가 자동 생성)에서 읽는다.
    /// </summary>
    public static class CharacterAnimLibrary
    {
        public const string ResourceRoot = "CharacterAnim";
        public const string DefaultClip = "idle";

        private static string[] _characters;
        private static readonly Dictionary<string, Sprite[]> _frameCache = new Dictionary<string, Sprite[]>();
        private static readonly Dictionary<string, PartClipManifest> _manifestCache = new Dictionary<string, PartClipManifest>();

        // 이 프로젝트는 Enter Play Mode Options에서 Domain Reload가 꺼져 있어 Play를 다시 시작해도 static이 유지된다.
        // 새로 추가한 캐릭터가 목록에 반영되도록 Play 진입마다 캐시를 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _characters = null;
            _frameCache.Clear();
            _manifestCache.Clear();
        }

        public static IReadOnlyList<string> Characters
        {
            get
            {
                if (_characters == null)
                {
                    var catalog = Resources.Load<TextAsset>($"{ResourceRoot}/characters");
                    _characters = catalog == null
                        ? Array.Empty<string>()
                        : catalog.text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

                    if (_characters.Length == 0)
                        Debug.LogWarning($"[CharacterAnimLibrary] Resources/{ResourceRoot}/characters.txt 가 없거나 비어 있습니다.");
                }
                return _characters;
            }
        }

        /// <summary>파츠 시트의 프레임 스프라이트를 인덱스 순으로 반환. 파츠가 없으면 빈 배열.</summary>
        public static Sprite[] LoadFrames(string character, string clip, string part)
        {
            string path = $"{ResourceRoot}/{character}/{clip}/{part}";
            if (_frameCache.TryGetValue(path, out var cached)) return cached;

            var frames = Resources.LoadAll<Sprite>(path)
                .OrderBy(s => ParseTrailingIndex(s.name))
                .ToArray();
            _frameCache[path] = frames;
            return frames;
        }

        public static Sprite LoadFrame(string character, string clip, string part, int frameIndex)
        {
            var frames = LoadFrames(character, clip, part);
            if (frames.Length == 0) return null;
            return frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        }

        public static bool HasPart(string character, string clip, string part) => LoadFrames(character, clip, part).Length > 0;

        public static PartClipManifest LoadManifest(string character, string clip)
        {
            string path = $"{ResourceRoot}/{character}/{clip}/manifest";
            if (_manifestCache.TryGetValue(path, out var cached)) return cached;

            var text = Resources.Load<TextAsset>(path);
            var manifest = text != null ? PartClipManifest.FromJson(text.text) : null;
            _manifestCache[path] = manifest;
            return manifest;
        }

        // 임포터가 붙인 "{파츠}_{NN}" 접미사에서 NN을 읽는다. 없으면 0.
        private static int ParseTrailingIndex(string spriteName)
        {
            int underscore = spriteName.LastIndexOf('_');
            if (underscore < 0 || underscore == spriteName.Length - 1) return 0;
            return int.TryParse(spriteName.Substring(underscore + 1), out int index) ? index : 0;
        }
    }
}
