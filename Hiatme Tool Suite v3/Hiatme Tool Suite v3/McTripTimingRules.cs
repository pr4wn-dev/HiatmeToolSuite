using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modivcare / Hiatme scoreboard timing windows (same as Supey builder and website checks).
    /// PU late: A-leg 0–14 min, B/C 0–29 min. A-leg PU early: up to 29 min. DO: not late (0 min).
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

        /// <summary>Driver PU is within scoreboard late allowance (not early-checked).</summary>
        public static bool PuLateMinutesOk(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            double late = MinutesLate(driverPu, schedPu);
            if (late <= 0)
                return true;
            return late <= PuLateMaxMinutes(tripNumber);
        }

        /// <summary>Driver PU early is allowed only on A-legs (up to 29 min). B/C early is never OK.</summary>
        public static bool PuEarlyMinutesOk(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            double early = MinutesEarly(schedPu, driverPu);
            if (early <= 0)
                return true;
            int cap = PuEarlyMaxMinutes(tripNumber);
            return cap > 0 && early <= cap;
        }

        /// <summary>DO at or before scheduled (scoreboard: 0 min late).</summary>
        public static bool DoLateMinutesOk(DateTime driverDo, DateTime schedDo) =>
            MinutesLate(driverDo, schedDo) <= DoLateMaxMinutes;

        /// <summary>Random shift cap when nudging a late PU onto scheduled time.</summary>
        public static int RandomLatePuCap(string tripNumber) => PuLateMaxMinutes(tripNumber);

        /// <summary>Random shift cap when nudging an early PU onto scheduled time (A-leg only).</summary>
        public static int RandomEarlyPuCap(string tripNumber) => PuEarlyMaxMinutes(tripNumber);

        /// <summary>
        /// Lenient PU late: ignore small lateness; only flag beyond scoreboard max (A 14 / B-C 29).
        /// </summary>
        public static bool IsLenientPuLateViolation(string tripNumber, DateTime driverPu, DateTime schedPu)
        {
            double late = MinutesLate(driverPu, schedPu);
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
            double early = MinutesEarly(schedPu, driverPu);
            if (early <= LenientNaturalSlackMinutes)
                return false;
            if (!IsALeg(tripNumber))
                return true;
            return early > LenientALegPuEarlyMinMinutes;
        }

        /// <summary>Lenient DO late: small delays are acceptable; only severe lateness is flagged.</summary>
        public static bool IsLenientDoLateViolation(DateTime driverDo, DateTime schedDo)
        {
            double late = MinutesLate(driverDo, schedDo);
            if (late <= LenientNaturalSlackMinutes)
                return false;
            return late > LenientDoLateMinMinutes;
        }

        public static bool IsLenientDoEarlyViolation(DateTime driverDo, DateTime schedDo)
        {
            double early = MinutesEarly(schedDo, driverDo);
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
