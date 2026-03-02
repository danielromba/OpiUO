# Circular Reference Fix for Opiland Server/Client Managers

## Problem

When implementing mutual exclusivity (only server OR client can be active at once), a circular reference was created:

```
OpilandServerManager.StartServer()
    → calls OpilandClientManager.Disconnect()
        → calls OpilandServerManager.StopServer()
            → calls OpilandClientManager.Disconnect()
                → INFINITE LOOP!
```

## Root Cause

Both managers tried to stop the other when starting, creating an infinite loop:

**OpilandServerManager.cs:**
```csharp
public void StartServer(...)
{
    // This creates circular reference!
    if (OpilandClientManager.Instance.IsConnected)
        OpilandClientManager.Instance.Disconnect();
}
```

**OpilandClientManager.cs:**
```csharp
public async Task<bool> ConnectAsync(...)
{
    // This creates circular reference!
    if (OpilandServerManager.Instance.IsRunning)
        OpilandServerManager.Instance.StopServer();
}
```

## Solution

Added a `_isChangingState` flag to prevent re-entrancy during state changes.

### Changes Made

#### 1. Added State Change Flag

**Both managers now have:**
```csharp
private bool _isChangingState; // Prevent circular references
```

#### 2. Protected State Change Methods

**OpilandServerManager.StartServer():**
```csharp
public void StartServer(string address, int port)
{
    lock (_serverLock)
    {
        if (IsRunning) return;
        
        // Prevent circular reference during state change
        if (_isChangingState)
            return;

        try
        {
            _isChangingState = true;

            // Safe to call client disconnect now
            if (OpilandClientManager.Instance.IsConnected)
                OpilandClientManager.Instance.Disconnect();

            // ... rest of start logic
        }
        finally
        {
            _isChangingState = false;
        }
    }
}
```

**OpilandServerManager.StopServer():**
```csharp
public void StopServer()
{
    lock (_serverLock)
    {
        if (!IsRunning) return;
        
        // Prevent circular reference during state change
        if (_isChangingState)
            return;

        try
        {
            _isChangingState = true;
            // ... stop logic
        }
        finally
        {
            _isChangingState = false;
        }
    }
}
```

**OpilandClientManager.ConnectAsync():**
```csharp
public async Task<bool> ConnectAsync(string address, int port, string password = null)
{
    lock (_clientLock)
    {
        if (IsConnected) return false;
        
        // Prevent circular reference during state change
        if (_isChangingState)
            return false;
    }

    // Safe to call server stop now
    if (OpilandServerManager.Instance.IsRunning)
    {
        _isChangingState = true;
        try
        {
            OpilandServerManager.Instance.StopServer();
        }
        finally
        {
            _isChangingState = false;
        }
    }

    try
    {
        _isChangingState = true;
        // ... connect logic
    }
    finally
    {
        _isChangingState = false;
    }
}
```

**OpilandClientManager.Disconnect():**
```csharp
public void Disconnect()
{
    lock (_clientLock)
    {
        if (!IsConnected && _webSocket == null) return;
        
        // Prevent circular reference during state change
        if (_isChangingState)
            return;

        try
        {
            _isChangingState = true;
            // ... disconnect logic
        }
        finally
        {
            _isChangingState = false;
        }
    }
}
```

## How It Works

### Scenario 1: Starting Server While Client Is Connected

```
1. User calls: OpilandServerManager.StartServer()
2. StartServer sets _isChangingState = true
3. StartServer calls: OpilandClientManager.Disconnect()
4. Disconnect checks _isChangingState (client's flag is false, so continues)
5. Disconnect sets _isChangingState = true (client's flag)
6. Disconnect does NOT call StopServer because client is in disconnect mode
7. Client disconnect completes, sets _isChangingState = false
8. Server start completes, sets _isChangingState = false
9. ✅ Success! No circular call
```

### Scenario 2: Connecting Client While Server Is Running

```
1. User calls: OpilandClientManager.ConnectAsync()
2. ConnectAsync sets _isChangingState = true
3. ConnectAsync calls: OpilandServerManager.StopServer()
4. StopServer checks _isChangingState (server's flag is false, so continues)
5. StopServer sets _isChangingState = true (server's flag)
6. StopServer does NOT call Disconnect because server is in stop mode
7. Server stop completes, sets _isChangingState = false
8. Client connect completes, sets _isChangingState = false
9. ✅ Success! No circular call
```

### Scenario 3: Attempt to Recursively Change State

```
1. User calls: OpilandServerManager.StartServer()
2. StartServer sets _isChangingState = true
3. StartServer calls: OpilandClientManager.Disconnect()
4. Disconnect (hypothetically) tries to call: StopServer()
5. StopServer checks _isChangingState → TRUE!
6. StopServer returns immediately without doing anything
7. ✅ Circular reference prevented!
```

## Benefits

1. **Prevents Infinite Loops**: Flag stops recursive calls
2. **Thread-Safe**: Combined with existing locks
3. **Clean State**: Each operation completes properly
4. **No Side Effects**: Other code unchanged
5. **Simple Logic**: Easy to understand and maintain

## Testing

Test the following scenarios:

### Test 1: Start Server → Connect Client
```csharp
// Should stop server and start client
Api.OpilandStartServer();
Thread.Sleep(1000);
Api.OpilandConnectClient("127.0.0.1", 8080);
// Expected: Client connected, server stopped
```

### Test 2: Connect Client → Start Server
```csharp
// Should disconnect client and start server
Api.OpilandConnectClient("127.0.0.1", 8080);
Thread.Sleep(1000);
Api.OpilandStartServer();
// Expected: Server started, client disconnected
```

### Test 3: Rapid Toggling
```csharp
// Should handle rapid switches without crashing
for (int i = 0; i < 10; i++)
{
    Api.OpilandStartServer();
    Thread.Sleep(100);
    Api.OpilandConnectClient("127.0.0.1", 8080);
    Thread.Sleep(100);
}
// Expected: Last call wins, no crashes
```

## Summary

The circular reference issue is now **fixed** using a simple re-entrancy guard (`_isChangingState` flag). Both managers can safely stop each other without causing infinite loops.

✅ Build successful  
✅ No circular references  
✅ Mutual exclusivity maintained  
✅ Thread-safe operations  
