# Pathfinding Analysis - LegionAPI

## Overview

The `Pathfind()` method in `LegionAPI.cs` is a high-level wrapper that uses a sophisticated **A\* (A-Star) pathfinding algorithm** implementation located in `Pathfinder.cs`. This document provides a detailed analysis of how it works and potential optimizations.

---

## How the Pathfind Method Works

### 1. **High-Level Flow (LegionAPI.cs)**

```csharp
public bool Pathfind(int x, int y, int z = int.MinValue, int distance = 1, 
                     WalkStyle walkStyle = WalkStyle.Auto, bool wait = false, double timeout = 10000)
```

**Steps:**
1. **Invoke pathfinding** on the main thread via `World.Player.Pathfinder.WalkTo()`
2. **Optional waiting**: If `wait=true`, polls until pathfinding completes or timeout (max 30 seconds)
3. **Fallback to long-distance pathfinder**: Checks `LongDistancePathfinder.IsPathfinding()` for large distances
4. **Result validation**: Returns `true` if within `distance` of target, `false` if timeout/failed

---

## Algorithm Details: A\* Pathfinding

### Core Components

#### **1. Data Structures**
```csharp
// Open set - Priority Queue (Min-Heap based on f-cost)
private static readonly PriorityQueue _openSet = new();

// Closed set - Visited nodes dictionary
private static readonly Dictionary<(int x, int y, int z), PathNode> _closedSet = new();

// Maximum nodes to explore (prevents infinite loops)
private const int PATHFINDER_MAX_NODES = 150000;
```

#### **2. PathNode Structure**
```csharp
class PathNode
{
    public int X, Y, Z;                     // Position
    public int Direction;                   // Facing direction
    public int Cost;                        // f = g + h (total cost)
    public int DistFromStartCost;           // g-cost (actual cost from start)
    public int DistFromGoalCost;            // h-cost (heuristic to goal)
    public PathNode Parent;                 // For path reconstruction
    public bool IsValid;                    // For lazy deletion
}
```

---

### A\* Algorithm Implementation

#### **Step 1: Initialization** (`FindPath()` - Line 871)
```csharp
var startNode = PathNode.Get();
startNode.X = _startPoint.X;
startNode.Y = _startPoint.Y;
startNode.Z = _world.Player.Z;
startNode.DistFromStartCost = 0;  // g = 0
startNode.DistFromGoalCost = GetGoalDistCost(startPoint, 0);  // h = heuristic
startNode.Cost = startNode.DistFromGoalCost;  // f = g + h
_openSet.Enqueue(startNode);
```

#### **Step 2: Main Loop** (Lines 891-921)
```csharp
while (AutoWalking && closedNodesCount < maxNodes)
{
    PathNode currentNode = FindCheapestNode();  // Dequeue node with lowest f-cost
    
    if (_goalNode != null) {
        ReconstructPath(_goalNode);  // Goal found!
        return true;
    }
    
    OpenNodes(currentNode);  // Explore neighbors
    closedNodesCount++;
}
```

#### **Step 3: Neighbor Exploration** (`OpenNodes()` - Line 773)
For each neighbor of current node:
1. Check if tile is **walkable** via `CanWalk()` and `CalculateNewZ()`
2. Calculate costs:
   - **g-cost**: `parent.g + moveCost + |z_diff| + turnPenalty`
   - **h-cost**: Chebyshev distance `max(|dx|, |dy|)` (Line 722)
   - **f-cost**: `g + h`
3. Add to open set if not in closed set

```csharp
private bool AddNodeToList(int direction, int x, int y, int z, PathNode parent, int cost)
{
    int turnPenalty = GetTurnPenalty(parent, direction);  // +1 if direction changed
    int newDistFromStart = parent.DistFromStartCost + cost + Math.Abs(z - parent.Z) + turnPenalty;
    
    updatedNode.DistFromStartCost = newDistFromStart;  // g
    updatedNode.DistFromGoalCost = GetGoalDistCost(new Point(x, y), cost);  // h
    updatedNode.Cost = updatedNode.DistFromStartCost + updatedNode.DistFromGoalCost;  // f
    
    _openSet.Enqueue(updatedNode);
}
```

#### **Step 4: Heuristic Function** (Line 720-722)
```csharp
private int GetGoalDistCost(Point point, int cost) =>
    Math.Max(Math.Abs(_endPoint.X - point.X), Math.Abs(_endPoint.Y - point.Y));
```
- Uses **Chebyshev distance** (diagonal movement allowed)
- **Admissible**: Never overestimates actual cost ✅
- **Consistent**: Satisfies triangle inequality ✅

