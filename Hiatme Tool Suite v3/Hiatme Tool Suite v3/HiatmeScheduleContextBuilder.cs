using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    internal static class HiatmeScheduleContextBuilder
    {
        public static JObject Build(
            DateTime serviceDate,
            IList<SupeyDriverProfile> roster,
            IList<MCDownloadedTrip> trips,
            SupeyScheduleResult result,
            bool includeAssignment,
            IList<SupeyDriverProfile> selectedDrivers = null)
        {
            var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedDrivers != null)
            {
                foreach (var d in selectedDrivers)
                {
                    if (d != null && !string.IsNullOrWhiteSpace(d.Name))
                        selectedNames.Add(d.Name);
                }
            }
            var ctx = new JObject
            {
                ["service_date"] = serviceDate.ToString("yyyy-MM-dd"),
                ["trip_count"] = trips?.Count ?? 0,
                ["roster"] = new JArray(),
                ["trips"] = new JArray(),
            };

            if (roster != null)
            {
                foreach (var d in roster)
                {
                    if (d == null) continue;
                    bool selected = selectedNames.Count == 0
                        || selectedNames.Contains(d.Name ?? "");
                    ((JArray)ctx["roster"]).Add(new JObject
                    {
                        ["name"] = Trunc(d.Name, 80),
                        ["capacity"] = d.CapacityPassengers,
                        ["shift_start"] = d.ShiftStart ?? "",
                        ["shift_end"] = d.ShiftEnd ?? "",
                        ["home_street"] = Trunc(d.HomeStreet ?? "", 80),
                        ["home_city"] = Trunc(d.HomeCity ?? "", 40),
                        ["home_state"] = Trunc(d.HomeState ?? "", 8),
                        ["home_zip"] = Trunc(d.HomeZip ?? "", 12),
                        ["home"] = Trunc(d.FormatHomeOneLine(), 120),
                        ["wellryde_sec_id"] = d.WellRydeSecId ?? "",
                        ["selected"] = selected,
                    });
                }
            }

            if (trips != null)
            {
                foreach (var t in trips.Take(500))
                {
                    if (t == null) continue;
                    ((JArray)ctx["trips"]).Add(new JObject
                    {
                        ["trip_number"] = t.TripNumber ?? "",
                        ["client"] = Trunc(t.ClientFullName ?? "", 60),
                        ["pu_time"] = t.PUTime ?? "",
                        ["pu_street"] = Trunc(t.PUStreet ?? "", 80),
                        ["pu_city"] = Trunc(t.PUCity ?? "", 40),
                        ["do_time"] = t.DOTime ?? t.SchedDOTime ?? "",
                        ["do_street"] = Trunc(t.DOStreet ?? "", 80),
                        ["do_city"] = Trunc(t.DOCITY ?? "", 40),
                        ["miles"] = t.Miles ?? "",
                        ["alerts"] = t.GetAlerts() ?? "",
                    });
                }
            }

            if (includeAssignment && result != null)
            {
                ctx["geocode"] = new JObject
                {
                    ["cache_hits"] = AddressGeocoder.CacheHits,
                    ["cache_misses_resolved_via_nominatim"] = AddressGeocoder.CacheMisses,
                    ["cache_total_entries"] = AddressGeocoder.CacheCount,
                    ["note"] = "Tool Suite already checks the local + AI server cache before " +
                              "calling Nominatim; addresses unresolved after that show up under " +
                              "warnings_text as 'MissingGeo' and were sent to reserves.",
                };
                var assign = new JArray();
                foreach (var plan in result.DriverPlans)
                {
                    if (plan?.Driver == null) continue;
                    foreach (var g in plan.Groups)
                    {
                        foreach (var trip in g.Trips)
                        {
                            assign.Add(new JObject
                            {
                                ["trip_number"] = trip.TripNumber ?? "",
                                ["driver_name"] = plan.Driver.Name ?? "",
                            });
                        }
                    }
                }
                ctx["assignment"] = assign;
                ctx["reserves"] = new JArray(
                    result.Reserves.Select(t => t?.TripNumber ?? "").Where(x => !string.IsNullOrEmpty(x)));
                ctx["warning_count"] = result.WarningCount;
                ctx["locks"] = JObject.FromObject(result.Locks ?? new Dictionary<string, string>());

                var warningsText = new JArray();
                if (result.BuildWarnings != null)
                {
                    foreach (var w in result.BuildWarnings.Take(20))
                    {
                        if (w == null) continue;
                        warningsText.Add(Trunc(w.Detail ?? "", 200));
                    }
                }
                foreach (var p in result.DriverPlans)
                {
                    if (p?.Warnings == null) continue;
                    foreach (var w in p.Warnings.Take(6))
                    {
                        if (w == null) continue;
                        string who = p.Driver?.Name ?? "";
                        warningsText.Add(Trunc((string.IsNullOrEmpty(who) ? "" : who + ": ") + (w.Detail ?? ""), 200));
                        if (warningsText.Count >= 24) break;
                    }
                    if (warningsText.Count >= 24) break;
                }
                ctx["warnings_text"] = warningsText;

                var schedDrivers = new JArray();
                foreach (var plan in result.DriverPlans)
                {
                    if (plan?.Driver == null) continue;
                    var nums = new JArray();
                    var groups = new JArray();
                    foreach (var g in plan.Groups)
                    {
                        var groupNums = new JArray();
                        foreach (var trip in g.Trips)
                        {
                            if (!string.IsNullOrWhiteSpace(trip?.TripNumber))
                            {
                                nums.Add(trip.TripNumber);
                                groupNums.Add(trip.TripNumber);
                            }
                        }
                        groups.Add(new JObject
                        {
                            ["group_number"] = g.GroupNumber,
                            ["label"] = "G" + g.GroupNumber,
                            ["trip_numbers"] = groupNums,
                            ["earliest_pickup"] = g.EarliestPickup.ToString(),
                            ["latest_pickup"] = g.LatestPickup.ToString(),
                            ["rider_count"] = g.RiderCount,
                        });
                    }
                    schedDrivers.Add(new JObject
                    {
                        ["driver_name"] = plan.Driver.Name ?? "",
                        ["trip_numbers"] = nums,
                        ["groups"] = groups,
                        ["first_pickup"] = plan.FirstPickup?.ToString() ?? "",
                        ["last_dropoff"] = plan.LastDropoff?.ToString() ?? "",
                        ["release_time"] = plan.ReleaseTimeOfDay?.ToString() ?? "",
                        ["rider_count"] = plan.RiderCount,
                        ["total_miles"] = Math.Round(plan.TotalMeters / 1609.344, 1),
                        ["warning_count"] = plan.Warnings?.Count ?? 0,
                    });
                }
                ctx["current_schedule"] = new JObject
                {
                    ["drivers"] = schedDrivers,
                    ["reserves"] = ctx["reserves"],
                    ["warnings_text"] = warningsText,
                };
            }

            return ctx;
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
