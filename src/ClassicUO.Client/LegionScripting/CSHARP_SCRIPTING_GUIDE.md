# C# Legion Scripting Guide

## Overview
Legion scripts in TazUO now use a class-based approach with the `ILegionScript` interface. This provides better IntelliSense support, proper namespaces, and a more structured way to write scripts.

## Requirements

Every C# script must:
1. Be in the `ClassicUO.LegionScripting.Scripts` namespace
2. Implement the `ILegionScript` interface
3. Provide a `Name` property (string)
4. Implement an `Execute` method that accepts `LegionAPI` and `CancellationToken` parameters

##  Basic Script Structure

```csharp
using System;
using System.Threading;
using ClassicUO.LegionScripting;

namespace ClassicUO.LegionScripting.Scripts;

public class MyScript : ILegionScript
{
    public string Name => "My Script Name";

    public void Execute(LegionAPI api, CancellationToken cancellationToken)
    {
        // Your script code here
        api.SysMsg("Script started!");

        // Check cancellation token in loops
        while (!cancellationToken.IsCancellationRequested)
        {
            // Do work...
            Thread.Sleep(1000);
        }

        api.SysMsg("Script stopped!");
    }
}
```

## Important Notes

### Cancellation
Always check `cancellationToken.IsCancellationRequested` in loops to allow the script to be stopped gracefully:

```csharp
while (!cancellationToken.IsCancellationRequested)
{
    // Your code
    if (someCondition)
        break;
        
    Thread.Sleep(100);
}
```

### API Access
The `api` parameter gives you access to all Legion API methods:
- `api.SysMsg("message")` - Display a system message
- `api.Player` - Access player information  
- `api.FindItemsById(...)` - Find items
- And many more - see the LegionAPI class for full documentation

### IntelliSense Support
The included `LegionScripts.csproj` file provides IntelliSense when editing scripts in Visual Studio or VS Code. Build errors in this project can be ignored - scripts are compiled individually by TazUO at runtime.

### Namespace Requirement
All scripts **must** be in the `ClassicUO.LegionScripting.Scripts` namespace. Scripts in other namespaces will not be recognized.

### File Naming
- Script files should end with `.cs`
- Files starting with `_` or named `API.py` are ignored
- Files ending with `.template.cs` are also ignored

## Example Scripts

### Simple Message Script
```csharp
using System.Threading;
using ClassicUO.LegionScripting;

namespace ClassicUO.LegionScripting.Scripts;

public class HelloWorld : ILegionScript
{
    public string Name => "Hello World";

    public void Execute(LegionAPI api, CancellationToken cancellationToken)
    {
        api.SysMsg($"Hello, {api.Player.Name}!");
    }
}
```

### Looping Script with Cancellation
```csharp
using System;
using System.Threading;
using ClassicUO.LegionScripting;

namespace ClassicUO.LegionScripting.Scripts;

public class ResourceGatherer : ILegionScript
{
    public string Name => "Resource Gatherer";

    public void Execute(LegionAPI api, CancellationToken cancellationToken)
    {
        api.SysMsg("Starting resource gathering...");
        
        int cycles = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // Check if player is alive
            if (api.Player.IsDead)
            {
                api.SysMsg("Player is dead, stopping script.");
                break;
            }

            // Do work
            cycles++;
            api.SysMsg($"Cycle {cycles} complete");

            // Sleep between cycles
            Thread.Sleep(5000);

            // Stop after 10 cycles
            if (cycles >= 10)
            {
                api.SysMsg("Completed 10 cycles, stopping.");
                break;
            }
        }

        api.SysMsg($"Script stopped after {cycles} cycles.");
    }
}
```

## Differences from Python Scripts

- **No top-level statements**: Everything must be inside a class
- **Explicit types**: C# requires type declarations (but `var` can be used)
- **Namespace required**: Must use `ClassicUO.LegionScripting.Scripts`  
- **Thread.Sleep instead of Pause**: Use `Thread.Sleep(milliseconds)` for delays
- **Compilation**: Scripts are compiled on first run and cached

## Troubleshooting

### "Script does not contain a class that implements ILegionScript"
- Make sure your class implements `ILegionScript`
- Verify you're using the correct namespace: `ClassicUO.LegionScripting.Scripts`
- Check that your class is public

### Compilation Errors
- Check the error window in-game for detailed error messages
- Ensure all using statements are correct
- Verify syntax is valid C#

### Script Won't Stop
- Make sure you're checking `cancellationToken.IsCancellationRequested` in all loops
- Avoid infinite loops without cancellation checks
- Don't use `while(true)` without a break condition or cancellation check
