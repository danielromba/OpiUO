using System;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Automatically marks tiles where Opiland players are located
    /// Reuses the existing TileMarkerManager system (same as pathfinding red tiles)
    /// </summary>
    public sealed class OpilandTileMarkerIntegration : IDisposable
    {
        private static readonly Lazy<OpilandTileMarkerIntegration> _instance = 
            new(() => new OpilandTileMarkerIntegration());

        public static OpilandTileMarkerIntegration Instance => _instance.Value;

        private const ushort OPILAND_PLAYER_HUE = 0x0059; // Cyan color
        private bool _disposed;

        private OpilandTileMarkerIntegration()
        {
            // Subscribe to Opiland tracker updates
            OpilandPlayerTracker.Instance.PlayerUpdated += OnOpilandPlayerUpdated;
            OpilandPlayerTracker.Instance.PlayerRemoved += OnOpilandPlayerRemoved;
        }

        /// <summary>
        /// Check if tile highlighting is enabled in profile
        /// </summary>
        private bool IsEnabled => ProfileManager.CurrentProfile?.ShowOpilandTileHighlights ?? false;

        private void OnOpilandPlayerUpdated(object sender, OpilandPlayerTracker.OpilandPlayer player)
        {
            if (!IsEnabled || player == null)
                return;

            // Mark the tile where the player is located (like pathfinding tiles)
            TileMarkerManager.Instance.AddTile(player.X, player.Y, player.MapIndex, OPILAND_PLAYER_HUE);
        }

        private void OnOpilandPlayerRemoved(object sender, OpilandPlayerTracker.OpilandPlayer player)
        {
            if (player == null)
                return;

            // Remove the tile marker
            TileMarkerManager.Instance.RemoveTile(player.X, player.Y, player.MapIndex);
        }

        /// <summary>
        /// Toggle tile highlighting on/off
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (ProfileManager.CurrentProfile != null)
            {
                ProfileManager.CurrentProfile.ShowOpilandTileHighlights = enabled;

                if (enabled)
                {
                    // Mark all currently tracked players
                    foreach (var player in OpilandPlayerTracker.Instance.GetAllPlayers())
                    {
                        TileMarkerManager.Instance.AddTile(player.X, player.Y, player.MapIndex, OPILAND_PLAYER_HUE);
                    }
                }
                else
                {
                    // Clear all Opiland markers
                    foreach (var player in OpilandPlayerTracker.Instance.GetAllPlayers())
                    {
                        TileMarkerManager.Instance.RemoveTile(player.X, player.Y, player.MapIndex);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Unsubscribe from events
            OpilandPlayerTracker.Instance.PlayerUpdated -= OnOpilandPlayerUpdated;
            OpilandPlayerTracker.Instance.PlayerRemoved -= OnOpilandPlayerRemoved;

            // Clear all markers
            SetEnabled(false);
        }
    }
}
