# Opiland Player Tracker - Complete Implementation

## Overview

The Opiland Player Tracker system allows you to see other players on your WorldMapGump even when they're outside your normal UO server view range. This works by having players broadcast their positions via the Opiland WebSocket network.

## Architecture

```
┌─────────────────────────────────────┐
│         Player 1 (Server)           │
│                                     │
│  ┌──────────────────────────────┐  │
│  │  OpilandServerManager        │  │
│  │  - Accepts connections       │  │
│  │  - Relays messages           │  │
│  └──────────────────────────────┘  │
│                                     │
│  ┌──────────────────────────────┐  │
│  │  OpilandPlayerTracker        │  │
│  │  - Stores player positions   │  │
│  └──────────────────────────────┘  │
│                                     │
│  ┌──────────────────────────────┐  │
│  │  WorldMapGump                │  │
│  │  - Draws tracked players     │  │
│  └──────────────────────────────┘  │
└─────────────────────────────────────┘
            ▲         │
            │         │ WebSocket
            │         ▼
┌───────────┴──────────────────────────┐
│         Player 2 (Client)            │
│                                      │
│  ┌──────────────────────────────┐   │
│  │  OpilandClientManager        │   │
│  │  - Connects to server        │   │
│  │  - Sends/receives messages   │   │
│  └──────────────────────────────┘   │
│                                      │
│  ┌──────────────────────────────┐   │
│  │  OpilandPlayerTracker        │   │
│  │  - Stores player positions   │   │
│  └──────────────────────────────┘   │
│                                      │
│  ┌──────────────────────────────┐   │
│  │  WorldMapGump                │   │
│  │  - Draws tracked players     │   │
│  └──────────────────────────────┘   │
└──────────────────────────────────────┘
```

## Components

### 1. OpilandPlayerTracker.cs
**Location:** `src\ClassicUO.Client\Game\Managers\OpilandPlayerTracker.cs`

**Purpose:** Central manager for tracking player positions received via Opiland network

**Features:**
- Singleton pattern for easy access
- Automatic message parsing (format: `OPILAND_POS|Name|X|Y|Z|Map`)
- Stale entry removal (5-minute timeout)
- Thread-safe concurrent dictionary storage
- Event subscription to both Client and Server managers

**Key Methods:**
```csharp
void UpdatePlayer(OpilandPlayer player)           // Add/update a player
void RemovePlayer(string playerName)              // Remove a player
IEnumerable<OpilandPlayer> GetPlayersOnMap(int)   // Get players on specific map
void Clear()                                      // Clear all tracked players
string BuildPositionMessage(...)                  // Helper to build message
```

### 2. WorldMapGump Integration
**Location:** `src\ClassicUO.Client\Game\UI\Gumps\WorldMapGump.cs`

**Changes:**
- Added `_showOpilandPlayers`, `_showOpilandPlayerNames`, `_showOpilandPlayerBars` fields
- Added profile settings persistence
- Added context menu options under "Opiland Network"
- Added `DrawOpilandPlayer()` method (draws in cyan/turquoise)
- Integrated drawing loop in `DrawAll()` method

**Drawing Style:**
- **Color:** Cyan (distinguishes from party/guild members)
- **Name Display:** Controlled by `_showOpilandPlayerNames`
- **Health Bar:** Controlled by `_showOpilandPlayerBars` (shows full bar)
- **Position:** Drawn between corpse and pathfinding indicators

### 3. Profile Settings
**Location:** `src\ClassicUO.Client\Configuration\Profile.cs`

**New Properties:**
```csharp
public bool WorldMapShowOpilandPlayers { get; set; } = true;
public bool WorldMapShowOpilandPlayerNames { get; set; } = true;
public bool WorldMapShowOpilandPlayerBars { get; set; } = true;
```

## Message Protocol

### Format
```
OPILAND_POS|PlayerName|X|Y|Z|MapIndex
```

### Example
```
OPILAND_POS|John|1234|5678|0|0
```

### Fields
- **Prefix:** `OPILAND_POS|` (identifies position messages)
- **PlayerName:** Character name
- **X, Y, Z:** World coordinates
- **MapIndex:** Map index (0=Felucca, 1=Trammel, etc.)

## Usage

### Setup (One-Time)

**As Server (Host):**
```csharp
// In Legion script
Api.OpilandStartServer();  // Uses profile defaults
// or
Api.OpilandStartServer("0.0.0.0", 8080);
```

**As Client (Join):**
```csharp
// In Legion script
Api.OpilandConnectClient();  // Uses profile defaults
// or
Api.OpilandConnectClient("192.168.1.100", 8080, "password");
```

### Broadcasting Position (Continuous)

Run the `OpilandPositionBroadcaster.cs` script:
```csharp
// This script broadcasts your position every second
// Located at: src\ClassicUO.Client\Examples\OpilandPositionBroadcaster.cs
```

**The script automatically:**
- Detects if you're server or client
- Builds proper message format
- Broadcasts/sends every second
- Handles errors gracefully

### Viewing Players on Map

1. **Open World Map** (default hotkey)
2. **Right-click on map** → **Opiland Network** submenu
3. **Toggle options:**
   - Show Opiland Players (on/off)
   - Show Opiland Names (on/off)
   - Show Opiland Health Bars (on/off)

**Visual Indicators:**
- **Cyan dots** = Opiland players
- **Yellow dots** = Party members
- **Green dots** = Guild members
- **White dot** = You

## Example Scenarios

### Scenario 1: Party Coordination
**Setup:**
1. **Player 1:** Start server
2. **Player 2-4:** Connect as clients
3. **All players:** Run position broadcaster script
4. **Result:** All players see each other on map

