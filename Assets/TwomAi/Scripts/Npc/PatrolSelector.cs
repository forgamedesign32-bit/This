using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // F1-1: weighted choice among linked, traversable nodes of the guard's zone.
    // Random draws happen only when a decision is made; same seed + same inputs => same choices.
    public sealed class PatrolSelector
    {
        private readonly System.Random _random;
        private readonly AiTuningConfig _config;
        private readonly List<Waypoint> _route;
        private readonly HashSet<Waypoint> _zone = new HashSet<Waypoint>();
        private readonly List<Waypoint> _candidates = new List<Waypoint>();
        private readonly List<float> _weights = new List<float>();

        private int _direction = 1;
        private Waypoint _previous;

        public PatrolSelector(System.Random random, AiTuningConfig config, List<Waypoint> route, List<Waypoint> zoneExtras)
        {
            _random = random;
            _config = config;
            _route = route;
            AddToZone(route);
            AddToZone(zoneExtras);
        }

        public int DecisionCount { get; private set; }

        public Waypoint GetRouteEntry(Vector3 from)
        {
            Waypoint best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _route.Count; i++)
            {
                var node = _route[i];
                if (node == null)
                    continue;

                float distance = AiMath.Distance(from, node.Position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = node;
            }

            return best;
        }

        public Waypoint SelectNext(Waypoint current, WaypointGraph graph, bool variable)
        {
            var primary = GetPrimary(current, graph);
            _candidates.Clear();
            var links = current.Links;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link != null && _zone.Contains(link) && graph.IsTraversable(current, link))
                    _candidates.Add(link);
            }

            if (_candidates.Count == 0)
                return null;

            Waypoint choice;
            if (!variable || _candidates.Count == 1)
                choice = primary != null ? primary : _candidates[0];
            else
                choice = WeightedChoice(primary);

            _previous = current;
            DecisionCount++;
            return choice;
        }

        public float NextWaitTime(bool variable)
        {
            if (!variable)
                return _config.LegacyPatrolWait;

            return Mathf.Lerp(_config.PatrolWaitMin, _config.PatrolWaitMax, (float)_random.NextDouble());
        }

        private Waypoint WeightedChoice(Waypoint primary)
        {
            int alternatives = primary != null ? _candidates.Count - 1 : _candidates.Count;
            float alternativeWeight = alternatives > 0 ? _config.PatrolAlternativeWeight / alternatives : 0f;
            float total = 0f;
            _weights.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var candidate = _candidates[i];
                float weight = candidate == primary ? _config.PatrolPrimaryWeight : alternativeWeight;
                if (candidate == _previous)
                    weight *= _config.RepeatNodePenalty;

                _weights.Add(weight);
                total += weight;
            }

            double roll = _random.NextDouble() * total;
            for (int i = 0; i < _candidates.Count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0d)
                    return _candidates[i];
            }

            return _candidates[_candidates.Count - 1];
        }

        // Next node along the route; loops if the route is closed, otherwise ping-pongs.
        private Waypoint GetPrimary(Waypoint current, WaypointGraph graph)
        {
            int index = _route.IndexOf(current);
            int count = _route.Count;
            if (index < 0 || count < 2)
                return null;

            int next = (index + _direction + count) % count;
            if (!IsLinked(current, _route[next], graph))
            {
                _direction = -_direction;
                next = (index + _direction + count) % count;
            }

            return IsLinked(current, _route[next], graph) ? _route[next] : null;
        }

        private static bool IsLinked(Waypoint a, Waypoint b, WaypointGraph graph)
        {
            if (a == null || b == null)
                return false;

            var links = a.Links;
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i] == b)
                    return graph.IsTraversable(a, b);
            }

            return false;
        }

        private void AddToZone(List<Waypoint> nodes)
        {
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null)
                    _zone.Add(nodes[i]);
            }
        }
    }
}