---

### Priority Queue Implementation

**Custom Min-Heap** with lazy deletion (Lines 1205-1360):

```csharp
class PriorityQueue
{
    List<PathNode> _heap = new();  // Binary min-heap
    Dictionary<(int, int, int), PathNode> _lookup = new();  // Fast duplicate check
    
    void HeapifyUp(int index)  // O(log n) - bubble up lower cost nodes
    void HeapifyDown(int index)  // O(log n) - bubble down higher cost nodes
    
    void Enqueue(PathNode node)  // O(log n) insertion
    {
        if (existing node has lower/equal cost)
            return;  // Ignore this node
        
        existing.IsValid = false;  // Mark for lazy deletion
        _heap.Add(node);
        HeapifyUp(_heap.Count - 1);
    }
    
    PathNode Dequeue()  // O(log n) extraction
    {
        while (!top.IsValid)  // Skip invalidated nodes
            RemoveAt(0);
        return top;
    }
}
```

**Key Optimizations:**
- **Lazy deletion**: Instead of immediately removing worse duplicates, marks them as invalid
- **O(1) duplicate checking**: Uses dictionary lookup instead of scanning heap
- **Early termination**: Reuses nodes already in open set if cost is better

---

### Movement Validation

#### **Walkability Check** (`CanWalk()` - Lines 576-718)
For each movement:
1. **Collision detection**: Checks all objects at target tile (land, statics, items, multis)
2. **Z-axis validation**: Ensures step height difference < 16 units
3. **Special states**: Handles flying, mounted (seahorse), dead/GM mode
4. **Diagonal blocking**: Prevents cutting corners through impassable tiles

#### **Object Flags** (Lines 145-375)
```csharp
POF_IMPASSABLE_OR_SURFACE  // Walls, blocking objects
POF_BRIDGE                 // Bridges (special Z handling)
POF_NO_DIAGONAL            // Prevents diagonal movement
```

---

## Performance Characteristics

### Time Complexity
- **Best case**: O(n) where n = straight-line distance
- **Average case**: O(n log n) for typical open terrain
- **Worst case**: O(V log V) where V = PATHFINDER_MAX_NODES (150,000)
  - Each node: O(8) neighbor checks × O(log n) heap operations
  - Actual: **O(150,000 × 8 × log 150,000) ≈ 17 million operations**

### Space Complexity
- **Open set**: O(V) - up to 150,000 nodes in heap
- **Closed set**: O(V) - dictionary of visited nodes
- **Total memory**: ~150,000 × 64 bytes ≈ **9.6 MB** per pathfinding operation

---

## Identified Bottlenecks

### 1. **Excessive Node Exploration**
```csharp
private const int PATHFINDER_MAX_NODES = 150000;  // Too high!
```
- Allows exploring 150,000 nodes before giving up
- For local pathfinding (< 50 tiles), this is overkill

### 2. **Walkability Checks are Expensive**
```csharp
private bool CreateItemList(List<PathObject> list, int x, int y, int stepState)
{
    // Iterates through ALL objects at tile (land, statics, items, multis)
    // Called 8 times per node (once per direction)
    // = 8 × 150,000 = 1.2 million tile checks worst case
}
```

### 3. **No Early Termination Optimizations**
- No **bidirectional search** (search from both start and goal)
- No **jump point search** optimization for open areas
- No **distance cutoff** (continues even if goal is very far)

### 4. **Lazy Deletion Overhead**
```csharp
while (!top.IsValid)  // O(n) worst case to find valid node
    RemoveAt(0);
```
- Can accumulate many invalid nodes in heap
- Wastes memory and CPU time

### 5. **Turn Penalty Calculation**
```csharp
int turnPenalty = GetTurnPenalty(parent, direction);  // +1 per turn
```
- Prevents natural movement but adds overhead
- Evaluated for every neighbor (8 times per node)

---

## Optimization Recommendations

### 🚀 **High Impact**

#### 1. **Adaptive Node Limit** (Easy - High Impact)
```csharp
// Current: Fixed 150,000 nodes
private const int PATHFINDER_MAX_NODES = 150000;

// Optimized: Scale with distance
private int GetMaxNodes(int distance)
{
    return distance <= 10 ? 5000 :     // Local: 5k nodes
           distance <= 50 ? 25000 :    // Medium: 25k nodes
           150000;                     // Long: 150k nodes
}
```
**Expected Gain**: 30-70% faster for short paths

