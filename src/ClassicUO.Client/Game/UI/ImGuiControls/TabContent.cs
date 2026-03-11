using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using ClassicUO.Renderer;

namespace ClassicUO.Game.UI.ImGuiControls
{
    /// <summary>
    /// Base class for tab content that is embedded within other windows (like AssistantWindow).
    /// Provides drawing utilities without the full window infrastructure of ImGuiWindow.
    /// </summary>
    public abstract class TabContent : IDisposable
    {
        private Dictionary<ushort, ArtPointerStruct> _texturePointerCache = new();

        /// <summary>
        /// Draw the content of this tab. Called when the tab is active.
        /// </summary>
        public abstract void DrawContent();

        /// <summary>
        /// Called each frame for updates. Override if you need per-frame logic.
        /// </summary>
        public virtual void Update()
        {
        }

        /// <summary>
        /// Sets a tooltip on the previously drawn ImGui item if it's hovered.
        /// </summary>
        protected void SetTooltip(string tooltip)
        {
            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tooltip))
                ImGui.SetTooltip(tooltip);
        }

        /// <summary>
        /// Copies a value to clipboard when the previously drawn ImGui item is clicked.
        /// </summary>
        protected void ClipboardOnClick(string value)
        {
            if (ImGui.IsItemClicked())
            {
                SDL3.SDL.SDL_SetClipboardText(value);
                GameActions.Print($"Copied [{value}] to clipboard.", Constants.HUE_SUCCESS);
            }
        }

        /// <summary>
        /// Draws an art graphic from the UO art files.
        /// </summary>
        /// <param name="graphic">The graphic ID to draw</param>
        /// <param name="size">The size to draw at</param>
        /// <param name="useSmallerIfGfxSmaller">If true, uses original size when smaller than requested</param>
        /// <returns>True if the graphic was drawn successfully</returns>
        protected bool DrawArt(ushort graphic, Vector2 size, bool useSmallerIfGfxSmaller = true)
        {
            SpriteInfo artInfo = Client.Game.UO.Arts.GetArt(graphic);

            if (artInfo.Texture == null)
                return false;

            if (useSmallerIfGfxSmaller && artInfo.UV.Width < size.X && artInfo.UV.Height < size.Y)
                size = new Vector2(artInfo.UV.Width, artInfo.UV.Height);

            if (_texturePointerCache.TryGetValue(graphic, out ArtPointerStruct art))
            {
                ImGui.Image(art.Pointer, size, art.UV0, art.UV1);
                return true;
            }

            if (artInfo.Texture != null)
            {
                var uv0 = new Vector2(artInfo.UV.X / (float)artInfo.Texture.Width, artInfo.UV.Y / (float)artInfo.Texture.Height);
                var uv1 = new Vector2((artInfo.UV.X + artInfo.UV.Width) / (float)artInfo.Texture.Width, (artInfo.UV.Y + artInfo.UV.Height) / (float)artInfo.Texture.Height);
                nint pnt = ImGuiManager.Renderer.BindTexture(artInfo.Texture);

                _texturePointerCache.Add(graphic, new ArtPointerStruct(pnt, artInfo, uv0, uv1, size));

                ImGui.Image(pnt, size, uv0, uv1);
                return true;
            }

            return false;
        }

        public virtual void Dispose()
        {
            foreach (KeyValuePair<ushort, ArtPointerStruct> item in _texturePointerCache)
                if (item.Value.Pointer != IntPtr.Zero)
                    ImGuiManager.Renderer.UnbindTexture(item.Value.Pointer);

            _texturePointerCache.Clear();
        }
    }
}