### Scenario 2: Guild Hunt
**Setup:**
1. **Guild leader:** Start server on public IP
2. **Guild members:** Connect to leader's IP
3. **All members:** Run broadcaster
4. **Result:** Guild-wide position tracking

### Scenario 3: PvP Tracking
**Setup:**
1. **Scouts:** Connect to team server
2. **Scouts:** Run broadcaster
3. **Commander:** Monitor all scout positions on map
4. **Result:** Real-time enemy location intelligence

## Advanced Features

### Programmatic Control

**Direct Position Update:**
```csharp
// Manually add a player position (without broadcasting)
using ClassicUO.Game.Managers;

var player = new OpilandPlayerTracker.OpilandPlayer
{
    Name = "Scout1",
    X = 1234,
    Y = 5678,
    Z = 0,
    MapIndex = 0,
    LastUpdate = DateTime.UtcNow
};

OpilandPlayerTracker.Instance.UpdatePlayer(player);
```

**Check Tracked Players:**
```csharp
// Get all tracked players on current map
var players = OpilandPlayerTracker.Instance.GetPlayersOnMap(Api.Player.Map);
foreach (var player in players)
{
    Api.SysMsg($"{player.Name} at {player.X},{player.Y}");
}
```

**Custom Broadcast Interval:**
```csharp
// In your script, modify the constant
private const double UPDATE_INTERVAL = 0.5; // Broadcast every 0.5 seconds (more traffic)
// or
private const double UPDATE_INTERVAL = 5.0; // Broadcast every 5 seconds (less traffic)
```

### Event Handling

The tracker automatically subscribes to:
- `OpilandClientManager.MessageReceived`
- `OpilandServerManager.MessageReceived`

Messages matching the protocol are automatically parsed and stored.

## Troubleshooting

### Players not showing up
1. **Check connection:** `Api.OpilandServerIsRunning()` or `Api.OpilandClientIsConnected()`
2. **Verify script is running:** Both sides need broadcaster script
3. **Check map:** Players only show on their current map
4. **Verify options:** Right-click map → Opiland Network → Show Opiland Players

### Old positions stuck
- Positions auto-expire after 5 minutes of no updates
- Manually clear: `OpilandPlayerTracker.Instance.Clear()`

### Connection issues
- **Firewall:** Make sure port is open
- **IP Address:** Use `0.0.0.0` for server to accept all connections
- **Password:** Make sure all clients use correct password

### Performance
- Default broadcast interval (1 second) is balanced
- More frequent = more accurate but more traffic
- Stale entries automatically cleaned every query

## Files Created/Modified

### New Files:
1. `src\ClassicUO.Client\Game\Managers\OpilandPlayerTracker.cs`
2. `src\ClassicUO.Client\Examples\OpilandPositionBroadcaster.cs`
3. `src\ClassicUO.Client\Examples\OpilandNetworkSetup.cs`
4. `src\ClassicUO.Client\Examples\OpilandAdvancedBroadcaster.cs`
5. `src\ClassicUO.Client\Examples\OpilandPlayerMonitor.cs`

### Modified Files:
1. `src\ClassicUO.Client\Game\UI\Gumps\WorldMapGump.cs`
   - Added Opiland player drawing
   - Added context menu options
   - Added profile settings
2. `src\ClassicUO.Client\Configuration\Profile.cs`
   - Added 3 new WorldMap properties

## API Reference

### Legion API Methods (C# Scripts)
```csharp
// Server
Api.OpilandStartServer(address, port);  // Returns bool
Api.OpilandStopServer();
Api.OpilandServerBroadcast(message);
Api.OpilandServerIsRunning();           // Returns bool
Api.OpilandServerClientCount();         // Returns int
Api.OpilandServerGetInfo();             // Returns OpilandConnectionInfo

// Client
Api.OpilandConnectClient(address, port, password);  // Returns bool
Api.OpilandDisconnectClient();
Api.OpilandClientSendMessage(message);
Api.OpilandClientIsConnected();         // Returns bool
Api.OpilandClientGetInfo();             // Returns OpilandConnectionInfo
```

### OpilandPlayerTracker Methods (C# Only)
```csharp
void UpdatePlayer(OpilandPlayer player)
void RemovePlayer(string playerName)
IEnumerable<OpilandPlayer> GetAllPlayers()
IEnumerable<OpilandPlayer> GetPlayersOnMap(int mapIndex)
void Clear()
int Count { get; }
bool IsTracking(string playerName)

static string BuildPositionMessage(string name, int x, int y, int z, int map)
```

## Protocol Extension

Want to send additional data? Extend the protocol:

**Example: Add health info**
```csharp
// Custom message format in your broadcaster script
string message = $"OPILAND_POS|{name}|{x}|{y}|{z}|{map}|{hp}|{hpMax}";
Api.OpilandServerBroadcast(message);
```

**Parse in C#:**
```csharp
// Modify ProcessPositionMessage() in OpilandPlayerTracker.cs
if (parts.Length >= 7)
{
    player.HP = int.Parse(parts[5]);
    player.HPMax = int.Parse(parts[6]);
}
```

## Security Considerations

1. **Passwords:** Use strong passwords for production
2. **Firewall:** Don't expose server to internet without protection
3. **Validation:** Messages are validated for format
4. **Stale Data:** Old entries auto-expire
5. **Local Network:** Safest to use on LAN

## Future Enhancements

Possible additions:
- Custom colors per player
- Player groups/teams
- Directional indicators
- Distance display
- Click-to-follow
- Position history/trails
- Alert on player proximity

## Support

For issues or questions:
1. Check this README
2. Review example scripts
3. Verify all components are installed
4. Check console for error messages

---

**Status:** ✅ Complete and ready to use!
