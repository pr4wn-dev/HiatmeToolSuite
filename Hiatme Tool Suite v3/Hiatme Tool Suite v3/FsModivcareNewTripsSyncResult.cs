using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal enum FsModivcareNewTripsSyncFailure
    {
        None,
        NoSchedule,
        ModivcareUnavailable,
        DownloadFailed,
        NoModivcareTrips,
    }

    internal sealed class FsModivcareNewTripsAddedEntry
    {
        public MCDownloadedTrip Trip { get; set; }
        public ScheduleBuilderReserveBuckets.ReserveBucket Bucket { get; set; }
    }

    internal sealed class FsModivcareNewTripsSyncResult
    {
        public DateTime ServiceDate { get; private set; }
        public string StatusNote { get; private set; }
        public int ModivcareTripCount { get; private set; }
        public int SkippedOnSchedule { get; private set; }
        public int SkippedRerouted { get; private set; }
        public IReadOnlyList<FsModivcareNewTripsAddedEntry> Added { get; private set; }
            = Array.Empty<FsModivcareNewTripsAddedEntry>();
        public FsModivcareNewTripsSyncFailure Failure { get; private set; }

        public bool HasAddedTrips => Added != null && Added.Count > 0;

        public static FsModivcareNewTripsSyncResult EmptySchedule(DateTime serviceDate) =>
            new FsModivcareNewTripsSyncResult
            {
                ServiceDate = serviceDate,
                StatusNote = "",
                Failure = FsModivcareNewTripsSyncFailure.NoSchedule,
            };

        public static FsModivcareNewTripsSyncResult Skipped(
            DateTime serviceDate,
            FsModivcareNewTripsSyncFailure failure,
            string statusNote,
            int modivcareTripCount = 0,
            int skippedOnSchedule = 0,
            int skippedRerouted = 0) =>
            new FsModivcareNewTripsSyncResult
            {
                ServiceDate = serviceDate,
                Failure = failure,
                StatusNote = statusNote,
                ModivcareTripCount = modivcareTripCount,
                SkippedOnSchedule = skippedOnSchedule,
                SkippedRerouted = skippedRerouted,
            };

        public static FsModivcareNewTripsSyncResult Completed(
            DateTime serviceDate,
            string statusNote,
            int modivcareTripCount,
            int skippedOnSchedule,
            int skippedRerouted,
            IReadOnlyList<FsModivcareNewTripsAddedEntry> added) =>
            new FsModivcareNewTripsSyncResult
            {
                ServiceDate = serviceDate,
                Failure = FsModivcareNewTripsSyncFailure.None,
                StatusNote = statusNote,
                ModivcareTripCount = modivcareTripCount,
                SkippedOnSchedule = skippedOnSchedule,
                SkippedRerouted = skippedRerouted,
                Added = added ?? Array.Empty<FsModivcareNewTripsAddedEntry>(),
            };
    }
}
