// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using static ClassicUO.LegionScripting.LegionAPI;
using MathHelper = ClassicUO.Utility.MathHelper;

namespace ClassicUO.Game
{
    public sealed class Pathfinder
    {
        private const int PATHFINDER_MAX_NODES = 150000;
        private const int MAX_REASONABLE_DISTANCE = 100; // Optimization 3: Early cutoff threshold
        private static PathNode _goalNode;
        private static int _pathfindDistance;
        private static readonly PriorityQueue _openSet = new();
        private static readonly Dictionary<(int x, int y, int z), PathNode> _closedSet = new();
        private static readonly Dictionary<(int x, int y, int z, int dir), bool> _walkabilityCache = new(); // Optimization 2: Walkability cache
        private static readonly List<PathNode> _path = new();
        private static int _pointIndex;
        private static bool _run;
        private static WalkStyle _walkStyle;
        private static readonly int[] _offsetX =
        {
            0, 1, 1, 1, 0, -1, -1, -1, 0, 1
        };
        private static readonly int[] _offsetY =
        {
            -1, -1, 0, 1, 1, 1, 0, -1, -1, -1
        };
        private static readonly sbyte[] _dirOffset =
        {
            1, -1
        };
        private Point _startPoint, _endPoint;

        private static int _endPointZ;
        private static readonly List<PathObject> _reusableList = new();
        private static uint _followTargetSerial; // Track follow target to exclude from obstacles

        // Path caching to avoid recalculating paths to the same destination
        private struct PathCacheKey
        {
            public int StartX, StartY, StartZ;
            public int EndX, EndY, EndZ;
            public int Distance;
            public WalkStyle WalkStyle;
            public uint FollowTarget;
        }

        private struct PathCacheEntry
        {
            public List<(int X, int Y, int Z, int Direction)> Steps;
            public DateTime CachedTime;
            public bool IsRunning;
        }

        private static PathCacheKey? _lastPathKey;
        private static PathCacheEntry? _lastPathEntry;
        private const double CACHE_VALIDITY_SECONDS = 30.0; // Cache paths for 30 seconds
        private const int CACHE_START_TOLERANCE = 2; // Allow 2 tile difference in start position

        public Point StartPoint => _startPoint;
        public Point EndPoint => _endPoint;
        public int PathSize => _path.Count;

        public bool AutoWalking { get; set; }

        public static bool PathFindingCanBeCancelled { get; set; }

        public static bool FastRotation { get; set; }

        public bool BlockMoving { get; set; }

        private World _world;

        public bool UseLongDistancePathfinding;

        public Pathfinder(World world)
        {
            _world = world;
            _ = Client.Settings.GetAsyncOnMainThread(SettingsScope.Global, Constants.SqlSettings.USE_LONG_DISTANCE_PATHING, false, (b) => UseLongDistancePathfinding = b);
        }

        public static bool ObjectBlocksLOS(GameObject obj, int losMinZ, int losMaxZ)
        {
            int objZ = obj.Z;
            int objHeight = 0;
            bool isBlocker = false;

            switch (obj)
            {
                case Land land:
                    objHeight = 1;
                    isBlocker = land.TileData.IsImpassable;
                    break;
                case Static s:
                    ref StaticTiles staticData = ref Client.Game.UO.FileManager.TileData.StaticData[s.OriginalGraphic];
                    objHeight = staticData.Height;
                    isBlocker = staticData.IsImpassable || staticData.IsWall;
                    break;
                case Item i:
                    objHeight = i.ItemData.Height;
                    isBlocker = i.ItemData.IsImpassable;
                    break;
                case Multi m:
                    objHeight = m.ItemData.Height;
                    isBlocker = m.ItemData.IsImpassable;
                    break;
                default:
                    return false;
            }

            if (!isBlocker)
                return false;

            int objTop = objZ + objHeight;

            int losMin = Math.Min(losMinZ, losMaxZ);
            int losMax = Math.Max(losMinZ, losMaxZ);

            if (objTop > losMin && objZ < losMax)
                return true;

            return false;
        }

        public static readonly ObjectPool<List<GameObject>> _listPool = new ObjectPool<List<GameObject>>(
            () => new List<GameObject>(),
            list => list.Clear(),
            100
        );

        public static List<GameObject> GetAllObjectsAt(int x, int y)
        {
            List<GameObject> result = _listPool.Get();
            GameObject tile = Client.Game.UO.World.Map.GetTile(x, y, false);
            if (tile == null)
                return result;

            GameObject obj = tile;
            while (obj.TPrevious != null)
                obj = obj.TPrevious;
            for (; obj != null; obj = obj.TNext)
                result.Add(obj);

            return result;
        }

