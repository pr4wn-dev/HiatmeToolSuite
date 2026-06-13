using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleBuilderReroutedTripRecord
    {
        [JsonProperty("trip_number")]
        public string TripNumber { get; set; }

        [JsonProperty("leg")]
        public string Leg { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("client_first_name")]
        public string ClientFirstName { get; set; }

        [JsonProperty("client_last_name")]
        public string ClientLastName { get; set; }

        [JsonProperty("client_full_name")]
        public string ClientFullName { get; set; }

        [JsonProperty("pu_street")]
        public string PUStreet { get; set; }

        [JsonProperty("pu_city")]
        public string PUCity { get; set; }

        [JsonProperty("pu_telephone")]
        public string PUTelephone { get; set; }

        [JsonProperty("pu_time")]
        public string PUTime { get; set; }

        [JsonProperty("do_street")]
        public string DOStreet { get; set; }

        [JsonProperty("do_city")]
        public string DOCITY { get; set; }

        [JsonProperty("do_telephone")]
        public string DOTelephone { get; set; }

        [JsonProperty("do_time")]
        public string DOTime { get; set; }

        [JsonProperty("sched_do_time")]
        public string SchedDOTime { get; set; }

        [JsonProperty("age")]
        public string Age { get; set; }

        [JsonProperty("miles")]
        public string Miles { get; set; }

        [JsonProperty("comments")]
        public string Comments { get; set; }

        [JsonProperty("rerouted_at")]
        public string ReroutedAt { get; set; }

        [JsonProperty("rerouted_by")]
        public string ReroutedBy { get; set; }

        public static ScheduleBuilderReroutedTripRecord FromTrip(MCDownloadedTrip trip, string reroutedBy)
        {
            if (trip == null)
                return null;

            string full = (trip.ClientFullName ?? "").Trim();
            if (full.Length == 0)
            {
                full = ((trip.ClientFirstName ?? "").Trim() + " " + (trip.ClientLastName ?? "").Trim()).Trim();
            }

            return new ScheduleBuilderReroutedTripRecord
            {
                TripNumber = (trip.TripNumber ?? "").Trim(),
                Date = (trip.Date ?? "").Trim(),
                ClientFirstName = (trip.ClientFirstName ?? "").Trim(),
                ClientLastName = (trip.ClientLastName ?? "").Trim(),
                ClientFullName = full,
                PUStreet = (trip.PUStreet ?? "").Trim(),
                PUCity = (trip.PUCity ?? "").Trim(),
                PUTelephone = (trip.PUTelephone ?? "").Trim(),
                PUTime = (trip.PUTime ?? "").Trim(),
                DOStreet = (trip.DOStreet ?? "").Trim(),
                DOCITY = (trip.DOCITY ?? "").Trim(),
                DOTelephone = (trip.DOTelephone ?? "").Trim(),
                DOTime = (trip.DOTime ?? "").Trim(),
                SchedDOTime = (trip.SchedDOTime ?? "").Trim(),
                Age = (trip.Age ?? "").Trim(),
                Miles = (trip.Miles ?? "").Trim(),
                Comments = (trip.Comments ?? "").Trim(),
                ReroutedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ReroutedBy = (reroutedBy ?? "").Trim(),
            };
        }

        public MCDownloadedTrip ToTrip()
        {
            string comments = (Comments ?? "").Trim();
            if (comments.Length == 0)
                comments = "(Rerouted on Modivcare — not in download)";

            return new MCDownloadedTrip
            {
                TripNumber = (TripNumber ?? "").Trim(),
                Date = (Date ?? "").Trim(),
                ClientFirstName = (ClientFirstName ?? "").Trim(),
                ClientLastName = (ClientLastName ?? "").Trim(),
                ClientFullName = (ClientFullName ?? "").Trim(),
                DriverNameParsed = "Reserves",
                PUStreet = (PUStreet ?? "").Trim(),
                PUCity = (PUCity ?? "").Trim(),
                PUTelephone = (PUTelephone ?? "").Trim(),
                PUTime = (PUTime ?? "").Trim(),
                DOStreet = (DOStreet ?? "").Trim(),
                DOCITY = (DOCITY ?? "").Trim(),
                DOTelephone = (DOTelephone ?? "").Trim(),
                DOTime = (DOTime ?? "").Trim(),
                SchedDOTime = (SchedDOTime ?? "").Trim(),
                Age = (Age ?? "").Trim(),
                Miles = (Miles ?? "").Trim(),
                Comments = comments,
                Assignable = false,
            };
        }
    }

    internal sealed class RerouteRegistryMergeResult
    {
        public int GhostsAdded { get; set; }
        public bool UsedServer { get; set; }
        public bool ServerUnreachable { get; set; }
        public List<string> ReroutedTripNumbers { get; set; } = new List<string>();
    }

    internal sealed class RerouteRegistryRecordResult
    {
        public bool LocalSaved { get; set; }
        public bool ServerSaved { get; set; }
        public bool ServerUnreachable { get; set; }
    }

    /// <summary>
    /// Office-panel registry of Modivcare reroutes — ghost trips for desks that BUILD after reroute.
    /// </summary>
    internal static class ScheduleBuilderReroutedTripsRegistry
    {
        public static string LocalConfigDirectory =>
            Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config", "rerouted_trips");

        public static string LocalPathForDate(DateTime serviceDate) =>
            Path.Combine(LocalConfigDirectory, serviceDate.ToString("yyyy-MM-dd") + ".json");

        public static string FormatServiceDate(DateTime serviceDate) =>
            serviceDate.Date.ToString("yyyy-MM-dd");

        public static HashSet<string> UnionReroutedTripNumbers(
            IEnumerable<string> first,
            IEnumerable<string> second)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (first != null)
            {
                foreach (var raw in first)
                    AddTripNumberKey(set, raw);
            }
            if (second != null)
            {
                foreach (var raw in second)
                    AddTripNumberKey(set, raw);
            }
            return set;
        }

        private static void AddTripNumberKey(ISet<string> keys, string tripNumber) =>
            ScheduleBuilderReroutedTrips.AddTripNumberKey(keys, tripNumber);

        public static async Task<RerouteRegistryRecordResult> RecordRerouteAsync(
            HiatmeAiSettings settings,
            DateTime serviceDate,
            MCDownloadedTrip trip,
            string reroutedBy,
            CancellationToken cancellationToken = default)
        {
            var result = new RerouteRegistryRecordResult();
            var record = ScheduleBuilderReroutedTripRecord.FromTrip(trip, reroutedBy);
            if (record == null || string.IsNullOrWhiteSpace(record.TripNumber))
                return result;

            var trips = LoadLocal(serviceDate);
            UpsertRecord(trips, record);
            result.LocalSaved = TrySaveLocal(serviceDate, trips);

            if (HiatmeGeoSettings.UseServer && settings != null
                && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                result.ServerSaved = await HiatmeAiClient.AddReroutedTripAsync(
                    settings, serviceDate, record, reroutedBy, cancellationToken).ConfigureAwait(false);
                result.ServerUnreachable = !result.ServerSaved;
                if (result.ServerSaved)
                    TrySaveLocal(serviceDate, trips);
            }

            return result;
        }

        public static async Task<RerouteRegistryMergeResult> MergeIntoBuilderAsync(
            FullScheduleBuilder builder,
            DateTime serviceDate,
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            var merge = new RerouteRegistryMergeResult();
            if (builder == null)
                return merge;

            var records = LoadLocal(serviceDate);

            if (HiatmeGeoSettings.UseServer && settings != null
                && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                merge.UsedServer = true;
                try
                {
                    var serverRecords = await HiatmeAiClient.GetReroutedTripsAsync(
                        settings, serviceDate, cancellationToken).ConfigureAwait(false);
                    if (serverRecords != null)
                    {
                        records = MergeRecordLists(records, serverRecords);
                        TrySaveLocal(serviceDate, records);
                    }
                    else
                        merge.ServerUnreachable = true;
                }
                catch
                {
                    merge.ServerUnreachable = true;
                }
            }


            foreach (var record in records)
            {
                string tn = ScheduleBuilderReroutedTrips.TripNumberKey(record?.TripNumber);
                if (tn.Length == 0)
                    continue;

                merge.ReroutedTripNumbers.Add(tn);

                var ghost = record.ToTrip();
                var existing = builder.FindTripInPreviewByNumber(record.TripNumber);
                if (existing != null)
                    existing.MergeMissingScheduleFieldsFrom(ghost);

                if (builder.TripExistsInPreview(tn))
                {
                    if (existing != null)
                        builder.MoveTripToPreviewReservesReroute(existing);
                    continue;
                }

                if (builder.TryAddSharedReroutedGhost(ghost))
                    merge.GhostsAdded++;
            }

            return merge;
        }

        public static void MarkReroutedOnPreview(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IEnumerable<string> reroutedTripNumbers)
        {
            if (linesByTab == null || reroutedTripNumbers == null)
                return;

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in reroutedTripNumbers)
                ScheduleBuilderReroutedTrips.AddTripNumberKey(keys, raw);
            if (keys.Count == 0)
                return;

            foreach (var kv in linesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;
                foreach (var line in lines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;
                    if (ScheduleBuilderReroutedTrips.TripNumberKeySetContains(keys, line.Trip.TripNumber))
                        line.ReroutedOnModivcare = true;
                }
            }
        }

        public static async Task<List<ScheduleBuilderReroutedTripRecord>> FetchForDateAsync(
            HiatmeAiSettings settings,
            DateTime serviceDate,
            CancellationToken cancellationToken = default)
        {
            if (HiatmeGeoSettings.UseServer && settings != null
                && !string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                try
                {
                    var server = await HiatmeAiClient.GetReroutedTripsAsync(
                        settings, serviceDate, cancellationToken).ConfigureAwait(false);
                    if (server != null)
                    {
                        TrySaveLocal(serviceDate, server);
                        return server;
                    }
                }
                catch
                {
                    // fall through to local
                }
            }

            return LoadLocal(serviceDate);
        }

        public static bool TrySaveLocal(DateTime serviceDate, IList<ScheduleBuilderReroutedTripRecord> trips)
        {
            try
            {
                string path = LocalPathForDate(serviceDate);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var doc = new JObject
                {
                    ["version"] = 1,
                    ["service_date"] = FormatServiceDate(serviceDate),
                    ["trips"] = JArray.FromObject(trips ?? new List<ScheduleBuilderReroutedTripRecord>()),
                    ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                };
                File.WriteAllText(path, doc.ToString(Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static List<ScheduleBuilderReroutedTripRecord> LoadLocal(DateTime serviceDate)
        {
            try
            {
                string path = LocalPathForDate(serviceDate);
                if (!File.Exists(path))
                    return new List<ScheduleBuilderReroutedTripRecord>();

                var root = JObject.Parse(File.ReadAllText(path));
                var arr = root["trips"] as JArray;
                if (arr == null || arr.Count == 0)
                    return new List<ScheduleBuilderReroutedTripRecord>();

                return arr.ToObject<List<ScheduleBuilderReroutedTripRecord>>()
                    ?? new List<ScheduleBuilderReroutedTripRecord>();
            }
            catch
            {
                return new List<ScheduleBuilderReroutedTripRecord>();
            }
        }

        /// <summary>Union local + server reroute records (server wins field conflicts).</summary>
        internal static List<ScheduleBuilderReroutedTripRecord> MergeRecordLists(
            IList<ScheduleBuilderReroutedTripRecord> local,
            IList<ScheduleBuilderReroutedTripRecord> server)
        {
            var merged = new List<ScheduleBuilderReroutedTripRecord>();
            if (local != null)
            {
                foreach (var record in local)
                {
                    if (record != null)
                        UpsertRecord(merged, record);
                }
            }
            if (server != null)
            {
                foreach (var record in server)
                {
                    if (record != null)
                        UpsertRecord(merged, record);
                }
            }
            return merged;
        }

        private static void UpsertRecord(
            IList<ScheduleBuilderReroutedTripRecord> trips,
            ScheduleBuilderReroutedTripRecord record)
        {
            if (trips == null || record == null)
                return;

            string key = ScheduleBuilderReroutedTrips.TripNumberKey(record.TripNumber);
            for (int i = 0; i < trips.Count; i++)
            {
                if (ScheduleBuilderReroutedTrips.TripNumberKeysMatch(trips[i]?.TripNumber, key))
                {
                    trips[i] = record;
                    return;
                }
            }
            trips.Add(record);
        }

        private static string NormalizeTripNumber(string raw) =>
            ScheduleBuilderReroutedTrips.TripNumberKey(raw);
    }
}
