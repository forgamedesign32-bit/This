using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    // F2: threat report delivery (range, delay, TTL, dedup) and role / search-slot assignment.
    // Recipients never re-broadcast a received report, so relay loops cannot happen.
    [DefaultExecutionOrder(-50)]
    public sealed class SquadCoordinator : MonoBehaviour
    {
        private sealed class PendingReport
        {
            public ThreatReport Report;
            public NpcActor Reporter;
            public float DeliverAt;
            public readonly List<GuardBrain> Recipients = new List<GuardBrain>();
        }

        private readonly List<GuardBrain> _guards = new List<GuardBrain>();
        private readonly List<PendingReport> _pending = new List<PendingReport>();
        private readonly List<(GuardBrain guard, float cost)> _ordered = new List<(GuardBrain guard, float cost)>();
        private readonly List<Waypoint> _pathScratch = new List<Waypoint>();
        private readonly List<Waypoint> _nodeScratch = new List<Waypoint>();

        private AiDirector _director;
        private AiTuningConfig _config;
        private WaypointGraph _graph;

        public static SquadCoordinator Instance { get; private set; }

        public IReadOnlyList<GuardBrain> Guards => _guards;
        public int LastReportId { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _director = AiDirector.Instance;
            _config = _director.Config;
            _graph = WaypointGraph.Instance;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Register(GuardBrain guard)
        {
            if (guard != null && !_guards.Contains(guard))
                _guards.Add(guard);
        }

        public void Unregister(GuardBrain guard) => _guards.Remove(guard);

        public int Submit(NpcActor reporter, Vector3 lastKnownPosition, float confidence, bool hasVisualContact, bool shouted = false)
        {
            if (_director == null || !_director.IsF2Active || reporter == null || reporter.IsDead)
                return -1;

            var report = new ThreatReport(AiIds.Next(), reporter.Id, lastKnownPosition, Mathf.Clamp01(confidence),
                Time.time, hasVisualContact);
            LastReportId = report.ReportId;

            // Range is evaluated at send time; targets out of range never receive it.
            var pending = new PendingReport { Report = report, Reporter = reporter, DeliverAt = Time.time + _config.ReportDelay };
            for (int i = 0; i < _guards.Count; i++)
            {
                var guard = _guards[i];
                if (guard == null || guard.Actor == reporter || guard.Actor.IsDead)
                    continue;

                if (shouted ? CanHearShout(reporter, guard.Actor) : CanCommunicate(reporter, guard.Actor))
                    pending.Recipients.Add(guard);
            }

            AiLog.Add($"Report #{report.ReportId} {reporter.name} {(shouted ? "SHOUT " : "")}{(hasVisualContact ? "SEEN" : "suspect")} c={report.Confidence:0.0} -> {pending.Recipients.Count}");
            if (pending.Recipients.Count > 0)
                _pending.Add(pending);

            return report.ReportId;
        }

        // A yell carries through walls and floors; only distance limits it.
        public bool CanHearShout(NpcActor from, NpcActor to)
        {
            return AiMath.Distance(from.transform.position, to.transform.position) <= _config.ShoutRange;
        }

        // Straight distance within range AND (same region OR clear line of sight).
        public bool CanCommunicate(NpcActor from, NpcActor to)
        {
            if (AiMath.Distance(from.transform.position, to.transform.position) > _config.CommunicationRange)
                return false;

            var a = _graph.FindNearest(from.transform.position, true);
            var b = _graph.FindNearest(to.transform.position, true);
            if (a != null && b != null && a.RegionId == b.RegionId)
                return true;

            return !AiPhysics.IsOccluded(from.EyePosition, to.EyePosition);
        }

        private void Update()
        {
            float now = Time.time;
            int i = 0;
            while (i < _pending.Count)
            {
                var pending = _pending[i];
                if (now < pending.DeliverAt)
                {
                    i++;
                    continue;
                }

                _pending.RemoveAt(i);
                if (pending.Reporter == null || pending.Reporter.IsDead)
                {
                    AiLog.Add($"Report #{pending.Report.ReportId} dropped (reporter down)");
                    continue;
                }

                Deliver(pending);
            }
        }

        private void Deliver(PendingReport pending)
        {
            var report = pending.Report;
            bool reporterIsGuard = pending.Reporter.Kind == NpcKind.Guard;
            int pursuerSlots = _config.MaxAssignedPursuers;
            if (report.HasVisualContact && reporterIsGuard)
                pursuerSlots--;

            _ordered.Clear();
            for (int i = 0; i < pending.Recipients.Count; i++)
            {
                var guard = pending.Recipients[i];
                if (guard == null || guard.Actor.IsDead)
                    continue;

                // A guard with its own visual contact keeps chasing and counts as a pursuer.
                if (guard.State == GuardState.Chase)
                {
                    if (report.HasVisualContact)
                        pursuerSlots--;
                    continue;
                }

                _ordered.Add((guard, AssignmentCost(guard, report.LastKnownPosition)));
            }

            _ordered.Sort((a, b) => a.cost.CompareTo(b.cost));
            bool centerTaken = reporterIsGuard;
            for (int i = 0; i < _ordered.Count; i++)
            {
                var guard = _ordered[i].guard;
                SquadRole role;
                Vector3? slot = null;
                if (!report.HasVisualContact)
                {
                    role = SquadRole.Flank;
                    slot = FindFlankSlot(report.LastKnownPosition, guard);
                }
                else if (pursuerSlots > 0)
                {
                    pursuerSlots--;
                    if (!centerTaken)
                    {
                        role = SquadRole.Pursue;
                        centerTaken = true;
                    }
                    else
                    {
                        role = SquadRole.Flank;
                        slot = FindFlankSlot(report.LastKnownPosition, guard);
                    }
                }
                else
                {
                    role = SquadRole.GuardExit;
                    slot = FindExitSlot(report.LastKnownPosition, guard);
                    if (!slot.HasValue)
                    {
                        role = SquadRole.Flank;
                        slot = FindFlankSlot(report.LastKnownPosition, guard);
                    }
                }

                if (guard.ReceiveReport(report, role, slot))
                    AiLog.Add($"  #{report.ReportId} -> {guard.name} as {role}");
            }
        }

        // Path length to the clue; guards already holding a pursue/flank role are kept stable.
        private float AssignmentCost(GuardBrain guard, Vector3 target)
        {
            Vector3 position = guard.transform.position;
            float cost = AiMath.Distance(position, target) * 2f;
            var start = _graph.FindNearest(position, true);
            var goal = _graph.FindNearest(target, true);
            if (start != null && goal != null && _graph.TryFindPath(start, goal, _pathScratch))
            {
                cost = AiMath.Distance(position, start.Position) + WaypointGraph.PathLength(_pathScratch)
                    + AiMath.Distance(goal.Position, target);
            }

            bool keepsRole = guard.IsSearching && (guard.Role == SquadRole.Pursue || guard.Role == SquadRole.Flank);
            return keepsRole ? cost - 100f : cost;
        }

        private Vector3? FindFlankSlot(Vector3 center, GuardBrain guard)
        {
            _nodeScratch.Clear();
            var nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                float distance = AiMath.Distance(center, nodes[i].Position);
                if (distance >= _config.SearchSlotSpacing && distance <= _config.SearchRadius)
                    _nodeScratch.Add(nodes[i]);
            }

            _nodeScratch.Sort((a, b) =>
                AiMath.Distance(center, a.Position).CompareTo(AiMath.Distance(center, b.Position)));
            for (int i = 0; i < _nodeScratch.Count; i++)
            {
                Vector3 position = _nodeScratch[i].Position;
                if (!IsSlotTaken(position, guard, center) && IsReachable(guard, _nodeScratch[i]))
                    return position;
            }

            return null;
        }

        private Vector3? FindExitSlot(Vector3 center, GuardBrain guard)
        {
            Waypoint best = null;
            float bestDistance = float.MaxValue;
            var nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!node.IsExit || IsSlotTaken(node.Position, guard, null) || !IsReachable(guard, node))
                    continue;

                float distance = AiMath.Distance(center, node.Position);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = node;
            }

            return best != null ? best.Position : (Vector3?)null;
        }

        private bool IsReachable(GuardBrain guard, Waypoint node)
        {
            var start = _graph.FindNearest(guard.transform.position, true);
            return start != null && _graph.TryFindPath(start, node, _pathScratch);
        }

        // Keeps search targets at least SearchSlotSpacing apart (no two guards on one coordinate).
        public bool IsSlotTaken(Vector3 position, GuardBrain asker, Vector3? reserved)
        {
            float spacing = _config != null ? _config.SearchSlotSpacing : 2f;
            if (reserved.HasValue && AiMath.Distance(position, reserved.Value) < spacing)
                return true;

            for (int i = 0; i < _guards.Count; i++)
            {
                var guard = _guards[i];
                if (guard == null || guard == asker || !guard.IsSearching)
                    continue;

                if (AiMath.Distance(guard.CurrentSearchTarget, position) < spacing)
                    return true;
            }

            return false;
        }

        // Re-assign: a dead pursuer/flanker is replaced by the exit guard.
        public void OnGuardDied(GuardBrain dead)
        {
            if (dead.Role != SquadRole.Pursue && dead.Role != SquadRole.Flank)
                return;

            for (int i = 0; i < _guards.Count; i++)
            {
                var guard = _guards[i];
                if (guard == null || guard == dead || guard.State != GuardState.Search || guard.Role != SquadRole.GuardExit)
                    continue;

                var slot = FindFlankSlot(guard.Blackboard.LastKnownPosition, guard);
                guard.PromoteToFlank(slot ?? guard.Blackboard.LastKnownPosition);
                AiLog.Add($"Role reassigned: {guard.name} GuardExit -> Flank");
                return;
            }
        }
    }
}
