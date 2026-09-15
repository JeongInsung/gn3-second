using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GN3.CharacterAnim
{
    /// <summary>파츠별 스프라이트 조합 결과. 지금은 프레임 1장(rest pose)만 담는다.</summary>
    public class ComposedCharacter
    {
        public Dictionary<string, Sprite> Parts { get; } = new Dictionary<string, Sprite>();
        public Dictionary<string, string> PartSources { get; } = new Dictionary<string, string>();
        public string[] ZOrderBackToFront { get; set; } = PartClipManifest.DefaultZOrderBackToFront;
        public string BodyCharacter { get; set; }

        public string SourceOf(string part) => PartSources.TryGetValue(part, out var s) ? s : "-";
    }

    /// <summary>
    /// 몸 세트(torso/팔/다리)는 한 캐릭터 폴더에서, 머리 세트(head/hair)는 다른 한 캐릭터 폴더에서 함께 뽑아 조합한다. weapon은 독립 랜덤.
    /// </summary>
    public static class RandomCharacterComposer
    {
        public static ComposedCharacter Compose(System.Random rng, string clip = CharacterAnimLibrary.DefaultClip, int frameIndex = 0)
        {
            var characters = CharacterAnimLibrary.Characters;
            var result = new ComposedCharacter();
            if (characters.Count == 0) return result;

            // 1) 몸 세트: 몸통 파츠가 있는 캐릭터 중 하나를 골라 5파츠를 전부 그 캐릭터에서 가져온다.
            var bodyCandidates = characters.Where(c => CharacterAnimLibrary.HasPart(c, clip, CharacterPartId.Torso)).ToList();
            if (bodyCandidates.Count == 0) bodyCandidates = characters.ToList();
            string bodyCharacter = bodyCandidates[rng.Next(bodyCandidates.Count)];
            result.BodyCharacter = bodyCharacter;

            foreach (var part in CharacterPartId.BodySetParts)
                Assign(result, bodyCharacter, clip, part, frameIndex);

            // 2) 머리 세트: head가 있는 캐릭터 하나를 골라 head/hair를 함께 가져온다 (CharacterPartId.HeadSetParts 참고).
            var headCandidates = characters.Where(c => CharacterAnimLibrary.HasPart(c, clip, CharacterPartId.Head)).ToList();
            if (headCandidates.Count > 0)
            {
                string headCharacter = headCandidates[rng.Next(headCandidates.Count)];
                foreach (var part in CharacterPartId.HeadSetParts)
                    Assign(result, headCharacter, clip, part, frameIndex);
            }

            // 3) 독립 파츠: 파츠마다 그 파츠를 가진 캐릭터 중에서 따로 뽑는다.
            foreach (var part in CharacterPartId.IndependentParts)
            {
                var candidates = characters.Where(c => CharacterAnimLibrary.HasPart(c, clip, part)).ToList();
                if (candidates.Count == 0) continue;
                Assign(result, candidates[rng.Next(candidates.Count)], clip, part, frameIndex);
            }

            var manifest = CharacterAnimLibrary.LoadManifest(bodyCharacter, clip);
            result.ZOrderBackToFront = manifest?.ZOrderOrDefault ?? PartClipManifest.DefaultZOrderBackToFront;
            return result;
        }

        private static void Assign(ComposedCharacter result, string character, string clip, string part, int frameIndex)
        {
            var sprite = CharacterAnimLibrary.LoadFrame(character, clip, part, frameIndex);
            if (sprite == null) return;
            result.Parts[part] = sprite;
            result.PartSources[part] = character;
        }
    }
}