        private bool CreateItemList(List<PathObject> list, int x, int y, int stepState)
        {
            GameObject tile = _world.Map.GetTile(x, y, false);

            if (tile == null)
            {
                return false;
            }

            bool ignoreGameCharacters = ProfileManager.CurrentProfile.IgnoreStaminaCheck || stepState == (int)PATH_STEP_STATE.PSS_DEAD_OR_GM || _world.Player.IgnoreCharacters || !(_world.Player.Stamina < _world.Player.StaminaMax && _world.Map.Index == 0);

            bool isGM = _world.Player.Graphic == 0x03DB;

            GameObject obj = tile;

            while (obj.TPrevious != null)
            {
                obj = obj.TPrevious;
            }

            for (; obj != null; obj = obj.TNext)
            {
                if (_world.CustomHouseManager != null && obj.Z < _world.Player.Z)
                {
                    continue;
                }

                ushort graphicHelper = obj.Graphic;

                switch (obj)
                {
                    case Land tile1:

                        if (graphicHelper < 0x01AE && graphicHelper != 2 || graphicHelper > 0x01B5 && graphicHelper != 0x01DB)
                        {
                            uint flags = (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE;

                            if (stepState == (int)PATH_STEP_STATE.PSS_ON_SEA_HORSE)
                            {
                                if (tile1.TileData.IsWet)
                                {
                                    flags = (uint)(PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE | PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                }
                            }
                            else
                            {
                                if (!tile1.TileData.IsImpassable)
                                {
                                    flags = (uint)(PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE | PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                }

                                if (stepState == (int)PATH_STEP_STATE.PSS_FLYING && tile1.TileData.IsNoDiagonal)
                                {
                                    flags |= (uint)PATH_OBJECT_FLAGS.POF_NO_DIAGONAL;
                                }
                            }

                            int landMinZ = tile1.MinZ;
                            int landAverageZ = tile1.AverageZ;
                            int landHeight = landAverageZ - landMinZ;

                            // TODO: Investigate reducing PathObject allocations here and below
                            list.Add
                            (
                                PathObject.Get
                                (
                                    flags,
                                    landMinZ,
                                    landAverageZ,
                                    landHeight,
                                    obj
                                )
                            );
                        }

                        break;

                    case GameEffect _: break;

                    default:
                        bool canBeAdd = true;
                        bool dropFlags = false;

                        switch (obj)
                        {
                            case Mobile mobile:
                                {
                                    Log.Trace($"[Pathfinder] CreateItemList: Found mobile {mobile.Serial:X8} at ({x},{y}), FollowTarget={_followTargetSerial:X8}");

                                    // Don't treat the follow target as an obstacle!
                                    // This allows diagonal movement when following
                                    if (mobile.Serial == _followTargetSerial)
                                    {
                                        Log.Trace($"[Pathfinder] >>> EXCLUDING follow target {mobile.Serial:X8} from obstacles");
                                        canBeAdd = false;
                                        break;
                                    }

                                    if (!ignoreGameCharacters && !mobile.IsDead && !mobile.IgnoreCharacters)
                                    {
                                        Log.Trace($"[Pathfinder] >>> ADDING mobile {mobile.Serial:X8} as obstacle (not follow target)");
                                        list.Add
                                        (
                                            PathObject.Get
                                            (
                                                (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE,
                                                mobile.Z,
                                                mobile.Z + Constants.DEFAULT_CHARACTER_HEIGHT,
                                                Constants.DEFAULT_CHARACTER_HEIGHT,
                                                mobile
                                            )
                                        );
                                    }
                                    else
                                    {
                                        Log.Trace($"[Pathfinder] >>> IGNORING mobile {mobile.Serial:X8} (ignoreGameCharacters={ignoreGameCharacters}, IsDead={mobile.IsDead}, IgnoreCharacters={mobile.IgnoreCharacters})");
                                    }

                                    canBeAdd = false;

                                    break;
                                }

                            case Item item when item.IsMulti || item.ItemData.IsInternal:
                                {
                                    //canBeAdd = false;

                                    break;
                                }

                            case Item item2:
                                if (stepState == (int)PATH_STEP_STATE.PSS_DEAD_OR_GM && (item2.ItemData.IsDoor || item2.ItemData.Weight <= 0x5A || isGM && !item2.IsLocked))
                                {
                                    dropFlags = true;
                                }
                                else if (ProfileManager.CurrentProfile.SmoothDoors && item2.ItemData.IsDoor)
                                {
                                    dropFlags = true;
                                }
                                else
                                {
                                    dropFlags = graphicHelper >= 0x3946 && graphicHelper <= 0x3964 || graphicHelper == 0x0082;
                                }

                                break;

                            case Multi m:

                                if ((_world.CustomHouseManager != null && m.IsCustom && (m.State & CUSTOM_HOUSE_MULTI_OBJECT_FLAGS.CHMOF_GENERIC_INTERNAL) == 0) || m.IsHousePreview)
                                {
                                    canBeAdd = false;
                                }

                                if ((m.State & CUSTOM_HOUSE_MULTI_OBJECT_FLAGS.CHMOF_IGNORE_IN_RENDER) != 0)
                                {
                                    dropFlags = true;
                                }

                                break;
                        }

                        if (canBeAdd)
                        {
                            uint flags = 0;

                            if (!(obj is Mobile))
                            {
                                ushort graphic = obj is Item it && it.IsMulti ? it.MultiGraphic : obj.Graphic;
                                ref StaticTiles itemdata = ref Client.Game.UO.FileManager.TileData.StaticData[graphic];

                                if (stepState == (int)PATH_STEP_STATE.PSS_ON_SEA_HORSE)
                                {
                                    if (itemdata.IsWet)
                                    {
                                        flags = (uint)(PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE);
                                    }
                                }
                                else
                                {
                                    if (itemdata.IsImpassable || itemdata.IsSurface)
                                    {
                                        flags = (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE;
                                    }

                                    if (!itemdata.IsImpassable)
                                    {
                                        if (itemdata.IsSurface)
                                        {
                                            flags |= (uint)PATH_OBJECT_FLAGS.POF_SURFACE;
                                        }

                                        if (itemdata.IsBridge)
                                        {
                                            flags |= (uint)PATH_OBJECT_FLAGS.POF_BRIDGE;
                                        }
                                    }

                                    if (stepState == (int)PATH_STEP_STATE.PSS_DEAD_OR_GM)
                                    {
                                        if (graphicHelper <= 0x0846)
                                        {
                                            if (!(graphicHelper != 0x0846 && graphicHelper != 0x0692 && (graphicHelper <= 0x06F4 || graphicHelper > 0x06F6)))
                                            {
                                                dropFlags = true;
                                            }
                                        }
                                        else if (graphicHelper == 0x0873)
                                        {
                                            dropFlags = true;
                                        }
                                    }

                                    if (dropFlags)
                                    {
                                        flags &= 0xFFFFFFFE;
                                    }

                                    if (stepState == (int)PATH_STEP_STATE.PSS_FLYING && itemdata.IsNoDiagonal)
                                    {
                                        flags |= (uint)PATH_OBJECT_FLAGS.POF_NO_DIAGONAL;
                                    }
                                }

                                if (flags != 0)
                                {
                                    int objZ = obj.Z;
                                    int staticHeight = itemdata.Height;
                                    int staticAverageZ = staticHeight;

                                    if (itemdata.IsBridge)
                                    {
                                        staticAverageZ /= 2;
                                        // revert fix from fwiffo because it causes unwalkable stairs [down --> up]
                                        //staticAverageZ += staticHeight % 2;
                                    }

                                    list.Add
                                    (
                                        PathObject.Get
                                        (
                                            flags,
                                            objZ,
                                            staticAverageZ + objZ,
                                            staticHeight,
                                            obj
                                        )
                                    );
                                }
                            }
                        }

                        break;
                }
            }

            return list.Count != 0;
        }

        private int CalculateMinMaxZ
        (
            ref int minZ,
            ref int maxZ,
            int newX,
            int newY,
            int currentZ,
            int newDirection,
            int stepState
        )
        {
            minZ = -128;
            maxZ = currentZ;
            newDirection &= 7;
            int direction = newDirection ^ 4;
            newX += _offsetX[direction];
            newY += _offsetY[direction];

            for (int i = 0; i < _reusableList.Count; i++)
            {
                _reusableList[i]?.Return();
            }

            _reusableList.Clear();

            if (!CreateItemList(_reusableList, newX, newY, stepState) || _reusableList.Count == 0)
            {
                return 0;
            }

            foreach (PathObject obj in _reusableList)
            {
                GameObject o = obj.Object;
                int averageZ = obj.AverageZ;

                if (averageZ <= currentZ && o is Land tile && tile.IsStretched)
                {
                    int avgZ = tile.CalculateCurrentAverageZ(newDirection);

                    if (minZ < avgZ)
                    {
                        minZ = avgZ;
                    }

                    if (maxZ < avgZ)
                    {
                        maxZ = avgZ;
                    }
                }
                else
                {
                    if ((obj.Flags & (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE) != 0 && averageZ <= currentZ && minZ < averageZ)
                    {
                        minZ = averageZ;
                    }

                    if ((obj.Flags & (uint)PATH_OBJECT_FLAGS.POF_BRIDGE) != 0 && currentZ == averageZ)
                    {
                        int z = obj.Z;
                        int height = z + obj.Height;

                        if (maxZ < height)
                        {
                            maxZ = height;
                        }

                        if (minZ > z)
                        {
                            minZ = z;
                        }
                    }
                }
            }

            maxZ += 2;

            return maxZ;
        }

        public bool CalculateNewZ(int x, int y, ref sbyte z, int direction)
        {
            int stepState = (int)PATH_STEP_STATE.PSS_NORMAL;

            if (_world.Player.IsDead || _world.Player.Graphic == 0x03DB)
            {
                stepState = (int)PATH_STEP_STATE.PSS_DEAD_OR_GM;
            }
            else
            {
                if (_world.Player.IsGargoyle && _world.Player.IsFlying)
                    stepState = (int)PATH_STEP_STATE.PSS_FLYING;
                else
                {
                    Item mount = _world.Player.FindItemByLayer(Layer.Mount);

                    if (mount != null && mount.Graphic == 0x3EB3) // sea horse
                        stepState = (int)PATH_STEP_STATE.PSS_ON_SEA_HORSE;
                }
            }

            int minZ = -128;
            int maxZ = z;

            CalculateMinMaxZ
            (
                ref minZ,
                ref maxZ,
                x,
                y,
                z,
                direction,
                stepState
            );

            foreach (PathObject o in _reusableList) o.Return();
            _reusableList.Clear();

            if (_world.CustomHouseManager != null)
            {
                var rect = new Rectangle(_world.CustomHouseManager.StartPos.X, _world.CustomHouseManager.StartPos.Y, _world.CustomHouseManager.EndPos.X, _world.CustomHouseManager.EndPos.Y);

                if (!rect.Contains(x, y))
                {
                    return false;
                }
            }

            if (!CreateItemList(_reusableList, x, y, stepState) || _reusableList.Count == 0)
            {
                return false;
            }

            _reusableList.Sort();

            var pathObj = PathObject.Get(
                (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE,
                128,
                128,
                128,
                null
            );

            if (pathObj != null)
                _reusableList.Add(pathObj);

            int resultZ = -128;

            if (z < minZ)
            {
                z = (sbyte)minZ;
            }

            int currentTempObjZ = 1000000;
            int currentZ = -128;

            for (int i = 0; i < _reusableList.Count; i++)
            {
                PathObject obj = _reusableList[i];

                if ((obj.Flags & (uint)PATH_OBJECT_FLAGS.POF_NO_DIAGONAL) != 0 && stepState == (int)PATH_STEP_STATE.PSS_FLYING)
                {
                    int objAverageZ = obj.AverageZ;
                    int delta = Math.Abs(objAverageZ - z);

                    if (delta <= 25)
                    {
                        resultZ = objAverageZ != -128 ? objAverageZ : currentZ;

                        break;
                    }
                }

                if ((obj.Flags & (uint)PATH_OBJECT_FLAGS.POF_IMPASSABLE_OR_SURFACE) != 0)
                {
                    int objZ = obj.Z;

                    if (objZ - minZ >= Constants.DEFAULT_BLOCK_HEIGHT)
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            PathObject tempObj = _reusableList[j];

                            if ((tempObj.Flags & (uint)(PATH_OBJECT_FLAGS.POF_SURFACE | PATH_OBJECT_FLAGS.POF_BRIDGE)) != 0)
                            {
                                int tempAverageZ = tempObj.AverageZ;

                                if (tempAverageZ >= currentZ && objZ - tempAverageZ >= Constants.DEFAULT_BLOCK_HEIGHT && (tempAverageZ <= maxZ && (tempObj.Flags & (uint)PATH_OBJECT_FLAGS.POF_SURFACE) != 0 || (tempObj.Flags & (uint)PATH_OBJECT_FLAGS.POF_BRIDGE) != 0 && tempObj.Z <= maxZ))
                                {
                                    int delta = Math.Abs(z - tempAverageZ);

                                    if (delta < currentTempObjZ)
                                    {
                                        currentTempObjZ = delta;
                                        resultZ = tempAverageZ;
                                    }
                                }
                            }
                        }
                    }

                    int averageZ = obj.AverageZ;

                    if (minZ < averageZ)
                    {
                        minZ = averageZ;
                    }

                    if (currentZ < averageZ)
                    {
                        currentZ = averageZ;
                    }
                }
            }

            z = (sbyte)resultZ;

            return resultZ != -128;
        }

        public static void GetNewXY(byte direction, ref int x, ref int y)
        {
            switch (direction & 7)
            {
                case 0:

                    {
                        y--;

                        break;
                    }

                case 1:

                    {
                        x++;
                        y--;

                        break;
                    }

                case 2:

                    {
                        x++;

                        break;
                    }

                case 3:

                    {
                        x++;
                        y++;

                        break;
                    }

                case 4:

                    {
                        y++;

                        break;
                    }

                case 5:

                    {
                        x--;
                        y++;

                        break;
                    }

                case 6:

                    {
                        x--;

                        break;
                    }

                case 7:

                    {
                        x--;
                        y--;

                        break;
                    }
            }
        }

        public bool CanWalk(ref Direction direction, ref int x, ref int y, ref sbyte z, bool dontChangeXY = false)
        {
            return CanWalk(ref direction, ref x, ref y, ref z, dontChangeXY, allowDiagonalForShortPath: false);
        }

        private bool CanWalk(ref Direction direction, ref int x, ref int y, ref sbyte z, bool dontChangeXY, bool allowDiagonalForShortPath)
        {
            int newX = x;
            int newY = y;
            sbyte newZ = z;
            byte newDirection = (byte)direction;
            if (!dontChangeXY) // if we dont want to change the xy, we can just use the current xy and direction
                GetNewXY((byte)direction, ref newX, ref newY);
            bool passed = CalculateNewZ(newX, newY, ref newZ, (byte)direction);

            if ((sbyte)direction % 2 != 0) // Diagonal movement
            {
                // For very short paths (1-2 tiles), skip the strict diagonal corner-cutting validation
                // This allows single diagonal steps without forcing 2 cardinal steps
                if (!allowDiagonalForShortPath)
                {
                    if (passed)
                    {
                        for (int i = 0; i < 2 && passed; i++)
                        {
                            int testX = x;
                            int testY = y;
                            sbyte testZ = z;
                            byte testDir = (byte)(((byte)direction + _dirOffset[i]) % 8);
                            GetNewXY(testDir, ref testX, ref testY);
                            passed = CalculateNewZ(testX, testY, ref testZ, testDir);
                        }
                    }

                    if (!passed)
                    {
                        for (int i = 0; i < 2 && !passed; i++)
                        {
                            newX = x;
                            newY = y;
                            newZ = z;
                            newDirection = (byte)(((byte)direction + _dirOffset[i]) % 8);
                            GetNewXY(newDirection, ref newX, ref newY);
                            passed = CalculateNewZ(newX, newY, ref newZ, newDirection);
                        }
                    }
                }
                // For short paths: passed remains as-is from CalculateNewZ,
                // skipping the adjacent tile checks that can block diagonal movement
            }

            if (passed)
            {
                x = newX;
                y = newY;
                z = newZ;
                direction = (Direction)newDirection;
            }

            return passed;
        }

        // Optimization 4: Improved heuristic using Octile distance
        // More accurate for diagonal movement than Chebyshev distance
        private int GetGoalDistCost(Point point, int cost)
        {
            int dx = Math.Abs(_endPoint.X - point.X);
            int dy = Math.Abs(_endPoint.Y - point.Y);
            int max = Math.Max(dx, dy);
            int min = Math.Min(dx, dy);

            // Octile distance: D * max(dx, dy) + (D2 - D) * min(dx, dy)
            // Where D = 1 (straight move cost) and D2 ≈ 1.41 (diagonal move cost)
            // Using more accurate integer math: max * 10 + min * 4, then divide by 10
            // This gives 0.4 which is closer to 0.414 than the previous (2/5)
            int octileDistance = (max * 10 + min * 4) / 10;

            // Add small tie-breaker to prefer paths closer to straight line
            // This helps prevent zig-zagging when multiple paths have same cost
            // Cross product gives us how far off the straight line we are
            int dx1 = point.X - _startPoint.X;
            int dy1 = point.Y - _startPoint.Y;
            int dx2 = _endPoint.X - _startPoint.X;
            int dy2 = _endPoint.Y - _startPoint.Y;
            int cross = Math.Abs(dx1 * dy2 - dx2 * dy1);
            int tieBreaker = cross / 1000; // Very small influence, just for tie-breaking

            return octileDistance + tieBreaker;
        }

        private static int GetTurnPenalty(PathNode parent, int direction)
        {
            // The turn penalty prevents unnecessary zig-zagging that takes extra
            // time (turning pauses movement briefly) and makes the movement look
            // more natural.
            if (parent.Parent == null || parent.Direction == direction)
                return 0;

            // Calculate turn angle to apply appropriate penalty
            int directionDiff = Math.Abs(parent.Direction - direction);
            if (directionDiff > 4) directionDiff = 8 - directionDiff; // Normalize to 0-4

            // For very short paths, use reduced penalties to allow quick direction changes
            // This is important for following where target moves diagonally
            int pathLength = _path.Count;
            bool useReducedPenalty = pathLength <= 2;

            if (useReducedPenalty)
            {
                // Reduced penalties for 1-2 step paths (following scenarios)
                return directionDiff switch
                {
                    0 => 0,  // Same direction
                    1 => 1,  // 45° turn (reduced from 2)
                    2 => 2,  // 90° turn (reduced from 3)
                    3 => 2,  // 135° turn (reduced from 4)
                    4 => 3,  // 180° turn (reduced from 5)
                    _ => 1   // Fallback
                };
            }

            // Normal penalties for longer paths:
            // - Same direction: 0
            // - 45° turn: 2 (diagonal to adjacent)
            // - 90° turn: 3
            // - 135° turn: 4
            // - 180° turn: 5 (complete reversal)
            return directionDiff switch
            {
                0 => 0,  // Same direction
                1 => 2,  // 45° turn
                2 => 3,  // 90° turn
                3 => 4,  // 135° turn
                4 => 5,  // 180° turn (reversal)
                _ => 2   // Fallback
            };
        }

        private bool AddNodeToList(int direction, int x, int y, int z, PathNode parent, int cost)
        {
            (int x, int y, int z) coordinate = (x, y, z);
            if (_closedSet.ContainsKey(coordinate))
            {
                return false;
            }

            // In terms of the distance (number of tile steps) of the final
            // reconstructed path, simply adding a turn penalty in this manner
            // will result in paths no worse than without the turn cost. However,
            // to guarantee that the minimal number of turns are taken, the open
            // and closed sets would need to key off of (x, y, z, direction) to
            // account for arriving at a tile from every direction. Doing this would
            // be up to an 8x increase in time and space, and this would only be
            // a problem where the cost of one step varies between tiles e.g. if
            // walking through mud tiles cost 2 instead of 1, but that's not a
            // concern here to warrent the 8x cost.
            int turnPenalty = GetTurnPenalty(parent, direction);
            int zDifferenceCost = Math.Abs(z - parent.Z);
            int newDistFromStart = parent.DistFromStartCost + cost + zDifferenceCost + turnPenalty;

            var updatedNode = PathNode.Get();

            updatedNode.X = x;
            updatedNode.Y = y;
            updatedNode.Z = z;
            updatedNode.Direction = direction;
            updatedNode.Parent = parent;
            updatedNode.DistFromStartCost = newDistFromStart;
            updatedNode.DistFromGoalCost = GetGoalDistCost(new Point(x, y), cost);
            updatedNode.Cost = updatedNode.DistFromStartCost + updatedNode.DistFromGoalCost;

            if (_openSet.Contains(coordinate))
            {
                // Since tile is already in the open list, we enqueue the better option that
                // has a lower cost (existing one will be ignored later by PriorityQueue impl)

                _openSet.Enqueue(updatedNode);
                return false;
            }

            _openSet.Enqueue(updatedNode);

            int distanceToTarget = MathHelper.GetDistance(_endPoint, new Point(x, y));
            bool withinRange = distanceToTarget <= _pathfindDistance && Math.Abs(_endPointZ - z) < Constants.ALLOWED_Z_DIFFERENCE;

            if (withinRange)
            {
                // For very short paths, prefer getting EXACTLY on target over just "within range"
                // This ensures diagonal moves that land on target are preferred over cardinal moves that stop nearby
                bool isVeryShort = IsVeryShortPath();
                bool exactlyOnTarget = distanceToTarget == 0;

                if (_goalNode == null)
                {
                    // No goal yet, this is it
                    _goalNode = updatedNode;
                    Log.Trace($"[Pathfinder] Setting initial goal node at ({x},{y},{z}), distToTarget={distanceToTarget}");
                }
                else if (isVeryShort && exactlyOnTarget)
                {
                    // For short paths, if this node is EXACTLY on target, prefer it over previous goal
                    int currentGoalDist = MathHelper.GetDistance(_endPoint, new Point(_goalNode.X, _goalNode.Y));
                    if (currentGoalDist > 0)
                    {
                        _goalNode = updatedNode;
                        Log.Trace($"[Pathfinder] Overwriting goal with exact target node at ({x},{y},{z})");
                    }
                }
            }

            return true;
        }

        private bool OpenNodes(PathNode node)
        {
            bool found = false;
            bool isVeryShort = IsVeryShortPath(); // Check if 1-2 tile path

            Log.Trace($"[Pathfinder] OpenNodes at ({node.X},{node.Y},{node.Z}), isVeryShort={isVeryShort}, target=({_endPoint.X},{_endPoint.Y})");

            for (int i = 0; i < 8; i++)
            {
                var direction = (Direction)i;
                int x = node.X;
                int y = node.Y;
                sbyte z = (sbyte)node.Z;
                Direction oldDirection = direction;

                // For very short paths, allow diagonal movement without strict corner-cutting checks
                if (CanWalk(ref direction, ref x, ref y, ref z, dontChangeXY: false, allowDiagonalForShortPath: isVeryShort))
                {
                    if (direction != oldDirection)
                    {
                        Log.Trace($"[Pathfinder] >>> Direction changed from {oldDirection} to {direction}, skipping");
                        continue;
                    }

                    int diagonal = i % 2;

                    if (diagonal != 0)
                    {
                        var wantDirection = (Direction)i;
                        int wantX = node.X;
                        int wantY = node.Y;
                        GetNewXY((byte)wantDirection, ref wantX, ref wantY);

                        // For very short paths (1-2 tiles), allow diagonal even if position differs slightly
                        // This prevents forcing 2 cardinal steps when 1 diagonal step is possible
                        if (!isVeryShort && (x != wantX || y != wantY))
                        {
                            Log.Trace($"[Pathfinder] >>> Diagonal {direction} blocked: wanted ({wantX},{wantY}), got ({x},{y})");
                            diagonal = -1;
                        }
                    }

                    if (diagonal >= 0)
                    {
                        // For very short paths: diagonal=1, cardinal=2 (prefer diagonal!)
                        // For normal paths: diagonal=2, cardinal=1 (normal A*)
                        int cost = (diagonal == 0) ? (isVeryShort ? 2 : 1) : (isVeryShort ? 1 : 2);

                        Log.Trace($"[Pathfinder] >>> Adding node: direction={direction}, to=({x},{y},{z}), cost={cost}, diagonal={diagonal}");

                        if (AddNodeToList((int)direction, x, y, z, node, cost))
                        {
                            found = true;
                        }
                    }
                }
                else
                {
                    Log.Trace($"[Pathfinder] >>> CanWalk failed for direction {direction}");
                }
            }

            return found;
        }

        private static PathNode FindCheapestNode()
        {
            while (!_openSet.IsEmpty())
            {
                PathNode node = _openSet.Dequeue();
                if (node == null) continue;

                (int X, int Y, int Z) key = (node.X, node.Y, node.Z);

                if (_closedSet.ContainsKey(key))
                {
                    // TODO: Maybe just remove this conditional.
                    // If it happens, there is a bug that needs to be fixed.
                    Log.Warn("[Pathfinder]Node in both open and closed set. This shouldn't happen.");
                    continue;
                }

                _closedSet[key] = node;
                return node;
            }

            return null;
        }

        private bool GetRunState(PathNode node = null, int dist = 22)
        {
            if (_walkStyle == WalkStyle.WalkOnly)
                return false;
            else if (_walkStyle == WalkStyle.RunOnly)
                return true;

            if (node != null && node.DistFromGoalCost > dist)
                return true;

            return false;
        }

        // Optimization 1: Adaptive node limit based on path distance
        private int GetMaxNodes(int distance)
        {
            // Scale max nodes based on expected path complexity
            // Lower limits for very short paths to reduce zig-zagging
            return distance <= 5 ? 500 :        // Very short: 500 nodes
                   distance <= 10 ? 2000 :      // Short paths: 2k nodes
                   distance <= 50 ? 25000 :     // Medium paths: 25k nodes
                   PATHFINDER_MAX_NODES;        // Long paths: 150k nodes
        }

        // Check if the path is extremely short (1-2 tiles) - use direct movement
        private bool IsVeryShortPath()
        {
            int dx = Math.Abs(_endPoint.X - _startPoint.X);
            int dy = Math.Abs(_endPoint.Y - _startPoint.Y);
            return Math.Max(dx, dy) <= 2; // Chebyshev distance <= 2
        }

        private bool FindPath(int maxNodes, bool ignoreAutowalkState)
        {
            var startNode = PathNode.Get();

            startNode.X = _startPoint.X;
            startNode.Y = _startPoint.Y;
            startNode.Z = _world.Player.Z;
            startNode.Parent = null;
            startNode.DistFromStartCost = 0;

            var startPoint = new Point(_startPoint.X, _startPoint.Y);
            startNode.DistFromGoalCost = GetGoalDistCost(startPoint, 0);
            startNode.Cost = startNode.DistFromGoalCost;

            _openSet.Enqueue(startNode);

            int closedNodesCount = 0;

            _run = GetRunState(startNode, 22);

            while (ignoreAutowalkState || AutoWalking)
            {
                PathNode currentNode = FindCheapestNode();

                if (currentNode == null)
                {
                    return false;
                }

                closedNodesCount++;

                // Optimization 3: Early distance cutoff for impossible paths
                if (currentNode.DistFromGoalCost > MAX_REASONABLE_DISTANCE && !UseLongDistancePathfinding)
                {
                    // Path is too far for local pathfinding, give up early
                    Log.Trace($"[Pathfinder] Path too far ({currentNode.DistFromGoalCost} > {MAX_REASONABLE_DISTANCE}), use LongDistancePathfinder");
                    return false;
                }

                if (closedNodesCount >= maxNodes)
                {
                    break;
                }

                if (_goalNode is not null)
                {
                    ReconstructPath(_goalNode);

#if DEBUG
                    foreach (PathNode step in _path) World.Instance.Map.GetTile(step.X, step.Y).Hue = 32;
#endif

                    return true;
                }

                OpenNodes(currentNode);
            }

            return false;
        }

        private void ReconstructPath(PathNode goalNode)
        {
            var pathStack = new Stack<PathNode>();
            PathNode current = goalNode;
            var visited = new HashSet<PathNode>();
            int iterations = 0;

            while (current is not null && current.Parent != current && iterations < PATHFINDER_MAX_NODES)
            {
                // Check for cycles
                if (visited.Contains(current))
                {
                    // Cycle detected - break out
                    Log.Warn("[Pathfinder]Cycle detected in path reconstruction!");
                    break;
                }

                visited.Add(current);
                pathStack.Push(current);
                current = current.Parent;
                iterations++;
            }

            if (iterations >= PATHFINDER_MAX_NODES)
            {
                Log.Warn($"[Pathfinder]Path reconstruction hit iteration limit: {PATHFINDER_MAX_NODES}");
            }

            _path.Clear();
            while (pathStack.Count > 0)
            {
                _path.Add(pathStack.Pop());
            }
        }

        public List<(int X, int Y, int Z)> GetPathTo(int x, int y, int z, int distance, WalkStyle walkStyle = WalkStyle.Auto)
        {
            CleanupPathfinding();
            _pointIndex = 0;
            _goalNode = null;
            _run = false;
            _startPoint.X = _world.Player.X;
            _startPoint.Y = _world.Player.Y;
            _endPoint.X = x;
            _endPoint.Y = y;
            _endPointZ = z;
            _walkStyle = walkStyle;
            _pathfindDistance = distance;

            // Try to use cached path first
            if (TryGetCachedPath(_world.Player.X, _world.Player.Y, _world.Player.Z, x, y, z, distance, walkStyle))
            {
                var result = new List<(int X, int Y, int Z)>(_path.Count);
                foreach (PathNode node in _path)
                {
                    result.Add((node.X, node.Y, node.Z));
                }
                return result;
            }

            // Optimization 1: Use adaptive node limit based on distance
            int estimatedDistance = Math.Max(Math.Abs(x - _world.Player.X), Math.Abs(y - _world.Player.Y));
            int maxNodes = GetMaxNodes(estimatedDistance);

            if (!FindPath(maxNodes, ignoreAutowalkState: true))
            {
                return null;
            }

            // Cache the successful path
            CachePath(_world.Player.X, _world.Player.Y, _world.Player.Z, x, y, z, distance, walkStyle);

            var result2 = new List<(int X, int Y, int Z)>(_path.Count);

            foreach (PathNode node in _path)
            {
                result2.Add((node.X, node.Y, node.Z));
            }

            return result2;
        }

        public bool WalkTo(int x, int y, int z, int distance, WalkStyle walkStyle = WalkStyle.Auto)
        {
            Log.Trace($"[Pathfinder] WalkTo called: target=({x},{y},{z}), distance={distance}, followTarget={_followTargetSerial:X8}");

            if (_world.Player == null /*|| World.Player.Stamina == 0*/ || _world.Player.IsParalyzed)
            {
                return false;
            }

            EventSink.InvokeOnPathFinding(null, new Vector4(x, y, z, distance));

            CleanupPathfinding();
            _pointIndex = 0;
            _goalNode = null;
            _run = false;
            _startPoint.X = _world.Player.X;
            _startPoint.Y = _world.Player.Y;
            _endPoint.X = x;
            _endPoint.Y = y;
            _endPointZ = z;
            _pathfindDistance = distance;
            _walkStyle = walkStyle;
            AutoWalking = true;

            // Try to use cached path first
            bool usedCache = TryGetCachedPath(_world.Player.X, _world.Player.Y, _world.Player.Z, x, y, z, distance, walkStyle);

            if (!usedCache)
            {
                // Optimization 1: Use adaptive node limit based on distance
                int estimatedDistance = Math.Max(Math.Abs(x - _world.Player.X), Math.Abs(y - _world.Player.Y));
                int maxNodes = GetMaxNodes(estimatedDistance);

                if (FindPath(maxNodes, ignoreAutowalkState: false))
                {
                    // Cache the successful path
                    CachePath(_world.Player.X, _world.Player.Y, _world.Player.Z, x, y, z, distance, walkStyle);
                }
                else
                {
                    AutoWalking = false;
                }
            }

            if (_path.Count > 0)
            {
                _pointIndex = 1;
                ProcessAutoWalk();
            }
            else
            {
                AutoWalking = false;
            }

            bool status = _path.Count != 0;

            Log.Trace($"[Pathfinder] WalkTo result: status={status}, pathLength={_path.Count}, usedCache={usedCache}");
            if (_path.Count > 0)
            {
                Log.Trace($"[Pathfinder] First step: direction={(Direction)_path[0].Direction}, to=({_path[0].X},{_path[0].Y},{_path[0].Z})");
            }

            if (UseLongDistancePathfinding && !status)
                if (LongDistancePathfinder.WalkLongDistance(x, y))
                    return true;

            return status;
        }

        public void ProcessAutoWalk()
        {
            if (AutoWalking && _world.InGame && _world.Player.Walker.StepsCount < Constants.MAX_STEP_COUNT && _world.Player.Walker.LastStepRequestTime <= Time.Ticks)
            {
                if (_pointIndex >= 0 && _pointIndex < _path.Count)
                {
                    PathNode p = _path[_pointIndex];

                    _world.Player.GetEndPosition(out int x, out int y, out sbyte z, out Direction dir);

                    if (dir == (Direction)p.Direction)
                    {
                        _pointIndex++;
                    }

                    if (!_world.Player.Walk((Direction)p.Direction, _run))
                    {
                        StopAutoWalk();
                    }
                }
                else
                {
                    StopAutoWalk();
                }
            }
        }

        public void StopAutoWalk()
        {
            AutoWalking = false;
            _run = false;
            _followTargetSerial = 0; // Clear follow target
            CleanupPathfinding();
        }

        /// <summary>
        /// Check if a cached path exists and is still valid for the given parameters
        /// </summary>
        private bool TryGetCachedPath(int startX, int startY, int startZ, int endX, int endY, int endZ, int distance, WalkStyle walkStyle)
        {
            if (!_lastPathKey.HasValue || !_lastPathEntry.HasValue)
                return false;

            var key = _lastPathKey.Value;
            var entry = _lastPathEntry.Value;

            // Check if cache has expired
            if ((DateTime.UtcNow - entry.CachedTime).TotalSeconds > CACHE_VALIDITY_SECONDS)
            {
                Log.Trace("[Pathfinder] Cache expired");
                return false;
            }

            // Check if destination matches exactly
            if (key.EndX != endX || key.EndY != endY || key.EndZ != endZ)
                return false;

            // Check if distance and style match
            if (key.Distance != distance || key.WalkStyle != walkStyle)
                return false;

            // Check if follow target matches
            if (key.FollowTarget != _followTargetSerial)
                return false;

            // Allow small difference in start position (player might have moved slightly)
            int startDx = Math.Abs(key.StartX - startX);
            int startDy = Math.Abs(key.StartY - startY);
            int startDz = Math.Abs(key.StartZ - startZ);

            if (startDx > CACHE_START_TOLERANCE || startDy > CACHE_START_TOLERANCE || startDz > 5)
            {
                Log.Trace($"[Pathfinder] Start position too different: dx={startDx}, dy={startDy}, dz={startDz}");
                return false;
            }

            // Restore cached path
            _path.Clear();
            _run = entry.IsRunning;

            foreach (var step in entry.Steps)
            {
                var node = PathNode.Get();
                node.X = step.X;
                node.Y = step.Y;
                node.Z = step.Z;
                node.Direction = step.Direction;
                _path.Add(node);
            }

            Log.Trace($"[Pathfinder] Using cached path with {_path.Count} steps");
            return true;
        }

        /// <summary>
        /// Cache the current path for future reuse
        /// </summary>
        private void CachePath(int startX, int startY, int startZ, int endX, int endY, int endZ, int distance, WalkStyle walkStyle)
        {
            if (_path.Count == 0)
                return;

            var key = new PathCacheKey
            {
                StartX = startX,
                StartY = startY,
                StartZ = startZ,
                EndX = endX,
                EndY = endY,
                EndZ = endZ,
                Distance = distance,
                WalkStyle = walkStyle,
                FollowTarget = _followTargetSerial
            };

            var steps = new List<(int X, int Y, int Z, int Direction)>(_path.Count);
            foreach (var node in _path)
            {
                steps.Add((node.X, node.Y, node.Z, node.Direction));
            }

            var entry = new PathCacheEntry
            {
                Steps = steps,
                CachedTime = DateTime.UtcNow,
                IsRunning = _run
            };

            _lastPathKey = key;
            _lastPathEntry = entry;

            Log.Trace($"[Pathfinder] Cached path with {steps.Count} steps to ({endX},{endY},{endZ})");
        }

        /// <summary>
        /// Clear the path cache
        /// </summary>
        public static void ClearPathCache()
        {
            _lastPathKey = null;
            _lastPathEntry = null;
            Log.Trace("[Pathfinder] Path cache cleared");
        }

        /// <summary>
        /// Set the follow target serial to exclude it from pathfinding obstacles.
        /// This allows diagonal movement when following a mobile.
        /// </summary>
        public static void SetFollowTarget(uint serial)
        {
            _followTargetSerial = serial;
            Log.Trace($"[Pathfinder] SetFollowTarget: {serial:X8}");
        }

        /// <summary>
        /// Clear the follow target serial
        /// </summary>
        public static void ClearFollowTarget()
        {
            Log.Trace($"[Pathfinder] ClearFollowTarget (was: {_followTargetSerial:X8})");
            _followTargetSerial = 0;
        }

        private static void CleanupPathfinding()
        {
            // Clean up any remaining nodes in the open set
            while (!_openSet.IsEmpty())
            {
                PathNode node = _openSet.Dequeue();
                node?.Return();
            }

            _openSet.Clear();

            // Clean up any remaining nodes in the closed set
            foreach (KeyValuePair<(int x, int y, int z), PathNode> n in _closedSet)
            {
                n.Value.Return();
            }

            _closedSet.Clear();

            // Optimization 2: Clear walkability cache when starting new pathfinding
            _walkabilityCache.Clear();

            _path.Clear();
            _goalNode = null;
        }

        private enum PATH_STEP_STATE
        {
            PSS_NORMAL = 0,
            PSS_DEAD_OR_GM,
            PSS_ON_SEA_HORSE,
            PSS_FLYING
        }

        [Flags]
        private enum PATH_OBJECT_FLAGS : uint
        {
            POF_IMPASSABLE_OR_SURFACE = 0x00000001,
            POF_SURFACE = 0x00000002,
            POF_BRIDGE = 0x00000004,
            POF_NO_DIAGONAL = 0x00000008
        }

        private class PathObject : IComparable<PathObject>
        {
            private static ObjectPool<PathObject> _pool = new ObjectPool<PathObject>(
                () => new PathObject(0, 0, 0, 0, null), (po) =>
                {
                    po.Flags = 0;
                    po.Z = 0;
                    po.AverageZ = 0;
                    po.Height = 0;
                    po.Object = null;
                },
                15
                );
            private PathObject(uint flags, int z, int avgZ, int h, GameObject obj)
            {
                Flags = flags;
                Z = z;
                AverageZ = avgZ;
                Height = h;
                Object = obj;
            }

            public static PathObject Get(uint flags, int z, int avgZ, int h, GameObject obj)
            {
                PathObject po = _pool.Get();
                po.Flags = flags;
                po.Z = z;
                po.AverageZ = avgZ;
                po.Height = h;
                po.Object = obj;
                return po;
            }

            public void Return() => _pool.Return(this);

            public uint Flags { get; private set; }

            public int Z { get; private set; }

            public int AverageZ { get; private set; }

            public int Height { get; private set; }

            public GameObject Object { get; private set; }

            public int CompareTo(PathObject other)
            {
                int comparision = Z - other.Z;

                if (comparision == 0)
                {
                    comparision = Height - other.Height;
                }

                return comparision;
            }
        }

        private class PathNode
        {
            private static ObjectPool<PathNode> _pool = new(
                () => new PathNode(),
                (pn) => { pn.Reset(); },
                15
                );

            private PathNode()
            {
            }

            public static PathNode Get() => _pool.Get();

            public void Return() => _pool.Return(this);

            public bool IsValid { get; set; }

            public int X { get; set; }

            public int Y { get; set; }

            public int Z { get; set; }

            public int Direction { get; set; }

            public bool Used { get; set; }

            public int Cost { get; set; }

            public int DistFromStartCost { get; set; }

            public int DistFromGoalCost { get; set; }

            public PathNode Parent { get; set; }

            public void Reset()
            {
                Parent = null;
                Used = IsValid = false;
                X = Y = Z = Direction = Cost = DistFromGoalCost = DistFromStartCost = 0;
            }
        }

        class PriorityQueue
        {
            readonly List<PathNode> _heap = new();
            readonly Dictionary<(int, int, int), PathNode> _lookup = new();
            readonly object _lock = new();

            internal bool Contains((int, int, int) coordinate)
            {
                lock (_lock)
                {
                    if (_lookup.TryGetValue(coordinate, out PathNode existing))
                    {
                        // The priority queue lazily remove duplicates, so we check
                        // whether the node is valid here.
                        return existing.IsValid;
                    }

                    return false;
                }
            }

            internal void Clear()
            {
                lock (_lock)
                {
                    _heap.Clear();
                    _lookup.Clear();
                }
            }

            internal bool IsEmpty()
            {
                lock (_lock)
                {
                    while (_heap.Count > 0)
                    {
                        // The priority queue lazily remove duplicates, so we check
                        // for them here. If should be removed lazily, remove it now
                        // and continue to next element.
                        if (_heap[0].IsValid) return false;

                        RemoveAt(0);
                    }

                    return true;
                }
            }

            internal void Enqueue(PathNode node)
            {
                lock (_lock)
                {
                    (int, int, int) key = GetKey(node);
                    if (_lookup.TryGetValue(key, out PathNode existing))
                    {
                        if (existing.IsValid && existing.Cost <= node.Cost)
                        {
                            // Existing priority is better or equal, so ignore this node.

                            // While it breaks encapsulation to perform this inside
                            // the priority queue, it is safe to return this node
                            // to the object pool early at this point because we know
                            // the caller will discard its reference to this node, so
                            // it cannot be used later during path reconstruction.
                            node.Return();

                            return;
                        }

                        // The priority queue lazily remove duplicates, so we mark existing to be deleted later.
                        existing.IsValid = false;
                    }

                    node.IsValid = true;
                    _lookup[key] = node;
                    _heap.Add(node);
                    int index = _heap.Count - 1;
                    HeapifyUp(index);
                }
            }

            internal PathNode Dequeue()
            {
                lock (_lock)
                {
                    while (_heap.Count > 0)
                    {
                        // The priority queue lazily remove duplicates, so we check
                        // for them here. If should be removed lazily, remove it now
                        // and continue to next element.
                        PathNode top = _heap[0];
                        if (!top.IsValid)
                        {
                            RemoveAt(0);
                            continue;
                        }

                        RemoveAt(0);
                        return top;
                    }

                    return null;
                }
            }

            void Swap(int i, int j) => (_heap[j], _heap[i]) = (_heap[i], _heap[j]);

            void HeapifyUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (_heap[index].Cost < _heap[parent].Cost)
                    {
                        Swap(index, parent);
                        index = parent;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            void HeapifyDown(int index)
            {
                int lastIndex = _heap.Count - 1;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = index * 2 + 2;
                    int smallest = index;

                    if (left <= lastIndex && _heap[left].Cost < _heap[smallest].Cost)
                    {
                        smallest = left;
                    }

                    if (right <= lastIndex && _heap[right].Cost < _heap[smallest].Cost)
                    {
                        smallest = right;
                    }

                    if (smallest != index)
                    {
                        Swap(index, smallest);
                        index = smallest;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            (int, int, int) GetKey(PathNode node) => (node.X, node.Y, node.Z);

            void RemoveAt(int index)
            {
                PathNode node = _heap[index];
                (int, int, int) key = GetKey(node);
                _lookup.Remove(key);

                int lastIndex = _heap.Count - 1;
                if (index != lastIndex)
                {
                    Swap(index, lastIndex);
                }

                _heap.RemoveAt(lastIndex);

                if (index < _heap.Count)
                {
                    HeapifyDown(index);
                    HeapifyUp(index);
                }

                if (!node.IsValid)
                {
                    // While it breaks encapsulation to perform this inside
                    // the priority queue, it is safe to return the invalid
                    // node to the object pool early at this point because
                    // we know no other data structures reference it any
                    // longer, so it won't be used in any subsequent
                    // pathfinding calculations nor in path reconstruction.
                    node.Return();
                }
            }
        }
    }
}
