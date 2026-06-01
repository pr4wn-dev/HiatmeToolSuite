# Deployment — Hiatme Tool Suite + AIagent

End-to-end checklist for a **new office**, **new AI server**, or **new dispatch desk**. Two GitHub repos; large runtime artifacts are built on each host, not stored in git.

## Architecture

```
  [Dispatch desk]  Hiatme Tool Suite v3.exe
        │  HTTP + Bearer HIATME_API_TOKEN
        ▼
  [Office server]  AIagent panel :8787  +  Ollama  +  Docker OSRM :5000
```

| Repo | Clone |
|------|--------|
| [AIagent](https://github.com/pr4wn-dev/AIagent) | Office server only |
| [HiatmeToolSuite](https://github.com/pr4wn-dev/HiatmeToolSuite) | Each dispatch desk |

---

## A. Office AI server (once per site)

```powershell
git clone https://github.com/pr4wn-dev/AIagent.git
cd AIagent
powershell -ExecutionPolicy Bypass -File scripts\bootstrap-new-server.ps1 -Role server
```

Record after bootstrap:

- **Panel URL** — e.g. `http://192.168.1.23:8787/` (LAN IP printed by the script)
- **HIATME_API_TOKEN** — in server `.env` (generated if missing)

Server-only detail: [AIagent docs/DEPLOY-NEW-SERVER.md](https://github.com/pr4wn-dev/AIagent/blob/main/docs/DEPLOY-NEW-SERVER.md)

### Prerequisites

| Software | Purpose |
|----------|---------|
| Git | Clone / pull |
| Python 3.11+ (3.12 recommended) | Panel `.venv` |
| Docker Desktop | OSRM container |
| Ollama | Local LLM |
| NVIDIA driver (optional) | GPU inference |

### One-time OSRM (Maine driving graph)

```powershell
cd AIagent\tools\osrm\scripts
.\install-osrm-maine.ps1
```

~5 GB disk, ~30–60 min first run. Not in GitHub.

### Migrate from an old server (optional)

Copy into the new `PROJECTS_ROOT` (often `F:\Projects\data\hiatme\`):

- `geocode-cache.json`
- `osrm-route-cache.json`
- `archive/`

Rebuild the OSRM graph on the new machine; caches are independent of `.osrm` files.

### Not in git (server)

| Item | How to get it |
|------|----------------|
| `.env` | `bootstrap-new-server.ps1` or `.env.example` |
| `.venv` | `pip install -e ".[dev]"` |
| OSRM Maine graph | `install-osrm-maine.ps1` |
| Ollama weights | `ollama pull phi4-mini` |
| `data/hiatme/*` | Runtime; copy when migrating |

### Daily (after reboot)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\ensure-server-services.ps1
```

Panel-only restart: `scripts\restart-panel.ps1`

Verify:

```powershell
.\scripts\hiatme_panel_check.ps1
```

---

## B. Each dispatch desk

```powershell
git clone https://github.com/pr4wn-dev/HiatmeToolSuite.git
cd HiatmeToolSuite
powershell -ExecutionPolicy Bypass -File scripts\setup-desk.ps1 -OfficePanelUrl "http://192.168.1.23:8787"
```

`setup-desk.ps1`:

1. Builds Tool Suite (Debug) when MSBuild is available  
2. Writes `hiatme_ai.defaults.json` (URL + token from `AIagent\.env` or common paths)  
3. Reminds you about weekday templates and Modivcare login  

### Weekday templates (BUILD and LOAD)

CSV folders next to the **running exe** / install dir:

```
<installDir>\Monday\*.csv
<installDir>\Tuesday\*.csv
...
```

Copy from a previous install or generate with `scripts\build_weekday_templates_from_xlsx.py`.

Preserved across in-app updates: `Template Temps\`, `%LOCALAPPDATA%` user settings.

### Schedule Builder on the desk

| Action | Needs |
|--------|--------|
| **BUILD** | Modivcare login + server ready + templates |
| **LOAD** | Saved `.xlsx` or driver CSV folder; Excel not required on desk |
| **SAVE** | Prior BUILD or LOAD |

LOAD infers service date from the file name or trip dates and sets the date picker. Groups come from route breaks in the file, weekday templates, or pickup time gaps.

### Not in git (desk)

| Item | How to get it |
|------|----------------|
| `hiatme_ai.defaults.json` | `setup-desk.ps1` |
| `bin\Debug` / Release | MSBuild / `setup-desk.ps1` |
| `packages/` | NuGet restore on build |
| Modivcare credentials | Saved in app after first login |

---

## C. Verify end-to-end

**Server:** `hiatme_panel_check.ps1` and `http://<server>:8787/api/hiatme/ready`

**Desk:** Tool Suite → Schedule Builder → BUILD or LOAD; Supey tab shows server geo when `UseServerGeo` is true.

---

## D. Pull updates

```powershell
git pull   # in AIagent and HiatmeToolSuite
```

Rebuild the desk `.exe` when C# changed. Restart the panel when Python/API changed (`restart-panel.ps1`).

---

## E. Optional components

| Component | Location |
|-----------|----------|
| In-app auto-update | `Hiatme Tool Suite v3\release\` |
| Node media server | `Server\` — see `Server/README.md` |
| OSRM on a desk | Not recommended; use server OSRM |

---

## Troubleshooting

| Issue | Check |
|-------|--------|
| BUILD “server not ready” | Docker + panel; `/api/hiatme/ready` |
| 401 on desk | Token matches server `.env`; re-run `setup-desk.ps1` |
| BUILD timeout | Server `data/hiatme/last-build-log.txt` |
| LOAD wrong groups | Date picker weekday vs template folders |
| PU/DO times as decimals | Reload after pull (xlsx time fix); or re-LOAD |
| No Excel for LOAD | Use `.xlsx` (built-in reader) or CSV export folder |

---

## Related docs

- [../README.md](../README.md) — repo overview  
- [AIagent ENVIRONMENT.md](https://github.com/pr4wn-dev/AIagent/blob/main/ENVIRONMENT.md) — `.env` reference  
- [AIagent office-server-setup.md](https://github.com/pr4wn-dev/AIagent/blob/main/docs/office-server-setup.md) — short server reference  
