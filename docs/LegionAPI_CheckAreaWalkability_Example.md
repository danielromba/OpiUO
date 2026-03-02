# Fast Area Walkability Checking

## Overview

The `CheckAreaWalkability()` method allows you to efficiently check the passability of multiple tiles at once. This is highly optimized for performance and uses cached data whenever possible.

## Performance Features

- ✅ **Struct-based results** - No heap allocations for maximum speed
- ✅ **Cached data** - Uses pre-calculated walkability data when available
- ✅ **Batch processing** - Single method call checks entire area
- ✅ **Minimal overhead** - Optimized for real-time pathfinding

## Basic Usage

```python
import API

# Check a 10x10 area around player
player_x = API.Player.X
player_y = API.Player.Y

results = API.CheckAreaWalkability(
    player_x - 5, player_y - 5,  # Start coordinates
    player_x + 5, player_y + 5   # End coordinates
)

# Count walkable tiles
walkable_count = 0
for result in results:
    if result.IsWalkable:
        walkable_count += 1

API.SysMsg(f"Found {walkable_count} walkable tiles out of {len(results)}")
```

## Advanced Example: Finding Nearest Walkable Tile

```python
import API
import math

def find_nearest_walkable_to_target(target_x, target_y, search_radius=10):
    """
    Find the nearest walkable tile to a target position.
    Uses efficient area checking for best performance.
    """
    # Check entire area at once
    results = API.CheckAreaWalkability(
        target_x - search_radius, target_y - search_radius,
        target_x + search_radius, target_y + search_radius
    )
    
    nearest_tile = None
    nearest_distance = float('inf')
    
    # Find nearest walkable tile
    for result in results:
        if not result.IsWalkable:
            continue
            
        # Calculate distance to target
        dx = result.X - target_x
        dy = result.Y - target_y
        distance = math.sqrt(dx*dx + dy*dy)
        
        if distance < nearest_distance:
            nearest_distance = distance
            nearest_tile = (result.X, result.Y)
    
    return nearest_tile

# Usage
target_x = 1414
target_y = 1515

nearest = find_nearest_walkable_to_target(target_x, target_y, search_radius=15)
if nearest:
    API.SysMsg(f"Nearest walkable tile: {nearest[0]}, {nearest[1]}")
    API.Pathfind(nearest[0], nearest[1])
else:
    API.SysMsg("No walkable tiles found in search area!")
```

## Example: Visual Debugging

```python
import API

def visualize_walkable_area(center_x, center_y, radius=8):
    """
    Visually mark walkable and blocked tiles on the map.
    Green = walkable, Red = blocked
    """
    # Clear previous markers
    API.ClearMarkedTiles()
    
    # Check area
    results = API.CheckAreaWalkability(
        center_x - radius, center_y - radius,
        center_x + radius, center_y + radius
    )
    
    walkable = 0
    blocked = 0
    
    # Mark tiles with colors
    for result in results:
        if result.IsWalkable:
            API.MarkTile(result.X, result.Y, 66)  # Green
            walkable += 1
        else:
            API.MarkTile(result.X, result.Y, 33)  # Red
            blocked += 1
    
    API.SysMsg(f"Walkable: {walkable}, Blocked: {blocked}")

# Visualize area around player
visualize_walkable_area(API.Player.X, API.Player.Y, radius=10)
```

## Example: Mining Resource Pathfinding

```python
import API

def find_best_mining_spot(ore_locations, max_distance=15):
    """
    Find the best walkable position near ore veins.
    Checks all potential mining spots efficiently.
    """
    best_positions = []
    
    for ore_x, ore_y in ore_locations:
        # Check 5x5 area around ore
        results = API.CheckAreaWalkability(
            ore_x - 2, ore_y - 2,
            ore_x + 2, ore_y + 2
        )
        
        # Find walkable tiles adjacent to ore
        for result in results:
            if not result.IsWalkable:
                continue
                
            # Check if tile is within mining range (2 tiles)
            dx = abs(result.X - ore_x)
            dy = abs(result.Y - ore_y)
            
            if dx <= 2 and dy <= 2:
                distance_to_player = abs(result.X - API.Player.X) + abs(result.Y - API.Player.Y)
                if distance_to_player <= max_distance:
                    best_positions.append({
                        'x': result.X,
                        'y': result.Y,
                        'ore_x': ore_x,
                        'ore_y': ore_y,
                        'distance': distance_to_player
                    })
    
    # Sort by distance
    best_positions.sort(key=lambda p: p['distance'])
    return best_positions

# Example ore locations (replace with actual ore finding logic)
ore_veins = [(1400, 1500), (1410, 1505), (1420, 1510)]

spots = find_best_mining_spot(ore_veins)
if spots:
    best = spots[0]
    API.SysMsg(f"Moving to mining spot near ore at {best['ore_x']}, {best['ore_y']}")
    API.Pathfind(best['x'], best['y'])
```

