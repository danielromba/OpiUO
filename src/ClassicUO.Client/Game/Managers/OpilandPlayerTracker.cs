// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Tracks player positions received via Opiland WebSocket for display on WorldMapGump
/// </summary>
public sealed class OpilandPlayerTracker
{
    private static OpilandPlayerTracker _instance;
    private readonly ConcurrentDictionary<string, OpilandPlayer> _trackedPlayers = new();
    private readonly ConcurrentDictionary<uint, string> _serialToNameLookup = new(); // O(1) serial lookup
    private readonly TimeSpan _staleTimeout = TimeSpan.FromSeconds(20);
    private DateTime _lastStaleCheck = DateTime.MinValue;
    private readonly TimeSpan _staleCheckInterval = TimeSpan.FromSeconds(2); // Throttle stale checks

    public static OpilandPlayerTracker Instance => _instance ??= new OpilandPlayerTracker();

    /// <summary>
    /// Fired when a player position is updated or added
    /// </summary>
    public event EventHandler<OpilandPlayer> PlayerUpdated;

    /// <summary>
    /// Fired when a player is removed from tracking
    /// </summary>
    public event EventHandler<OpilandPlayer> PlayerRemoved;

    private OpilandPlayerTracker()
    {
        // Subscribe to Opiland events
        OpilandClientManager.Instance.MessageReceived += OnClientMessageReceived;
        OpilandServerManager.Instance.MessageReceived += OnServerMessageReceived;
    }

    /// <summary>
    /// Represents a tracked Opiland player
    /// </summary>
    public class OpilandPlayer
    {
        public uint Serial { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int MapIndex { get; set; }
        public int Hue { get; set; } = 0x005A;
        public DateTime LastUpdate { get; set; }
        public string ClientId { get; set; } // For server tracking
    }

    private void OnClientMessageReceived(object sender, ClientMessageReceivedEventArgs e)
    {
        ProcessMessage(e.Message, null);
    }

    private void OnServerMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        ProcessMessage(e.Message, e.ClientId);
    }

