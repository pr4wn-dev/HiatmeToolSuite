using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Orchestrates a "pull driver roster from WellRyde" job: paginates the user list, filters
    /// to enabled+unlocked accounts, and fans out per-user detail page fetches with bounded
    /// concurrency so we don't slam the portal. Reports incremental progress so the picker UI
    /// can show a live "fetched 7 / 11 drivers" counter, and respects cancellation so the user
    /// can bail mid-pull.
    /// </summary>
    /// <remarks>
    /// Concurrency capped at <see cref="DefaultDetailConcurrency"/> (4) — empirically the portal
    /// tolerates 4–6 simultaneous AJAX calls without throttling, and 4 keeps headroom for any
    /// Trip Scout work the user might have queued. Detail-page failures are tolerated: the
    /// summary still ends up in the result with a flag, so the picker can offer "include anyway"
    /// or skip-and-warn.
    /// </remarks>
    internal sealed class WellRydeRosterImporter
    {
        public const int DefaultDetailConcurrency = 4;

        private readonly WellRydePortalSession _session;
        private readonly int _detailConcurrency;

        public WellRydeRosterImporter(WellRydePortalSession session, int detailConcurrency = DefaultDetailConcurrency)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _detailConcurrency = detailConcurrency < 1 ? 1 : detailConcurrency;
        }

        /// <summary>
        /// Fetches the entire users list (paginating through if necessary), filters to
        /// <see cref="WellRydeUserSummary.IsEligibleForSchedule"/> = true, then concurrently loads
        /// the per-user detail page for each. <paramref name="progress"/> is invoked on the
        /// calling synchronization context (or thread pool if no UI context) once per detail
        /// completion. <paramref name="cancellationToken"/> aborts pending detail fetches but
        /// already-running ones run to completion.
        /// </summary>
        public async Task<WellRydeRosterImportResult> ImportEligibleDriversAsync(
            IProgress<WellRydeRosterImportProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new WellRydeRosterImportResult();

            // ---- Phase 1: paginate the list ----
            ReportProgress(progress, 0, 0, "Loading user list...");

            var summaries = new List<WellRydeUserSummary>();
            int page = 1;
            int totalRecords = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var listResult = await _session.PostUsersFilterDataAsync(
                    page: page,
                    maxResults: WellRydePortalSession.DefaultUsersFilterMaxResult,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!listResult.IsSuccess)
                {
                    result.ListErrorMessage = listResult.ErrorMessage ?? "Failed to load users list.";
                    return result;
                }

                int pageTotal;
                var pageSummaries = WellRydeUserParser.ParseUsersList(listResult.JsonBody, out pageTotal);
                if (page == 1) totalRecords = pageTotal;

                summaries.AddRange(pageSummaries);

                // Stop when we've collected everything the portal said exists, or the page came
                // back empty (defensive — guards against runaway loops if totalRecords lies).
                if (pageSummaries.Count == 0 || summaries.Count >= totalRecords)
                    break;
                page++;
                // Hard ceiling so a misbehaving server can't trigger infinite loops.
                if (page > 50) break;
            }

            // Filter to eligible (enabled + not locked).
            var eligible = summaries.Where(s => s.IsEligibleForSchedule).ToList();
            result.EligibleSummaries = eligible;
            result.TotalUsersScanned = summaries.Count;

            ReportProgress(progress, 0, eligible.Count,
                "Found " + eligible.Count + " eligible driver" + (eligible.Count == 1 ? "" : "s") +
                " of " + summaries.Count + " total. Loading details...");

            // ---- Phase 2: fan out detail fetches ----
            // Use a SemaphoreSlim to bound concurrency. Each task awaits the semaphore, fetches
            // the detail, parses it, increments the progress counter under a lock, and releases.
            int completed = 0;
            object counterLock = new object();
            var details = new WellRydeUserDetail[eligible.Count];

            using (var gate = new SemaphoreSlim(_detailConcurrency))
            {
                var tasks = new List<Task>(eligible.Count);
                for (int i = 0; i < eligible.Count; i++)
                {
                    int idx = i;
                    var summary = eligible[i];
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var detailResult = await _session.GetUserDetailHtmlAsync(summary.SecId, cancellationToken)
                                .ConfigureAwait(false);
                            WellRydeUserDetail detail;
                            if (detailResult.IsSuccess && !string.IsNullOrEmpty(detailResult.HtmlBody))
                            {
                                detail = WellRydeUserParser.ParseUserDetail(summary.SecId, detailResult.HtmlBody);
                                // Backfill summary-level fields the detail page sometimes leaves blank.
                                if (string.IsNullOrEmpty(detail.Username)) detail.Username = summary.Username;
                                if (string.IsNullOrEmpty(detail.FullName)) detail.FullName = summary.FullName;
                                if (string.IsNullOrEmpty(detail.Email)) detail.Email = summary.Email;
                            }
                            else
                            {
                                // Stub: at least keep the summary identity so the picker can show
                                // "couldn't load — included as a stub". Address fields stay blank.
                                detail = new WellRydeUserDetail
                                {
                                    SecId = summary.SecId,
                                    Username = summary.Username,
                                    FullName = summary.FullName,
                                    Email = summary.Email,
                                    AccountEnabled = summary.Enabled,
                                    AccountLocked = summary.Locked,
                                };
                            }
                            details[idx] = detail;
                        }
                        finally
                        {
                            int now;
                            lock (counterLock) { now = ++completed; }
                            ReportProgress(progress, now, eligible.Count,
                                "Loading driver details (" + now + " / " + eligible.Count + ")...");
                            gate.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            result.Details = details.Where(d => d != null).ToList();
            ReportProgress(progress, eligible.Count, eligible.Count, "Done.");
            return result;
        }

        private static void ReportProgress(IProgress<WellRydeRosterImportProgress> progress,
            int completed, int total, string message)
        {
            progress?.Report(new WellRydeRosterImportProgress(completed, total, message));
        }
    }

    /// <summary>Snapshot of the import job's progress for UI consumption.</summary>
    internal sealed class WellRydeRosterImportProgress
    {
        public WellRydeRosterImportProgress(int completed, int total, string message)
        {
            Completed = completed;
            Total = total;
            Message = message ?? "";
        }
        public int Completed { get; }
        public int Total { get; }
        public string Message { get; }
    }

    /// <summary>Final result of an import. Non-null even on partial failure.</summary>
    internal sealed class WellRydeRosterImportResult
    {
        public IList<WellRydeUserSummary> EligibleSummaries { get; set; } = new List<WellRydeUserSummary>();
        public IList<WellRydeUserDetail> Details { get; set; } = new List<WellRydeUserDetail>();
        public int TotalUsersScanned { get; set; }
        public string ListErrorMessage { get; set; }
        public bool ListLoadFailed => !string.IsNullOrEmpty(ListErrorMessage);
    }
}
