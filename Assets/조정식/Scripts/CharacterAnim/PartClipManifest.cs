using System;
using UnityEngine;

namespace GN3.CharacterAnim
{
    /// <summary>pixel-anim-tool이 클립 폴더마다 내보내는 manifest.json의 필요한 부분만 매핑.</summary>
    [Serializable]
    public class PartClipManifest
    {
        public string clip;
        public int frameCount;
        public int fps;
        public bool loop;
        public string[] zOrderBackToFront;

        /// <summary>manifest가 없거나 z-order가 비어 있을 때 쓰는 기본 순서(뒤→앞).</summary>
        public static readonly string[] DefaultZOrderBackToFront =
        {
            CharacterPartId.ArmBack, CharacterPartId.LegBack, CharacterPartId.LegFront, CharacterPartId.Torso,
            CharacterPartId.ArmFront, CharacterPartId.Weapon, CharacterPartId.SlashFx, CharacterPartId.Head, CharacterPartId.Hair,
        };

        public string[] ZOrderOrDefault =>
            zOrderBackToFront != null && zOrderBackToFront.Length > 0 ? zOrderBackToFront : DefaultZOrderBackToFront;

        public static PartClipManifest FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<PartClipManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PartClipManifest] manifest 파싱 실패: {e.Message}");
                return null;
            }
        }
    }
}
