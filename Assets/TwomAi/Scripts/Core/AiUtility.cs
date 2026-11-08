using System;
using System.Collections.Generic;
using UnityEngine;

namespace TwomAi
{
    public static class AiMath
    {
        // Floors are stacked, so distances are full 3D; only facing uses the horizontal plane.
        public const float SameFloorTolerance = 0.6f;

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static float Distance(Vector3 a, Vector3 b) => Vector3.Distance(a, b);

        public static bool IsSameFloor(Vector3 a, Vector3 b) => Mathf.Abs(a.y - b.y) < SameFloorTolerance;
    }

    public static class AiPhysics
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // True when a wall or closed door (any collider with Occluder) lies between the points.
        public static bool IsOccluded(Vector3 from, Vector3 to)
        {
            return CountOccluders(from, to, true) > 0;
        }

        public static int CountOccluders(Vector3 from, Vector3 to, bool stopAtFirst = false)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return 0;

            int count = Physics.RaycastNonAlloc(from, delta / distance, Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            int occluders = 0;
            for (int i = 0; i < count; i++)
            {
                if (!Hits[i].collider.TryGetComponent(out Occluder _))
                    continue;

                occluders++;
                if (stopAtFirst)
                    return occluders;
            }

            return occluders;
        }
    }

    public static class AiEventBus
    {
        public static event Action<NoiseEvent> NoiseEmitted;

        public static NoiseEvent EmitNoise(Vector3 position, float loudness, int sourceId)
        {
            var noise = new NoiseEvent(AiIds.Next(), position, loudness, Time.time, sourceId);
            Raise(noise);
            return noise;
        }

        // Raise an existing event (used by QA to deliver the same EventId twice).
        public static void Raise(in NoiseEvent noise)
        {
            NoiseEmitted?.Invoke(noise);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBus() => NoiseEmitted = null;
    }

    public static class AiLog
    {
        private const int Capacity = 14;
        private static readonly Queue<string> Lines = new Queue<string>(Capacity + 1);

        public static IEnumerable<string> Entries => Lines;
        public static bool MirrorToConsole { get; set; } = true;

        public static void Add(string message)
        {
            string line = $"[{Time.time:0.0}] {message}";
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();

            if (MirrorToConsole)
                Debug.Log("[TwomAi] " + line);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLog() => Lines.Clear();
    }
}
