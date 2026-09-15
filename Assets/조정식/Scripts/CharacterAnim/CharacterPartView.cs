using System;
using System.Collections.Generic;
using UnityEngine;

namespace GN3.CharacterAnim
{
    /// <summary>
    /// 파츠별 SpriteRenderer를 자식으로 두고 조합 결과를 그린다.
    /// 모든 파츠 시트가 같은 256x256 좌표계를 공유하므로 전부 (0,0)에 두고 sortingOrder만 z-order 순서로 준다.
    /// </summary>
    public class CharacterPartView : MonoBehaviour
    {
        private readonly Dictionary<string, SpriteRenderer> _renderers = new Dictionary<string, SpriteRenderer>();

        public void Show(ComposedCharacter character)
        {
            foreach (var renderer in _renderers.Values)
                renderer.sprite = null;

            int order = 0;
            foreach (var part in character.ZOrderBackToFront)
            {
                var renderer = GetOrCreateRenderer(part);
                renderer.sortingOrder = order++;
                renderer.sprite = character.Parts.TryGetValue(part, out var sprite) ? sprite : null;
            }

            // manifest z-order에 없는 파츠가 조합에 섞여 있으면 맨 앞에 그린다.
            foreach (var kv in character.Parts)
            {
                if (Array.IndexOf(character.ZOrderBackToFront, kv.Key) >= 0) continue;
                var renderer = GetOrCreateRenderer(kv.Key);
                renderer.sortingOrder = order++;
                renderer.sprite = kv.Value;
            }
        }

        private SpriteRenderer GetOrCreateRenderer(string part)
        {
            if (_renderers.TryGetValue(part, out var existing)) return existing;

            var go = new GameObject(part, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var renderer = go.GetComponent<SpriteRenderer>();
            _renderers[part] = renderer;
            return renderer;
        }
    }
}
