using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // Waypoint graph with door-aware Dijkstra. Graph is small (~30 nodes), so O(n^2) is fine.
    [DefaultExecutionOrder(-90)]
    public sealed class WaypointGraph : MonoBehaviour
    {
        private readonly Dictionary<long, LockableDoor> _edgeDoors = new Dictionary<long, LockableDoor>();

        private Waypoint[] _nodes = Array.Empty<Waypoint>();
        private float[] _distance;
        private int[] _previous;
        private bool[] _closed;

        public static WaypointGraph Instance { get; private set; }
        public IReadOnlyList<Waypoint> Nodes => _nodes;

        private void Awake()
        {
            Instance = this;
            _nodes = GetComponentsInChildren<Waypoint>();
            for (int i = 0; i < _nodes.Length; i++)
                _nodes[i].SetIndex(i);

            // Make every link bidirectional.
            for (int i = 0; i < _nodes.Length; i++)
            {
                var links = _nodes[i].Links;
                for (int j = 0; j < links.Count; j++)
                    links[j]?.EnsureLink(_nodes[i]);
            }

            _distance = new float[_nodes.Length];
            _previous = new int[_nodes.Length];
            _closed = new bool[_nodes.Length];

            var doors = FindObjectsByType<LockableDoor>(FindObjectsSortMode.None);
            for (int i = 0; i < doors.Length; i++)
            {
                var door = doors[i];
                if (door.SideA == null || door.SideB == null)
                    continue;

                _edgeDoors[EdgeKey(door.SideA.Index, door.SideB.Index)] = door;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        public LockableDoor GetDoor(Waypoint a, Waypoint b)
        {
            if (a == null || b == null)
                return null;

            return _edgeDoors.TryGetValue(EdgeKey(a.Index, b.Index), out var door) ? door : null;
        }

        public bool IsTraversable(Waypoint a, Waypoint b)
        {
            var door = GetDoor(a, b);
            return door == null || !door.IsLocked;
        }

        public Waypoint FindNearest(Vector3 position, bool requireLineOfSight)
        {
            Waypoint bestVisible = null;
            Waypoint bestAny = null;
            float bestVisibleDistance = float.MaxValue;
            float bestAnyDistance = float.MaxValue;
            Vector3 eye = position + Vector3.up;

            for (int i = 0; i < _nodes.Length; i++)
            {
                var node = _nodes[i];
                float distance = AiMath.Distance(position, node.Position);
                if (distance < bestAnyDistance)
                {
                    bestAnyDistance = distance;
                    bestAny = node;
                }

                if (!requireLineOfSight || distance >= bestVisibleDistance)
                    continue;

                if (AiPhysics.IsOccluded(eye, node.Position + Vector3.up))
                    continue;

                bestVisibleDistance = distance;
                bestVisible = node;
            }

            if (!requireLineOfSight)
                return bestAny;

            return bestVisible != null ? bestVisible : bestAny;
        }

        public bool TryFindPath(Waypoint start, Waypoint goal, List<Waypoint> result, Func<Waypoint, float> nodePenalty = null)
        {
            result.Clear();
            if (start == null || goal == null || start.Index < 0 || goal.Index < 0)
                return false;

            int count = _nodes.Length;
            for (int i = 0; i < count; i++)
            {
                _distance[i] = float.PositiveInfinity;
                _previous[i] = -1;
                _closed[i] = false;
            }

            _distance[start.Index] = 0f;
            while (true)
            {
                int current = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (_closed[i] || _distance[i] >= best)
                        continue;

                    best = _distance[i];
                    current = i;
                }

                if (current < 0)
                    return false;

                if (current == goal.Index)
                    break;

                _closed[current] = true;
                var node = _nodes[current];
                var links = node.Links;
                for (int j = 0; j < links.Count; j++)
                {
                    var link = links[j];
                    if (link == null || link.Index < 0 || _closed[link.Index] || !IsTraversable(node, link))
                        continue;

                    float cost = _distance[current] + Vector3.Distance(node.Position, link.Position);
                    if (nodePenalty != null)
                        cost += nodePenalty(link);

                    if (cost >= _distance[link.Index])
                        continue;

                    _distance[link.Index] = cost;
                    _previous[link.Index] = current;
                }
            }

            for (int i = goal.Index; i >= 0; i = _previous[i])
                result.Add(_nodes[i]);

            result.Reverse();
            return true;
        }

        // Closest point on any same-floor edge: clicked points always land on walkable floor.
        public bool TrySnapToWalkable(Vector3 point, out Vector3 snapped)
        {
            snapped = point;
            float best = float.MaxValue;
            for (int i = 0; i < _nodes.Length; i++)
            {
                var a = _nodes[i];
                if (!AiMath.IsSameFloor(a.Position, point))
                    continue;

                var links = a.Links;
                for (int j = 0; j < links.Count; j++)
                {
                    var b = links[j];
                    if (b == null || b.Index < a.Index || !AiMath.IsSameFloor(b.Position, point))
                        continue;

                    Vector3 candidate = ClosestPointOnSegment(a.Position, b.Position, point);
                    float distance = Vector3.Distance(candidate, point);
                    if (distance >= best)
                        continue;

                    best = distance;
                    snapped = candidate;
                }
            }

            return best < float.MaxValue;
        }

        // Agent standing in the middle of a stair edge: returns its two end nodes.
        public bool TryGetSlopedEdgeAt(Vector3 position, out Waypoint a, out Waypoint b)
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                var from = _nodes[i];
                var links = from.Links;
                for (int j = 0; j < links.Count; j++)
                {
                    var to = links[j];
                    if (to == null || to.Index < from.Index || AiMath.IsSameFloor(from.Position, to.Position))
                        continue;

                    if (Vector3.Distance(ClosestPointOnSegment(from.Position, to.Position, position), position) > 0.3f)
                        continue;

                    a = from;
                    b = to;
                    return true;
                }
            }

            a = null;
            b = null;
            return false;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
        {
            Vector3 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr < 0.0001f)
                return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSqr);
            return a + ab * t;
        }

        public static float PathLength(List<Waypoint> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1].Position, path[i].Position);

            return length;
        }
    }
}
