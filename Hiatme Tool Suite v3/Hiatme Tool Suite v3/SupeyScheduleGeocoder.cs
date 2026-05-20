using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Fill PU/DO coordinates on AI-built plans so the map and Geo column work.</summary>
    internal static class SupeyScheduleGeocoder
    {
        public static async Task HydrateResultAsync(SupeyScheduleResult result, CancellationToken token = default)
        {
            if (result == null) return;
            if (result.DriverPlans != null)
            {
                foreach (var plan in result.DriverPlans)
                {
                    if (plan == null) continue;
                    await HydratePlanAsync(plan, token).ConfigureAwait(false);
                }
            }
        }

        public static async Task HydratePlanAsync(SupeyDriverPlan plan, CancellationToken token = default)
        {
            if (plan == null) return;

            if (plan.Driver != null && !plan.HomeGeo.HasValue)
            {
                var home = await AddressGeocoder.ResolveWithFallbacksAsync(
                    plan.Driver.HomeStreet,
                    plan.Driver.HomeCity,
                    plan.Driver.HomeState,
                    plan.Driver.HomeZip,
                    "us",
                    token).ConfigureAwait(false);
                if (home.HasValue)
                    plan.HomeGeo = home;
            }

            if (plan.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null) continue;
                g.PickupPoints.Clear();
                g.DropoffPoints.Clear();
                foreach (var t in g.Trips)
                {
                    token.ThrowIfCancellationRequested();
                    if (t == null) continue;
                    var pu = await AddressGeocoder.ResolveTripEndpointAsync(
                        t.PUStreet, t.PUCity, token).ConfigureAwait(false);
                    var dof = await AddressGeocoder.ResolveTripEndpointAsync(
                        t.DOStreet, t.DOCITY, token).ConfigureAwait(false);
                    g.PickupPoints.Add(pu ?? new GeoPoint(0, 0));
                    g.DropoffPoints.Add(dof ?? new GeoPoint(0, 0));
                }
            }
        }
    }
}