#### 2. **Walkability Caching** (Medium - High Impact)
```csharp
// Cache walkability results for frequently checked tiles
private Dictionary<(int x, int y, int z, int dir), bool> _walkabilityCache = new();

private bool CanWalkCached(int x, int y, int z, int dir)
{
    var key = (x, y, z, dir);
    if (_walkabilityCache.TryGetValue(key, out bool result))
        return result;
    
    result = CanWalk(x, y, z, dir);
    _walkabilityCache[key] = result;
    return result;
}
```
**Expected Gain**: 20-40% faster (reduces 1.2M calls to ~300K unique checks)

#### 3. **Early Distance Cutoff** (Easy - Medium Impact)
```csharp
// In FindPath loop:
if (currentNode.DistFromGoalCost > MAX_REASONABLE_DISTANCE)
{
    // Goal too far, use LongDistancePathfinder instead
    return false;
}
```
**Expected Gain**: Prevents wasted work on impossible paths

### ⚡ **Medium Impact**

#### 4. **Bidirectional A\*** (Hard - Medium Impact)
Search simultaneously from start and goal, meeting in the middle:
```csharp
// Two separate A* searches
var forwardSearch = new AStarSearch(start, goal);
var backwardSearch = new AStarSearch(goal, start);

// Alternate between them
while (true)
{
    forwardSearch.ExpandNode();
    backwardSearch.ExpandNode();
    
    if (forwardSearch.ClosedSet.Intersects(backwardSearch.ClosedSet))
        return JoinPaths(forwardSearch, backwardSearch);
}
```
**Expected Gain**: 30-50% faster for medium-long paths

#### 5. **Jump Point Search (JPS)** (Hard - High Impact for open terrain)
Skip straight-line nodes in open areas:
```csharp
// Instead of exploring every tile in a straight line:
//   Start -> Tile1 -> Tile2 -> Tile3 -> ... -> Goal
// Jump directly to "forced neighbors":
//   Start -> JumpPoint1 -> JumpPoint2 -> Goal
```
**Expected Gain**: 2-10x faster in open areas, 0% in tight corridors

#### 6. **Improved Heuristic** (Easy - Low-Medium Impact)
```csharp
// Current: Chebyshev distance
Math.Max(Math.Abs(_endPoint.X - point.X), Math.Abs(_endPoint.Y - point.Y));

// Optimized: Octile distance (more accurate for diagonal movement)
private int GetGoalDistCost(Point point)
{
    int dx = Math.Abs(_endPoint.X - point.X);
    int dy = Math.Abs(_endPoint.Y - point.Y);
    return Math.Max(dx, dy) + (int)(0.41 * Math.Min(dx, dy));  // 1.41 ≈ √2 for diagonals
}
```
**Expected Gain**: 5-15% fewer nodes explored

### 🔧 **Low Impact (Code Quality)**

#### 7. **Remove Lazy Deletion** (Medium - Low Impact)
```csharp
// Instead of marking nodes invalid, immediately remove duplicates
// Trade: Slower Enqueue() but faster Dequeue()
```
**Expected Gain**: 5-10% cleaner, potentially faster for dense graphs

#### 8. **Object Pooling Optimization**
```csharp
// Current: Already uses object pooling for PathNode
// Optimize: Pool PathObject instances too

private static readonly ObjectPool<PathObject> _pathObjectPool = new ObjectPool<PathObject>(
    () => new PathObject(),
    obj => obj.Reset(),
    1000
);
```
**Expected Gain**: Reduces GC pressure, 5-10% faster

---

## Comparison with LongDistancePathfinder

For paths > ~50 tiles, the code switches to `LongDistancePathfinder`:
- Uses **map chunks** instead of individual tiles
- **Lower resolution** but much faster for long distances
- **Hierarchical approach**: Find chunk path, then detailed path within chunks

---

## Summary

### Algorithm: **A\* with Chebyshev Heuristic**
✅ **Optimal**: Finds shortest path  
✅ **Complete**: Will find a path if one exists  
✅ **Efficient**: Better than Dijkstra's for single-target search  

### Current Performance
- **Good for**: Short paths (< 20 tiles)
- **Acceptable for**: Medium paths (20-50 tiles)
- **Poor for**: Long paths (> 50 tiles) - should use LongDistancePathfinder

### Recommended Priority
1. **Adaptive node limit** (easy, high impact)
2. **Walkability caching** (medium, high impact)
3. **Early distance cutoff** (easy, medium impact)
4. **Improved heuristic** (easy, low-medium impact)
5. **Bidirectional A\*** (hard, medium impact - for later)
6. **Jump Point Search** (hard, high impact in open terrain - advanced)

### Expected Overall Improvement
Implementing recommendations 1-4: **40-60% faster** for typical gameplay scenarios with minimal code changes.
