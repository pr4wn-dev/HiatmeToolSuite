using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Rebuilds driver groups and OSRM routes after manual trip reorder in the Supey preview list.</summary>
    internal static class SupeyDriverPlanManualEdit
    {
        internal enum PreviewLineKind
        {
            Gap,
            Trip,
        }

        internal sealed class PreviewLine
        {
            public PreviewLineKind Kind;
            public MCDownloadedTrip Trip;
            public string GapNoteText;
        }

        /// <summary>
        /// Moves one trip in the preview line list. <paramref name="mergeOntoTarget"/> joins the
        /// dragged trip into the target trip's group (piggyback); otherwise gaps split groups.
        /// </summary>
        internal static void ApplyTripMove(
            IList<PreviewLine> lines,
            MCDownloadedTrip dragged,
            MCDownloadedTrip dropOnTargetTrip,
            int insertBeforeLineIndex,
            bool mergeOntoTarget)
        {
            if (lines == null || dragged == null) return;

            int from = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Kind == PreviewLineKind.Trip
                    && TripEquals(lines[i].Trip, dragged))
                {
                    from = i;
                    break;
                }
            }
            if (from < 0) return;

            var moving = lines[from];
            lines.RemoveAt(from);

            if (mergeOntoTarget && dropOnTargetTrip != null)
            {
                int targetLine = FindTripLine(lines, dropOnTargetTrip);
                if (targetLine < 0)
                {
                    lines.Insert(Math.Min(insertBeforeLineIndex, lines.Count), moving);
                    return;
                }
                lines.Insert(targetLine + 1, moving);
                return;
            }

            int insert = insertBeforeLineIndex;
            if (from < insert) insert--;
            insert = Math.Max(0, Math.Min(insert, lines.Count));
            lines.Insert(insert, moving);
        }

        internal static void RebuildGroupsFromLines(SupeyDriverPlan plan, IList<PreviewLine> lines)
        {
            if (plan == null || lines == null) return;

            var tripGeo = SnapshotTripGeo(plan);
            plan.Groups.Clear();

            SupeyTripCluster current = null;
            int groupNum = 0;

            void Flush()
            {
                if (current == null || current.Trips.Count == 0)
                {
                    current = null;
                    return;
                }
                groupNum++;
                current.GroupNumber = groupNum;
                current.GroupColor = SupeyGroupPalette.For(groupNum);
                plan.Groups.Add(current);
                current = null;
            }

            foreach (var line in lines)
            {
                if (line.Kind == PreviewLineKind.Gap)
                {
                    Flush();
                    continue;
                }
                if (line.Trip == null) continue;

                if (current == null)
                    current = new SupeyTripCluster();

                int idx = current.Trips.Count;
                current.Trips.Add(line.Trip);
                if (tripGeo.TryGetValue(NormalizeTn(line.Trip), out var g))
                {
                    current.PickupPoints.Add(g.Pu);
                    current.DropoffPoints.Add(g.Do);
                }
                else
                {
                    current.PickupPoints.Add(default);
                    current.DropoffPoints.Add(default);
                }
            }

            Flush();
            RenumberGroups(plan);
            SyncTemplateSlotsFromLines(plan, lines);
        }

        private static void RenumberGroups(SupeyDriverPlan plan)
        {
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                plan.Groups[i].GroupNumber = i + 1;
                plan.Groups[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }
        }

        /// <summary>Re-OSRM all drivers, refresh route notes (via list rebind), and rebuild timing warnings.</summary>
        internal static async Task RefreshEntireScheduleAsync(SupeyScheduleResult result, CancellationToken token)
        {
            if (result == null) return;
            foreach (var plan in result.DriverPlans)
            {
                token.ThrowIfCancellationRequested();
                await RefreshRoutingAsync(plan, token).ConfigureAwait(false);
            }

            SyncDriverWarningsToResult(result);
        }

        internal static void SyncDriverWarningsToResult(SupeyScheduleResult result)
        {
            if (result == null) return;
            result.BuildWarnings.RemoveAll(w =>
                w.Kind == SupeyWarningKind.LateArrival
                || w.Kind == SupeyWarningKind.TightArrival
                || w.Kind == SupeyWarningKind.StraightLineFallback);

            var algo = new SupeyScheduleAlgorithm();
            foreach (var plan in result.DriverPlans)
            {
                algo.EvaluateWarningsAndTimingsPublic(plan);
                if (plan.Warnings != null && plan.Warnings.Count > 0)
                    result.BuildWarnings.AddRange(plan.Warnings);
            }
        }

        internal static bool MoveTripBetweenDrivers(
            SupeyScheduleResult result,
            SupeyDriverPlan fromPlan,
            SupeyDriverPlan toPlan,
            MCDownloadedTrip trip)
        {
            if (fromPlan == null || toPlan == null || trip == null || ReferenceEquals(fromPlan, toPlan))
                return false;

            if (!TryRemoveTripFromPlan(fromPlan, trip))
                return false;

            TryGetTripGeoFromPlan(fromPlan, trip, out var movedPu, out var movedDo);
            AddTripAsNewGroup(toPlan, trip, movedPu, movedDo);

            string tn = (trip.TripNumber ?? "").Trim();
            if (tn.Length > 0 && result?.Locks != null && toPlan.Driver != null)
                result.Locks[tn] = toPlan.Driver.Name.Trim();

            return true;
        }

        internal static async Task RefreshRoutingAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan == null || plan.Groups.Count == 0) return;

            var algo = new SupeyScheduleAlgorithm();
            foreach (var g in plan.Groups)
            {
                token.ThrowIfCancellationRequested();
                await EnsureClusterGeoAsync(g, token).ConfigureAwait(false);
                g.RoutePolyline.Clear();
                g.PickupOrder.Clear();
                g.DropoffOrder.Clear();
                g.DropoffLegSeconds.Clear();
                g.PickupLegSeconds.Clear();
                algo.SyncClusterMetadataPublic(g);
                SupeyClusterRouting.ApplyManualEditTour(g);
                await algo.PopulateClusterPolylinePublicAsync(g, token).ConfigureAwait(false);
            }

            await algo.SequenceDriverPublicAsync(plan, token).ConfigureAwait(false);
            algo.EvaluateWarningsAndTimingsPublic(plan);
        }

        private static async Task EnsureClusterGeoAsync(SupeyTripCluster g, CancellationToken token)
        {
            if (g?.Trips == null) return;
            for (int i = 0; i < g.Trips.Count; i++)
            {
                while (g.PickupPoints.Count <= i) g.PickupPoints.Add(default);
                while (g.DropoffPoints.Count <= i) g.DropoffPoints.Add(default);

                var t = g.Trips[i];
                if (IsMissingGeo(g.PickupPoints[i]))
                {
                    var pu = await AddressGeocoder.ResolveTripEndpointAsync(t.PUStreet, t.PUCity, token)
                        .ConfigureAwait(false);
                    if (pu.HasValue) g.PickupPoints[i] = pu.Value;
                }
                if (IsMissingGeo(g.DropoffPoints[i]))
                {
                    var dout = await AddressGeocoder.ResolveTripEndpointAsync(t.DOStreet, t.DOCITY, token)
                        .ConfigureAwait(false);
                    if (dout.HasValue) g.DropoffPoints[i] = dout.Value;
                }
            }
            g.PickupCentroid = Centroid(g.PickupPoints);
            g.DropoffCentroid = Centroid(g.DropoffPoints);
        }

        private static bool IsMissingGeo(GeoPoint p) =>
            p.Lat == 0 && p.Lng == 0;

        private static GeoPoint Centroid(List<GeoPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GeoPoint(0, 0);
            double lat = 0, lng = 0;
            int n = 0;
            foreach (var p in pts)
            {
                if (IsMissingGeo(p)) continue;
                lat += p.Lat;
                lng += p.Lng;
                n++;
            }
            if (n == 0) return new GeoPoint(0, 0);
            return new GeoPoint(lat / n, lng / n);
        }

        private static bool TryRemoveTripFromPlan(SupeyDriverPlan plan, MCDownloadedTrip trip)
        {
            var lines = new List<PreviewLine>();
            if (plan.TemplateDisplaySlots != null && plan.TemplateDisplaySlots.Count > 0)
            {
                foreach (var slot in plan.TemplateDisplaySlots)
                {
                    if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                    {
                        lines.Add(new PreviewLine
                        {
                            Kind = PreviewLineKind.Gap,
                            GapNoteText = slot.NoteText ?? "",
                        });
                    }
                    else if (slot.IsMatched && slot.MatchedLiveTrip != null)
                        lines.Add(new PreviewLine { Kind = PreviewLineKind.Trip, Trip = slot.MatchedLiveTrip });
                }
            }
            else
            {
                foreach (var g in plan.Groups)
                {
                    foreach (var t in g.Trips)
                        lines.Add(new PreviewLine { Kind = PreviewLineKind.Trip, Trip = t });
                }
            }

            int before = lines.Count;
            lines.RemoveAll(l => l.Kind == PreviewLineKind.Trip && TripEquals(l.Trip, trip));
            if (lines.Count == before) return false;

            RebuildGroupsFromLines(plan, lines);
            return true;
        }

        private static void AddTripAsNewGroup(
            SupeyDriverPlan plan,
            MCDownloadedTrip trip,
            GeoPoint movedPu = default,
            GeoPoint movedDo = default)
        {
            var tripGeo = SnapshotTripGeo(plan);
            var g = new SupeyTripCluster();
            g.Trips.Add(trip);
            if (!IsMissingGeo(movedPu) || !IsMissingGeo(movedDo))
            {
                g.PickupPoints.Add(IsMissingGeo(movedPu) ? default : movedPu);
                g.DropoffPoints.Add(IsMissingGeo(movedDo) ? default : movedDo);
            }
            else if (tripGeo.TryGetValue(NormalizeTn(trip), out var geo))
            {
                g.PickupPoints.Add(geo.Pu);
                g.DropoffPoints.Add(geo.Do);
            }
            else
            {
                g.PickupPoints.Add(IsMissingGeo(movedPu) ? default : movedPu);
                g.DropoffPoints.Add(IsMissingGeo(movedDo) ? default : movedDo);
            }

            plan.Groups.Add(g);
            RenumberGroups(plan);

            if (plan.TemplateDisplaySlots != null)
            {
                plan.TemplateDisplaySlots.Add(new SupeyTemplateSlot
                {
                    Kind = SupeyTemplateSlot.SlotKind.Trip,
                    MatchedLiveTrip = trip,
                });
            }
        }

        internal static string FormatRouteHeader(SupeyTripCluster g)
        {
            if (g == null) return "";
            string note = SupeyRouteNoteFormatter.Format(g);
            double mi = g.IntraClusterMeters * 0.000621371;
            if (mi > 0.05)
                return note + " · " + mi.ToString("0.0") + " mi route";
            return note;
        }

        private static void SyncTemplateSlotsFromLines(SupeyDriverPlan plan, IList<PreviewLine> lines)
        {
            if (plan?.TemplateDisplaySlots == null || plan.TemplateDisplaySlots.Count == 0)
                return;

            var templateByTn = new Dictionary<string, MCDownloadedTrip>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in plan.TemplateDisplaySlots)
            {
                if (slot.Kind != SupeyTemplateSlot.SlotKind.Trip || slot.TemplateTrip == null)
                    continue;
                string tn = NormalizeTn(slot.MatchedLiveTrip);
                if (tn.Length > 0)
                    templateByTn[tn] = slot.TemplateTrip;
            }

            var newSlots = new List<SupeyTemplateSlot>();
            foreach (var line in lines)
            {
                if (line.Kind == PreviewLineKind.Gap)
                {
                    newSlots.Add(new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.Gap,
                        NoteText = line.GapNoteText ?? "",
                    });
                    continue;
                }
                if (line.Trip == null) continue;
                string key = NormalizeTn(line.Trip);
                templateByTn.TryGetValue(key, out var tpl);
                newSlots.Add(new SupeyTemplateSlot
                {
                    Kind = SupeyTemplateSlot.SlotKind.Trip,
                    MatchedLiveTrip = line.Trip,
                    TemplateTrip = tpl,
                });
            }
            plan.TemplateDisplaySlots = newSlots;
        }

        private static bool TryGetTripGeoFromPlan(
            SupeyDriverPlan plan,
            MCDownloadedTrip trip,
            out GeoPoint pu,
            out GeoPoint dout)
        {
            pu = default;
            dout = default;
            if (plan == null || trip == null) return false;
            foreach (var g in plan.Groups)
            {
                for (int i = 0; i < g.Trips.Count; i++)
                {
                    if (!TripEquals(g.Trips[i], trip)) continue;
                    if (i < g.PickupPoints.Count) pu = g.PickupPoints[i];
                    if (i < g.DropoffPoints.Count) dout = g.DropoffPoints[i];
                    return !IsMissingGeo(pu) || !IsMissingGeo(dout);
                }
            }
            return false;
        }

        private static Dictionary<string, (GeoPoint Pu, GeoPoint Do)> SnapshotTripGeo(SupeyDriverPlan plan)
        {
            var map = new Dictionary<string, (GeoPoint, GeoPoint)>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in plan.Groups)
            {
                for (int i = 0; i < g.Trips.Count; i++)
                {
                    string tn = NormalizeTn(g.Trips[i]);
                    if (tn.Length == 0) continue;
                    GeoPoint pu = i < g.PickupPoints.Count ? g.PickupPoints[i] : default;
                    GeoPoint dout = i < g.DropoffPoints.Count ? g.DropoffPoints[i] : default;
                    map[tn] = (pu, dout);
                }
            }
            return map;
        }

        private static int FindTripLine(IList<PreviewLine> lines, MCDownloadedTrip trip)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Kind == PreviewLineKind.Trip && TripEquals(lines[i].Trip, trip))
                    return i;
            }
            return -1;
        }

        private static bool TripEquals(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            return string.Equals(a.TripNumber, b.TripNumber, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTn(MCDownloadedTrip t) => (t?.TripNumber ?? "").Trim();
    }
}
