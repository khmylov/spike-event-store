using System;

namespace EventStore
{
    internal static class Rand
    {
        private static readonly Random _random = new();

        public static TimeSpan TimeSpanMs(int lower, int upper)
        {
            return TimeSpan.FromMilliseconds(_random.Next(lower, upper));
        }
    }
}
