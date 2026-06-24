using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Find the office AI panel on the local LAN — async only, never blocks the UI thread.</summary>
    internal static class HiatmePanelLanDiscovery
    {
        private const int MaxHostsPerScan = 32;
        private const int MaxParallel = 8;
        private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(400);

        private static readonly object CacheLock = new object();
        private static DateTime _discoveredUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
        private static string _cachedUrl = "";

        private static int _scanRunning;

        /// <summary>Previously discovered URL only — does not scan.</summary>
        public static IReadOnlyList<string> GetCachedUrls()
        {
            lock (CacheLock)
            {
                if (!string.IsNullOrEmpty(_cachedUrl) && DateTime.UtcNow - _discoveredUtc < CacheTtl)
                    return new[] { _cachedUrl };
            }
            return Array.Empty<string>();
        }

        public static void InvalidateCache()
        {
            lock (CacheLock)
            {
                _cachedUrl = "";
                _discoveredUtc = DateTime.MinValue;
            }
        }

        /// <summary>Scan local /24 in the background (at most one scan at a time).</summary>
        public static void DiscoverInBackground()
        {
            if (Interlocked.CompareExchange(ref _scanRunning, 1, 0) != 0)
                return;
            _ = Task.Run(async () =>
            {
                try
                {
                    string found = await DiscoverPanelUrlAsync(CancellationToken.None).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(found)) return;
                    lock (CacheLock)
                    {
                        _cachedUrl = found;
                        _discoveredUtc = DateTime.UtcNow;
                    }
                    HiatmeAiSettings.InvalidateSessionCacheOnly();
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _scanRunning, 0);
                }
            });
        }

        private static async Task<string> DiscoverPanelUrlAsync(CancellationToken outerToken)
        {
            var hosts = CollectProbeHosts();
            if (hosts.Count == 0) return null;

            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(outerToken))
            {
                budget.CancelAfter(ScanBudget);
                var token = budget.Token;
                var hits = new ConcurrentBag<string>();
                using (var gate = new SemaphoreSlim(MaxParallel, MaxParallel))
                {
                    var tasks = new List<Task>();
                    foreach (string ip in hosts)
                    {
                        token.ThrowIfCancellationRequested();
                        if (hits.Count > 0) break;
                        await gate.WaitAsync(token).ConfigureAwait(false);
                        tasks.Add(ProbeHostAsync(ip, hits, gate, token));
                    }
                    try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!outerToken.IsCancellationRequested) { }
                    catch (TaskCanceledException) { }
                }
                return hits.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            }
        }

        private static async Task ProbeHostAsync(
            string ip,
            ConcurrentBag<string> hits,
            SemaphoreSlim gate,
            CancellationToken token)
        {
            try
            {
                if (hits.Count > 0) return;
                string url = "http://" + ip + ":" + HiatmeAiSettings.DefaultPort + "/api/hiatme/geo/status";
                using (var http = new HttpClient { Timeout = RequestTimeout })
                {
                    try
                    {
                        using (var resp = await http.GetAsync(url, token).ConfigureAwait(false))
                        {
                            if (resp.IsSuccessStatusCode)
                                hits.Add("http://" + ip + ":" + HiatmeAiSettings.DefaultPort);
                        }
                    }
                    catch (TaskCanceledException) { }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (HttpRequestException) { }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Local IP ± a few neighbors and .1 — not the whole subnet.</summary>
        private static List<string> CollectProbeHosts()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address;
                        if (IPAddress.IsLoopback(ip)) continue;

                        uint ipVal = ToUInt32(ip);
                        uint lastOctet = ipVal & 0xFFu;
                        uint prefix = ipVal & 0xFFFFFF00u;

                        set.Add(ip.ToString());
                        set.Add(FromUInt32(prefix | 1));
                        for (uint n = 1; n <= MaxHostsPerScan && set.Count < MaxHostsPerScan; n++)
                        {
                            uint o = lastOctet + n;
                            if (o == 0 || o > 254) break;
                            set.Add(FromUInt32(prefix | o));
                        }
                        for (uint n = 1; n <= 8 && set.Count < MaxHostsPerScan; n++)
                        {
                            if (lastOctet <= n) break;
                            set.Add(FromUInt32(prefix | (lastOctet - n)));
                        }
                    }
                }
            }
            catch { }

            return set.Take(MaxHostsPerScan).ToList();
        }

        private static uint ToUInt32(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return BitConverter.ToUInt32(b, 0);
        }

        private static string FromUInt32(uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return new IPAddress(b).ToString();
        }
    }
}
