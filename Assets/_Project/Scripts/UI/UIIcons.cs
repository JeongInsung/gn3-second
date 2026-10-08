using UnityEngine;

namespace GN3.UI
{
    /// <summary>Resources 아래 UI 아이콘(우편함·골드 등)을 Sprite로 읽는다.</summary>
    public static class UIIcons
    {
        /// <summary>Sprite로 가져와 있으면 그대로, 텍스처로만 가져와 있으면 Sprite로 만들어 쓴다. 없으면 null.</summary>
        public static Sprite Load(string path)
        {
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;
            var texture = Resources.Load<Texture2D>(path);
            return texture != null ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f)) : null;
        }
    }
}
