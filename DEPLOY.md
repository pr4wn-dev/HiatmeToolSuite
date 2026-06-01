# Deploy Hiatme Tool Suite + AIagent

End-to-end checklist for a **new office** or **new PC**. Two GitHub repos; large artifacts are built locally, not stored in git.

## Architecture

```
  [Dispatch desk]  Hiatme Tool Suite v3.exe
        │  HTTPS/HTTP  Bearer HIATME_API_TOKEN
        ▼
  [Office server]  AIagent panel :8787  +  Ollama  +  Docker OSRM :5000
```

## A. Office AI server (once per site)

```powershell
git clone https://github.com/pr4wn-dev/AIagent.git
cd AIagent
powershell -ExecutionPolicy Bypass -File scripts\bootstrap-new-server.ps1 -Role server
```

Record:

- **Panel URL** — e.g. `http://192.168.1.23:8787/`
- **HIATME_API_TOKEN** — printed by bootstrap (also in server `.env`)

Details: [AIagent DEPLOY-NEW-SERVER](https://github.com/pr4wn-dev/AIagent/blob/main/docs/DEPLOY-NEW-SERVER.md)

### Migrate from old server (optional, faster BUILD)

Copy to the new `PROJECTS_ROOT` (often `F:\Projects\data\hiatme\`):

- `geocode-cache.json`
- `osrm-route-cache.json`
- `archive/` folder

Rebuild **OSRM graph** on the new machine (`tools\osrm\scripts\install-osrm-maine.ps1`).

### Not in git (server)

| Item | How to get it |
|------|----------------|
| `.env` | `bootstrap-new-server.ps1` or copy `.env.example` |
| `.venv` | `pip install -e ".[dev]"` |
| OSRM Maine graph | `install-osrm-maine.ps1` (~5 GB) |
| Ollama weights | `ollama pull phi4-mini` |
| `data/hiatme/*` | Runtime; copy from old host if migrating |

## B. Each dispatch desk

```powershell
git clone https://github.com/pr4wn-dev/HiatmeToolSuite.git
cd HiatmeToolSuite
powershell -ExecutionPolicy Bypass -File scripts\setup-desk.ps1 -OfficePanelUrl "http://192.168.1.23:8787"
```

`setup-desk.ps1` will:

1. Build the Tool Suite (Debug) if MSBuild is available  
2. Write `hiatme_ai.defaults.json` with panel URL + token (reads sibling `AIagent\.env` or common paths)  
3. Print reminders for templates and Modivcare login  

### Weekday templates (required for BUILD / LOAD)

Place folders next to the **running exe** (install dir), same names as weekdays:

```
<installDir>\Monday\*.csv
<installDir>\Tuesday\*.csv
...
```

Copy from the previous install, backup, or generate with `scripts\build_weekday_templates_from_xlsx.py`.

Also preserved across in-app updates: `Template Temps\`, user settings under `%LOCALAPPDATA%`.

### Not in git (desk)

| Item | How to get it |
|------|----------------|
| `hiatme_ai.defaults.json` | `setup-desk.ps1` |
| `bin\Debug` or Release build | MSBuild / `setup-desk.ps1` |
| `packages/` (NuGet) | Restored on first build |
| Modivcare credentials | Saved in app after first login |

## C. Verify

**Server:**

```powershell
cd AIagent
.\scripts\hiatme_panel_check.ps1
```

**Desk:** open Tool Suite → Schedule Builder → **BUILD** (needs Modivcare session) or **LOAD** (saved `.xlsx` / CSV folder, no Modivcare).

Supey tab should show server geo status when `UseServerGeo` is true.

## D. Daily ops

| Machine | Command |
|---------|---------|
| Server after reboot | `AIagent\scripts\ensure-server-services.ps1` |
| Panel only restart | `AIagent\scripts\restart-panel.ps1` |
| Pull updates | `git pull` in both repos; rebuild desk exe if C# changed |

## E. Optional components

| Component | Location |
|-----------|----------|
| In-app auto-update | `Hiatme Tool Suite v3\release\` → website zip |
| Node client media server | `Server\` — `npm install` && `npm start` |
| Local OSRM on a desk | Not recommended; use server OSRM |

## Troubleshooting

| Issue | Check |
|-------|--------|
| BUILD “server not ready” | Server Docker + panel; `/api/hiatme/ready` |
| 401 on desk | `HIATME_API_TOKEN` matches server `.env`; re-run `setup-desk.ps1` |
| LOAD groups wrong | Service date / weekday templates; see Schedule Builder status line |
| No Excel for LOAD | Use `.xlsx` (built-in reader) or CSV folder export |