    private void ProcessMessage(string messageJson, string clientId)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            Log.Warn("Received empty Opiland message");
            return;
        }

        try
        {
            // Log raw message for debugging
            Log.Trace($"OpilandPlayerTracker processing message: {messageJson}");

            OpilandMessage message = OpilandMessage.Deserialize(messageJson);

            if (message == null)
            {
                Log.Warn($"Failed to deserialize Opiland message: {messageJson}");
                return;
            }

            if (message is MobilePositionMessage positionMsg)
            {
                Log.Trace($"Received position message for {positionMsg.Name} ({positionMsg.Serial}) at {positionMsg.X},{positionMsg.Y}");

                var player = new OpilandPlayer
                {
                    Serial = positionMsg.Serial,
                    Name = positionMsg.Name,
                    X = positionMsg.X,
                    Y = positionMsg.Y,
                    Z = positionMsg.Z,
                    MapIndex = positionMsg.MapIndex,
                    Hue = positionMsg.Hue,
                    LastUpdate = DateTime.UtcNow,
                    ClientId = clientId
                };

                UpdatePlayer(player);
                Log.Trace($"Updated player tracker for {player.Name}, total players: {_trackedPlayers.Count}");
            }
            else
            {
                Log.Trace($"Received non-position message type: {message.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error processing Opiland message: {ex.Message}\nMessage: {messageJson}\nStackTrace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Manually update or add a player position
    /// </summary>
    public void UpdatePlayer(OpilandPlayer player)
    {
        if (player == null || string.IsNullOrWhiteSpace(player.Name))
            return;

        _trackedPlayers[player.Name] = player;

        // Update serial lookup for O(1) IsPlayerTracked() performance
        _serialToNameLookup[player.Serial] = player.Name;

        // Fire event on main thread
        MainThreadQueue.EnqueueAction(() =>
        {
            PlayerUpdated?.Invoke(this, player);
        });
    }

    /// <summary>
    /// Remove a player from tracking
    /// </summary>
    public void RemovePlayer(string playerName)
    {
        if (!string.IsNullOrWhiteSpace(playerName) && 
            _trackedPlayers.TryRemove(playerName, out var player))
        {
            // Remove from serial lookup
            _serialToNameLookup.TryRemove(player.Serial, out _);

            // Fire event on main thread
            MainThreadQueue.EnqueueAction(() =>
            {
                PlayerRemoved?.Invoke(this, player);
            });
        }
    }

    /// <summary>
    /// Get all currently tracked players
    /// </summary>
    public IEnumerable<OpilandPlayer> GetAllPlayers()
    {
        RemoveStaleEntriesIfNeeded();
        return _trackedPlayers.Values.ToList();
    }

    /// <summary>
    /// Check if a mobile serial is being tracked by Opiland (O(1) performance)
    /// </summary>
    public bool IsPlayerTracked(uint serial)
    {
        return _serialToNameLookup.ContainsKey(serial);
    }

    /// <summary>
    /// Get players on a specific map
    /// </summary>
    public IEnumerable<OpilandPlayer> GetPlayersOnMap(int mapIndex)
    {
        RemoveStaleEntriesIfNeeded();
        return _trackedPlayers.Values.Where(p => p.MapIndex == mapIndex).ToList();
    }

    /// <summary>
    /// Clear all tracked players
    /// </summary>
    public void Clear()
    {
        _trackedPlayers.Clear();
        _serialToNameLookup.Clear();
    }

    /// <summary>
    /// Remove entries that haven't updated recently (throttled to run every 2 seconds)
    /// </summary>
    private void RemoveStaleEntriesIfNeeded()
    {
        var now = DateTime.UtcNow;

        // Only run stale check every 2 seconds instead of every frame
        if ((now - _lastStaleCheck) < _staleCheckInterval)
        {
            return;
        }

        _lastStaleCheck = now;
        RemoveStaleEntries();
    }

    /// <summary>
    /// Actually remove stale entries
    /// </summary>
    private void RemoveStaleEntries()
    {
        var now = DateTime.UtcNow;
        var staleKeys = _trackedPlayers
            .Where(kvp => now - kvp.Value.LastUpdate > _staleTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string key in staleKeys)
        {
            if (_trackedPlayers.TryRemove(key, out var player))
            {
                // Remove from serial lookup
                _serialToNameLookup.TryRemove(player.Serial, out _);
            }
        }
    }

    /// <summary>
    /// Build a position message for broadcasting your position
    /// </summary>
    public static string BuildPositionMessage(Mobile mobile)
    {
        var serial = mobile?.Serial ?? 0;
        var name = string.IsNullOrWhiteSpace(ProfileManager.CurrentProfile.OpilandClientCustomName) 
            ? (mobile?.Name ?? "player") 
            : ProfileManager.CurrentProfile.OpilandClientCustomName;
        var x = mobile?.X ?? 0;
        var y = mobile?.Y ?? 0;
        var z = mobile?.Z ?? 0;
        var mapIndex = World.Instance.MapIndex;
        var hue = ProfileManager.CurrentProfile.OpilandClientCustomNameHue;

        var message = new MobilePositionMessage
        {
            Serial = serial,
            Name = name,
            X = x,
            Y = y,
            Z = z,
            MapIndex = mapIndex,
            Hue = hue
        };

        return message.Serialize();
    }

    /// <summary>
    /// Get count of tracked players
    /// </summary>
    public int Count
    {
        get
        {
            RemoveStaleEntriesIfNeeded();
            return _trackedPlayers.Count;
        }
    }

    /// <summary>
    /// Check if a specific player is being tracked
    /// </summary>
    public bool IsTracking(string playerName)
    {
        return !string.IsNullOrWhiteSpace(playerName) && _trackedPlayers.ContainsKey(playerName);
    }
}
