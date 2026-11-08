using UnityEngine;

namespace TwomAi
{
    // Screen areas owned by HUD widgets; world clicks and wheel zoom are ignored there.
    // Widgets re-register every frame; a rect stays valid for the frame it was added and the next one.
    public static class UiBlocker
    {
        private const int Capacity = 16;
        private static readonly Rect[] Rects = new Rect[Capacity];
        private static readonly int[] Frames = new int[Capacity];
        private static int _next;

        // Rect in GUI space (origin top-left).
        public static void Add(Rect rect)
        {
            Rects[_next] = rect;
            Frames[_next] = Time.frameCount;
            _next = (_next + 1) % Capacity;
        }

        // Input System screen position has origin bottom-left.
        public static bool IsPointerOverUi(Vector2 screenPosition)
        {
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            int frame = Time.frameCount;
            for (int i = 0; i < Capacity; i++)
            {
                if (frame - Frames[i] <= 1 && Rects[i].Contains(guiPoint))
                    return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRects()
        {
            for (int i = 0; i < Capacity; i++)
                Frames[i] = int.MinValue / 2;
        }
    }
}
