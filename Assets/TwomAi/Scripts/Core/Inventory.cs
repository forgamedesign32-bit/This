using System.Collections.Generic;

namespace TwomAi
{
    // Item names shared by the backpack and containers (no weight limit).
    public static class Inventory
    {
        public const string Ammo = "탄약";
        public const string Lockpick = "락픽";
    }

    // In-game night clock shared by HUD and player: starts 21:00, ~1.4 game minutes per second,
    // the night ends at 05:00 (480 game minutes).
    public static class NightClock
    {
        public const float StartMinutes = 21f * 60f;
        public const float GameMinutesPerSecond = 1.4f;
        public const float NightLengthMinutes = 8f * 60f;
        public const float DawnWarningMinutes = 7.5f * 60f;

        public static float ElapsedMinutes => UnityEngine.Time.timeSinceLevelLoad * GameMinutesPerSecond;
        public static bool IsDawn => ElapsedMinutes >= NightLengthMinutes;
        public static bool IsDawnSoon => ElapsedMinutes >= DawnWarningMinutes;

        // Shown in 10-minute steps, as in the original.
        public static string Text
        {
            get
            {
                int minutes = (int)(StartMinutes + System.Math.Min(ElapsedMinutes, NightLengthMinutes));
                minutes = minutes / 10 * 10 % (24 * 60);
                int hour24 = minutes / 60;
                int hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
                return $"{hour12:00}:{minutes % 60:00}{(hour24 < 12 ? "am" : "pm")}";
            }
        }
    }
}