## Example: Safe Zone Detection

```python
import API

def find_safe_zone(center_x, center_y, required_radius=5):
    """
    Find a safe area where all surrounding tiles are walkable.
    Useful for setting up camps, etc.
    """
    search_radius = 20
    
    # Check large area
    results = API.CheckAreaWalkability(
        center_x - search_radius, center_y - search_radius,
        center_x + search_radius, center_y + search_radius
    )
    
    # Create lookup dict for fast access
    walkable_map = {}
    for result in results:
        walkable_map[(result.X, result.Y)] = result.IsWalkable
    
    # Check each potential center point
    safe_zones = []
    
    for check_x in range(center_x - search_radius + required_radius, 
                        center_x + search_radius - required_radius):
        for check_y in range(center_y - search_radius + required_radius,
                            center_y + search_radius - required_radius):
            
            # Check if all tiles in radius are walkable
            all_walkable = True
            for dx in range(-required_radius, required_radius + 1):
                for dy in range(-required_radius, required_radius + 1):
                    pos = (check_x + dx, check_y + dy)
                    if not walkable_map.get(pos, False):
                        all_walkable = False
                        break
                if not all_walkable:
                    break
            
            if all_walkable:
                distance = abs(check_x - API.Player.X) + abs(check_y - API.Player.Y)
                safe_zones.append((check_x, check_y, distance))
    
    # Sort by distance
    safe_zones.sort(key=lambda z: z[2])
    return safe_zones

# Find safe zones
zones = find_safe_zone(API.Player.X, API.Player.Y, required_radius=3)
if zones:
    best_zone = zones[0]
    API.SysMsg(f"Found safe zone at {best_zone[0]}, {best_zone[1]}")
    API.Pathfind(best_zone[0], best_zone[1])
else:
    API.SysMsg("No safe zones found!")
```

## Performance Tips

1. **Batch Your Checks**: Always prefer one large area check over many small checks
   ```python
   # GOOD - Single batch check
   results = API.CheckAreaWalkability(x1, y1, x2, y2)
   
   # BAD - Multiple individual checks
   for x in range(x1, x2):
       for y in range(y1, y2):
           walkable = API.IsWalkable(x, y)  # Slow!
   ```

2. **Use Appropriate Area Sizes**: 
   - Small areas (< 20x20): Very fast, use freely
   - Medium areas (20x20 to 50x50): Still fast, good for most uses
   - Large areas (> 50x50): May have noticeable delay, use sparingly

3. **Cache Results**: If checking the same area multiple times, store results
   ```python
   # Cache results for reuse
   cached_results = API.CheckAreaWalkability(x1, y1, x2, y2)
   walkable_positions = [(r.X, r.Y) for r in cached_results if r.IsWalkable]
   ```

## Result Structure

Each result in the returned list contains:
- `X` (int): X coordinate
- `Y` (int): Y coordinate  
- `IsWalkable` (bool): True if tile is walkable, False if blocked

## Comparison to Individual Checks

```python
import API
import time

# Method 1: Individual checks (SLOW)
start = time.time()
count = 0
for x in range(API.Player.X - 10, API.Player.X + 10):
    for y in range(API.Player.Y - 10, API.Player.Y + 10):
        if API.IsWalkable(x, y):
            count += 1
elapsed1 = time.time() - start

# Method 2: Batch check (FAST)
start = time.time()
results = API.CheckAreaWalkability(
    API.Player.X - 10, API.Player.Y - 10,
    API.Player.X + 10, API.Player.Y + 10
)
count2 = sum(1 for r in results if r.IsWalkable)
elapsed2 = time.time() - start

API.SysMsg(f"Individual: {elapsed1:.3f}s, Batch: {elapsed2:.3f}s")
API.SysMsg(f"Batch is {elapsed1/elapsed2:.1f}x faster!")
```

## See Also

- `IsWalkable(x, y)` - Check single tile walkability
- `IsTileWalkable(x, y, z)` - Check with specific Z elevation
- `MarkTile(x, y, hue)` - Visually mark tiles for debugging
- `ClearMarkedTiles()` - Clear tile markers
