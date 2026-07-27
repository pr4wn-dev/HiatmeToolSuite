using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modivcare / Hiatme scoreboard timing windows (same as Supey builder and website checks).
    /// PU late: A-leg 0–14 min, B/C 0–29 min. A-leg PU early: up to 29 min. DO: not late (0 min).
    /// Caps are whole minutes: A-leg PU at 14:59 past sched is still OK; late only at 15:00 past.
    /// </summary>
    internal static class McTripTimingRules
    {
        public const int ALegPuLateMaxMinutes = 14;
        public const int ALegPuEarlyMaxMinutes = 29;
        public const int BcLegPuLateMaxMinutes = 29;
        public const int DoLateMaxMinutes = 0;

        /// <summary>
        /// Lenient load: PU/DO within this many minutes of scheduled (early or late) are left as-is so times look natural.
        /// </summary>
        public const int LenientNaturalSlackMinutes = 6;

        /// <summary>Lenient load: flag A-leg PU early only above this (scoreboard allows 29).</summary>
        public const int LenientALegPuEarlyMinMinutes = 45;

        /// <summary>Lenient load: flag DO early only above this many minutes before scheduled.</summary>
        public const int LenientDoEarlyMinMinutes = 30;

        /// <summary>
        /// Lenient load: flag DO late only above this many minutes after scheduled (minor lateness is left as driver actuals).
        /// </summary>
        public const int LenientDoLateMinMinutes = 30;

        public static bool IsALeg(string tripNumber) =>
            !string.IsNullOrEmpty(tripNumber) &&
            tripNumber.IndexOf("A", StringComparison.OrdinalIgnoreCase) >= 0;

        public static int PuLateMaxMinutes(string tripNumber) =>
            IsALeg(tripNumber) ? ALegPuLateMaxMinutes : BcLegPuLateMaxMinutes;

        public static int PuEarlyMaxMinutes(string tripNumber) =>
            IsALeg(tripNumber) ? ALegPuEarlyMaxMinutes : 0;

        public static double MinutesLate(DateTime actual, DateTime scheduled) =>
            (actual - scheduled).TotalMinutes;

        public static double MinutesEarly(DateTime scheduled, DateTime actual) =>
            (scheduled - actual).TotalMinutes;

        /// <summary>
        /// Completed whole minutes that <paramref name="actual"/> is past <paramref name="scheduled"/> (floor).
        /// 14:59 past → 14; 15:00 past → 15.
        /// </summary>
        public static int WholeMinutesLate(DateTime actual, DateTime scheduled)
        {
            double secs = (actual - scheduled).TotalSeconds;
            if (secs <= 0)
                return 0;
            return (int)Math.Floor(secs / 60.0);
        }

        /// <summary>Completed whole minutes that <paramref name="actual"/> is before <paramref name="scheduled"/> (floor).</summary>
        public static int WholeMinutesEarly(DateTime scheduled, DateTime actual) =>
            WholeMinutesLate(scheduled, actual);

        /// <summary>Floor a fractional minute delta; negatives clamp to 0.</summary>
        public static int FloorMinutes(double value) =>
            (int)Math.Floor(Math.Max(0, value));

        /// <summary>
        /// Whole minutes past the allowed late window (not raw vs schedule).
        /// A-leg PU 17 after sched with 14 grace → 3.
        /// </summary>
        public static double ExcessLateMinutes(string tripNumber, double lateVsSched, bool isDo)
        {
            int late = FloorMinutes(lateVsSched);
            int grace = isDo ? DoLateMaxMinutes : PuLateMaxMinutes(tripNumber);
            return Math.Max(0, late - grace);
        }

        /// <summary>
        /// Whole minutes past the allowed early window. PU uses scoreboard early cap (A 29 / B-C 0);
        /// DO uses the lenient early flag threshold (30).
        /// </summary>
        public static double ExcessEarlyMinutes(string tripNumber, double earlyVsSched, bool isDo)
        {
            int early = FloorMinutes(earlyVsSched);
            int cap = isDo ? LenientDoEarlyMinMinutes : PuEarlyMaxMinutes(tripNumber);
            return Math.Max(0, early - cap);
        }

        /// <summary>Driver PU is within scoreboard late allowance (not early-checked).</summary>
        public static bool PuLateMinutesOk(string tripNumber, DateTime driverPu, DateTime schedPu) =>
            WholeMinutesLate(driverPu, schedPu) <= PuLateMaxMinutes(tripNumber);

        /// <summary>Driver PU early is allowed only on A-legs (up to 29 min). B/C early is never OK.</summary>
        public static bool PuEarlyMinutesOk(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            int early = WholeMinutesEarly(schedPu, driverPu);
            if (early <= 0)
                return true;
            int cap = PuEarlyMaxMinutes(tripNumber);
            return cap > 0 && early <= cap;
        }

        /// <summary>DO within scoreboard late allowance (0 whole minutes late).</summary>
        public static bool DoLateMinutesOk(DateTime driverDo, DateTime schedDo) =>
            WholeMinutesLate(driverDo, schedDo) <= DoLateMaxMinutes;

        /// <summary>Random shift cap when nudging a late PU onto scheduled time.</summary>
        public static int RandomLatePuCap(string tripNumber) => PuLateMaxMinutes(tripNumber);

        /// <summary>Random shift cap when nudging an early PU onto scheduled time (A-leg only).</summary>
        public static int RandomEarlyPuCap(string tripNumber) => PuEarlyMaxMinutes(tripNumber);

        /// <summary>
        /// Lenient PU late: ignore small lateness; only flag beyond scoreboard max (A 14 / B-C 29).
        /// </summary>
        public static bool IsLenientPuLateViolation(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            int late = WholeMinutesLate(driverPu, schedPu);
            if (late <= LenientNaturalSlackMinutes)
                return false;
            return late > PuLateMaxMinutes(tripNumber);
        }

        /// <summary>
        /// Lenient PU early: a few minutes early is fine; B/C beyond slack is severe;
        /// A-leg only if extremely early (above <see cref="LenientALegPuEarlyMinMinutes"/>).
        /// </summary>
        public static bool IsLenientPuEarlyViolation(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            int early = WholeMinutesEarly(schedPu, driverPu);
            if (early <= LenientNaturalSlackMinutes)
                return false;
            if (!IsALeg(tripNumber))
                return true;
            return early > LenientALegPuEarlyMinMinutes;
        }

        /// <summary>Lenient DO late: small delays are acceptable; only severe lateness is flagged.</summary>
        public static bool IsLenientDoLateViolation(DateTime driverDo, DateTime schedDo)
        {
            int late = WholeMinutesLate(driverDo, schedDo);
            if (late <= LenientNaturalSlackMinutes)
                return false;
            return late > LenientDoLateMinMinutes;
        }

        public static bool IsLenientDoEarlyViolation(DateTime driverDo, DateTime schedDo)
        {
            int early = WholeMinutesEarly(schedDo, driverDo);
            return early > LenientNaturalSlackMinutes && early > LenientDoEarlyMinMinutes;
        }

        /// <summary>Random nudge off scheduled time (never 0 when cap allows) for a natural-looking correction.</summary>
        public static int LenientNudgeMinutes(int maxInclusive, Random r)
        {
            if (maxInclusive <= 0)
                return 0;
            if (maxInclusive == 1)
                return 1;
            return r.Next(1, maxInclusive + 1);
        }
    }
}
