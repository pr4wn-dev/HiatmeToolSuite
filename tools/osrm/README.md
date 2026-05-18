# Local OSRM (Maine)

Hiatme Tool Suite uses this for snap-to-street routing during Supey schedule builds.

## Prerequisites

- Docker Desktop (working `docker` CLI)
- ~5 GB free disk, ~4 GB RAM while building

## One-time setup

From PowerShell:

```powershell
cd tools\osrm\scripts
.\01-download-maine.ps1
.\02-build-graph.ps1    # 20–60 minutes
.\start-osrm.ps1
.\test-osrm.ps1         # should print OK
```

## Daily use

```powershell
.\start-osrm.ps1   # before opening Hiatme / building schedules
.\stop-osrm.ps1    # optional
```

The app expects `http://127.0.0.1:5000/route/v1/driving/` (see `App.config` `OsrmBaseUrl`).

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `test-osrm` connection refused | Run `start-osrm.ps1`; check `docker ps` shows `hiatme-osrm-maine` |
| Container exits immediately | Run `docker compose logs` — graph missing → run `02-build-graph.ps1` |
| `NoRoute` on some trips | Coordinates outside Maine extract; switch to New England extract later |
| Port 5000 in use | Change host port in `docker-compose.yml` and `OsrmBaseUrl` in App.config |

## Update map data

Re-run `01-download-maine.ps1` (delete old pbf first), then `02-build-graph.ps1`, then restart.
