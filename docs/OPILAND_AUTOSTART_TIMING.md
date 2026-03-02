# Opiland Autostart - Proper Initialization Timing

## Problem

Autostart in the constructor was too early:
```csharp
private OpilandServerManager()
{
    // ❌ Too early! ProfileManager might not be ready
    if (ProfileManager.CurrentProfile.OpilandServerAutostart)
    {
        StartServer(...);
    }
}
```

**Issues:**
- `ProfileManager.CurrentProfile` might be null or not fully loaded
- Network infrastructure not ready
- Game/World not initialized yet

## Solution

Use **EventSink.OnConnected** event to trigger autostart when the player actually connects to the game.

### Implementation

#### 1. OpilandServerManager

```csharp
private OpilandServerManager()
{
    // Subscribe to connection event for autostart
    EventSink.OnConnected += OnGameConnected;
}

private void OnGameConnected(object sender, EventArgs e)
{
    // Try autostart when player connects to game
    TryAutoStart();
}

/// <summary>
/// Attempts to auto-start the server based on profile settings.
/// Safe to call multiple times - will only start if not already running.
/// </summary>
public void TryAutoStart()
{
    if (ProfileManager.CurrentProfile?.OpilandServerAutostart == true && !IsRunning)
    {
        string address = ProfileManager.CurrentProfile.OpilandServerIp;
        if (string.IsNullOrWhiteSpace(address))
            address = "127.0.0.1";

        int port = 5055;
        if (int.TryParse(ProfileManager.CurrentProfile.OpilandServerPort, out int parsedPort))
            port = parsedPort;

        Log.Trace($"Opiland server auto-starting on {address}:{port}");
        StartServer(address, port);
    }
}

public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;
    
    // Unsubscribe from events
    EventSink.OnConnected -= OnGameConnected;
    
    StopServer();
}
```

#### 2. OpilandClientManager

```csharp
private OpilandClientManager()
{
    // Subscribe to connection event for autostart
    EventSink.OnConnected += OnGameConnected;
}

private void OnGameConnected(object sender, EventArgs e)
{
    // Try autostart when player connects to game
    _ = TryAutoStartAsync();
}

/// <summary>
/// Attempts to auto-connect the client based on profile settings.
/// Safe to call multiple times - will only connect if not already connected.
/// </summary>
public async Task TryAutoStartAsync()
{
    if (ProfileManager.CurrentProfile?.OpilandServerAutostart == true && !IsConnected)
    {
        string address = ProfileManager.CurrentProfile.OpilandClientIp;
        if (string.IsNullOrWhiteSpace(address))
            address = "127.0.0.1";

        int port = 5055;
        if (int.TryParse(ProfileManager.CurrentProfile.OpilandClientPort, out int parsedPort))
            port = parsedPort;

        string password = ProfileManager.CurrentProfile.OpilandServerPassword ?? "";

        Log.Trace($"Opiland client auto-connecting to {address}:{port}");
        await ConnectAsync(address, port, password);
    }
}

public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;
    
    // Unsubscribe from events
    EventSink.OnConnected -= OnGameConnected;
    
    Disconnect();
}
```

## Why This Works

### Timing
```
1. Application Start
   └─> Singleton constructors called (too early)

2. Player Connects to UO Server
   └─> EventSink.OnConnected fired ✅ (perfect timing!)
       └─> OpilandServerManager.TryAutoStart()
       └─> OpilandClientManager.TryAutoStartAsync()
```

### Benefits

✅ **Profile is loaded** - `ProfileManager.CurrentProfile` is guaranteed to be available  
✅ **Game is initialized** - All systems are ready  
✅ **Network is ready** - Can establish WebSocket connections  
✅ **Safe to call multiple times** - Checks if already running/connected  
✅ **Proper cleanup** - Unsubscribes in Dispose  

## Available EventSink Events

If you need different timing, other available events:

```csharp
// When player character is created (happens before OnConnected)
EventSink.OnPlayerCreated

// When connected to UO server (used in solution) ⭐
EventSink.OnConnected

// When disconnected from UO server
EventSink.OnDisconnected

// When item is created
EventSink.OnItemCreated

// When corpse is created
EventSink.OnCorpseCreated
```

## Usage Flow

### Autostart Enabled in Profile
```
1. User logs into UO
2. EventSink.OnConnected fires
3. TryAutoStart() checks profile setting
4. Profile.OpilandServerAutostart == true
5. Server/Client starts automatically
6. User sees message in log
```

### Autostart Disabled
```
1. User logs into UO
2. EventSink.OnConnected fires
3. TryAutoStart() checks profile setting
4. Profile.OpilandServerAutostart == false
5. Nothing happens (no autostart)
6. User can manually start via API
```

### Manual Start Anytime
```csharp
// Can still be called manually at any time
Api.OpilandStartServer();
Api.OpilandConnectClient();

// Or programmatically
OpilandServerManager.Instance.TryAutoStart();
await OpilandClientManager.Instance.TryAutoStartAsync();
```

## Alternative Approaches (Not Used)

### Option 1: Profile.AfterLoad() ❌
```csharp
// In ProfileManager.cs
internal void AfterLoad()
{
    // Could work but:
    // - Requires modifying ProfileManager
    // - Profile loads before game connection
    // - Still might be too early
}
```

### Option 2: Delayed Initialization ❌
```csharp
// In constructor
Task.Delay(5000).ContinueWith(_ => TryAutoStart());
// Problems:
// - Arbitrary delay (unreliable)
// - Might still be too early or too late
// - No guarantee profile is ready
```

### Option 3: Explicit Call from Game ❌
```csharp
// In Game.cs or World.cs
OpilandServerManager.Instance.TryAutoStart();
// Problems:
// - Requires modifying game core code
// - Tight coupling
// - Less flexible
```

## Testing

### Test 1: Autostart Enabled
```
1. Set OpilandServerAutostart = true in profile
2. Launch game and log in
3. Should see: "Opiland server auto-starting on 127.0.0.1:5055"
4. Server should be running
```

### Test 2: Autostart Disabled
```
1. Set OpilandServerAutostart = false in profile
2. Launch game and log in
3. Should see: No autostart messages
4. Server should NOT be running
```

### Test 3: Manual Override
```
1. Set OpilandServerAutostart = false
2. Log in (no autostart)
3. Call: Api.OpilandStartServer()
4. Server should start manually
```

### Test 4: Already Running
```
1. Start server manually
2. Call TryAutoStart()
3. Should check IsRunning and skip (no duplicate start)
```

## Summary

**Before:** Autostart in constructor → Too early, unreliable  
**After:** Autostart on EventSink.OnConnected → Perfect timing, reliable  

✅ Build successful  
✅ Proper event lifecycle  
✅ Safe to call multiple times  
✅ Clean disposal  
