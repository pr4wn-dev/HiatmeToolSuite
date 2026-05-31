using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One OSRM table per ride-share group — 2-opt compares tours via matrix, not per-swap /route HTTP.
    /// </summary>
    internal sealed class SupeyClusterOsrmTable
    {
        private readonly int[] _puPointIndex;
        private readonly int[] _doPointIndex;
        private readonly double?[,] _meters;
        private readonly double?[,] _seconds;
        private readonly Dictionary<long, int> _keyToIndex;

        private SupeyClusterOsrmTable(
            int[] puPointIndex,
            int[] doPointIndex,
            double?[,] meters,
            double?[,] seconds,
            Dictionary<long, int> keyToIndex)
        {
            _puPointIndex = puPointIndex;
            _doPointIndex = doPointIndex;
            _meters = meters;
            _seconds = seconds;
            _keyToIndex = keyToIndex;
        }

        internal static SupeyClusterOsrmTable Current { get; private set; }

        internal static void Clear() => Current = null;

        internal static async Task<bool> TryBindClusterAsync(SupeyTripCluster c, CancellationToken token)
        {
            Clear();
            if (c == null || c.Trips.Count < 2) return false;
            var built = await BuildAsync(c, token).ConfigureAwait(false);
            if (built == null) return false;
            Current = built;
            return true;
        }

        /// <summary>Recompute intra metrics + per-DO legs after trip row reorder (display / desk order).</summary>
        internal static async Task<bool> TryRefreshTourMetricsAsync(SupeyTripCluster c, CancellationToken token)
        {
            if (c == null || c.Trips.Count < 2
                || c.PickupOrder.Count == 0 || c.DropoffOrder.Count == 0)
                return false;
            try
            {
                if (!await TryBindClusterAsync(c, token).ConfigureAwait(false))
                    return false;
                return Current != null && Current.TryApplyTourMetrics(c);
            }
            finally
            {
                Clear();
            }
        }

        internal static async Task<SupeyClusterOsrmTable> BuildAsync(
            SupeyTripCluster c, CancellationToken token)
        {
            int n = c.Trips.Count;
            if (n < 2 || c.PickupPoints == null || c.DropoffPoints == null
                || c.PickupPoints.Count < n || c.DropoffPoints.Count < n)
                return null;

            var points = new List<GeoPoint>(n * 2);
            var keyToIndex = new Dictionary<long, int>();
            var puIdx = new int[n];
            var doIdx = new int[n];

            for (int i = 0; i < n; i++)
            {
                puIdx[i] = IndexPoint(points, keyToIndex, c.PickupPoints[i]);
                doIdx[i] = IndexPoint(points, keyToIndex, c.DropoffPoints[i]);
            }

            if (points.Count < 2) return null;

            var matrix = await FetchMatrixAsync(points, token).ConfigureAwait(false);
            if (matrix == null) return null;

            return new SupeyClusterOsrmTable(
                puIdx, doIdx, matrix.Value.meters, matrix.Value.seconds, keyToIndex);
        }

        internal double? Meters(GeoPoint a, GeoPoint b)
        {
            if (!TryMatrixIndices(a, b, out int i, out int j)) return null;
            return CellOrZero(_meters, i, j);
        }

        internal double? Seconds(GeoPoint a, GeoPoint b)
        {
            if (!TryMatrixIndices(a, b, out int i, out int j)) return null;
            return CellOrZero(_seconds, i, j);
        }

        private static double? CellOrZero(double?[,] grid, int i, int j)
        {
            if (grid == null) return null;
            var v = grid[i, j];
            if (v.HasValue) return v;
            return i == j ? 0.0 : (double?)null;
        }

        private static async Task<(double?[,] meters, double?[,] seconds)?> FetchMatrixAsync(
            List<GeoPoint> points, CancellationToken token)
        {
            if (HiatmeGeoSettings.UseServer)
            {
                var ai = HiatmeAiSettings.Load();
                var matrix = await HiatmeGeoClient.FetchOsrmTableAsync(ai, points, token)
                    .ConfigureAwait(false);
                if (matrix != null) return matrix;
            }
            return await OsrmTableLocal.FetchAsync(points, token).ConfigureAwait(false);
        }

        internal double? TourMeters(SupeyTripCluster c, List<int> puOrder, List<int> doOrder)
        {
            if (c == null || puOrder == null || doOrder == null
                || puOrder.Count == 0 || doOrder.Count == 0)
                return null;

            double total = 0;
            for (int i = 1; i < puOrder.Count; i++)
            {
                var leg = LegMetersPu(puOrder[i - 1], puOrder[i]);
                if (!leg.HasValue) return null;
                total += leg.Value;
            }

            int lastPu = puOrder[puOrder.Count - 1];
            var puDo = LegMetersPuDo(lastPu, doOrder[0]);
            if (!puDo.HasValue) return null;
            total += puDo.Value;

            for (int i = 1; i < doOrder.Count; i++)
            {
                var leg = LegMetersDo(doOrder[i - 1], doOrder[i]);
                if (!leg.HasValue) return null;
                total += leg.Value;
            }

            return total;
        }

        internal bool TryApplyTourMetrics(SupeyTripCluster c)
        {
            if (c == null || c.PickupOrder.Count == 0 || c.DropoffOrder.Count == 0)
                return false;
            var meters = TourMeters(c, c.PickupOrder, c.DropoffOrder);
            var seconds = TourSeconds(c, c.PickupOrder, c.DropoffOrder);
            if (!meters.HasValue) return false;
            c.IntraClusterMeters = meters.Value;
            c.IntraClusterDriveSeconds = seconds ?? 0;
            c.IsStraightLineFallback = false;
            FillPickupLegSecondsFromTable(c);
            FillDropoffLegSecondsFromTable(c);
            return true;
        }

        private void FillPickupLegSecondsFromTable(SupeyTripCluster c)
        {
            if (c == null) return;
            c.PickupLegSeconds.Clear();
            if (c.PickupOrder == null || c.PickupOrder.Count < 2)
                return;
            for (int i = 1; i < c.PickupOrder.Count; i++)
            {
                var leg = LegSecondsPu(c.PickupOrder[i - 1], c.PickupOrder[i]);
                c.PickupLegSeconds.Add(leg ?? 0);
            }
        }

        private void FillDropoffLegSecondsFromTable(SupeyTripCluster c)
        {
            if (c == null || c.DropoffOrder == null || c.DropoffOrder.Count == 0)
                return;
            c.DropoffLegSeconds.Clear();
            int n = c.DropoffOrder.Count;
            if (c.PickupOrder == null || c.PickupOrder.Count == 0)
            {
                double per = n > 0 && c.IntraClusterDriveSeconds > 0
                    ? c.IntraClusterDriveSeconds / n : 0;
                for (int i = 0; i < n; i++) c.DropoffLegSeconds.Add(per);
                c.TailDriveSeconds = c.IntraClusterDriveSeconds;
                return;
            }

            int lastPu = c.PickupOrder[c.PickupOrder.Count - 1];
            double tail = 0;
            var firstDo = LegSecondsPuDo(lastPu, c.DropoffOrder[0]);
            if (firstDo.HasValue)
            {
                tail += firstDo.Value;
                c.DropoffLegSeconds.Add(firstDo.Value);
            }
            else
                c.DropoffLegSeconds.Add(0);

            for (int i = 1; i < n; i++)
            {
                var leg = LegSecondsDo(c.DropoffOrder[i - 1], c.DropoffOrder[i]);
                double sec = leg ?? 0;
                tail += sec;
                c.DropoffLegSeconds.Add(sec);
            }
            if (tail <= 0 && c.IntraClusterDriveSeconds > 0)
            {
                c.DropoffLegSeconds.Clear();
                double per = c.IntraClusterDriveSeconds / n;
                for (int i = 0; i < n; i++) c.DropoffLegSeconds.Add(per);
                tail = c.IntraClusterDriveSeconds;
            }
            c.TailDriveSeconds = tail;
        }

        internal double? TourSeconds(SupeyTripCluster c, List<int> puOrder, List<int> doOrder)
        {
            if (c == null || puOrder == null || doOrder == null
                || puOrder.Count == 0 || doOrder.Count == 0)
                return null;

            double total = 0;
            for (int i = 1; i < puOrder.Count; i++)
            {
                var leg = LegSecondsPu(puOrder[i - 1], puOrder[i]);
                if (!leg.HasValue) return null;
                total += leg.Value;
            }

            int lastPu = puOrder[puOrder.Count - 1];
            var puDo = LegSecondsPuDo(lastPu, doOrder[0]);
            if (!puDo.HasValue) return null;
            total += puDo.Value;

            for (int i = 1; i < doOrder.Count; i++)
            {
                var leg = LegSecondsDo(doOrder[i - 1], doOrder[i]);
                if (!leg.HasValue) return null;
                total += leg.Value;
            }

            return total;
        }

        private double? LegSecondsPu(int tripA, int tripB)
        {
            if (tripA < 0 || tripB < 0 || tripA >= _puPointIndex.Length || tripB >= _puPointIndex.Length)
                return null;
            int i = _puPointIndex[tripA];
            int j = _puPointIndex[tripB];
            return CellOrZero(_seconds, i, j);
        }

        private double? LegSecondsPuDo(int tripPu, int tripDo)
        {
            if (tripPu < 0 || tripDo < 0 || tripPu >= _puPointIndex.Length || tripDo >= _doPointIndex.Length)
                return null;
            int i = _puPointIndex[tripPu];
            int j = _doPointIndex[tripDo];
            return CellOrZero(_seconds, i, j);
        }

        private double? LegSecondsDo(int tripA, int tripB)
        {
            if (tripA < 0 || tripB < 0 || tripA >= _doPointIndex.Length || tripB >= _doPointIndex.Length)
                return null;
            int i = _doPointIndex[tripA];
            int j = _doPointIndex[tripB];
            return CellOrZero(_seconds, i, j);
        }

        private double? LegMetersPu(int tripA, int tripB)
        {
            if (tripA < 0 || tripB < 0 || tripA >= _puPointIndex.Length || tripB >= _puPointIndex.Length)
                return null;
            int i = _puPointIndex[tripA];
            int j = _puPointIndex[tripB];
            return CellOrZero(_meters, i, j);
        }

        private double? LegMetersPuDo(int tripPu, int tripDo)
        {
            if (tripPu < 0 || tripDo < 0 || tripPu >= _puPointIndex.Length || tripDo >= _doPointIndex.Length)
                return null;
            int i = _puPointIndex[tripPu];
            int j = _doPointIndex[tripDo];
            return CellOrZero(_meters, i, j);
        }

        private double? LegMetersDo(int tripA, int tripB)
        {
            if (tripA < 0 || tripB < 0 || tripA >= _doPointIndex.Length || tripB >= _doPointIndex.Length)
                return null;
            int i = _doPointIndex[tripA];
            int j = _doPointIndex[tripB];
            return CellOrZero(_meters, i, j);
        }

        private bool TryMatrixIndices(GeoPoint a, GeoPoint b, out int i, out int j)
        {
            i = j = -1;
            if (_keyToIndex == null) return false;
            if (!_keyToIndex.TryGetValue(PointKey(a), out i)) return false;
            if (!_keyToIndex.TryGetValue(PointKey(b), out j)) return false;
            return i >= 0 && j >= 0 && i < _meters.GetLength(0) && j < _meters.GetLength(1);
        }

        private static int IndexPoint(
            List<GeoPoint> points, Dictionary<long, int> keyToIndex, GeoPoint p)
        {
            long key = PointKey(p);
            if (keyToIndex.TryGetValue(key, out int existing))
                return existing;
            int idx = points.Count;
            points.Add(p);
            keyToIndex[key] = idx;
            return idx;
        }

        private static long PointKey(GeoPoint p) =>
            ((long)Math.Round(p.Lat * 100000.0) << 32)
            ^ (uint)Math.Round(p.Lng * 100000.0);
    }
}
