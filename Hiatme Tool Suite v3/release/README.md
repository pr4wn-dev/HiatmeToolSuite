# Hiatme Tool Suite v3 — Release / Updater

This folder is the source of truth for the **in-app updater**.

The desktop app polls a small JSON endpoint, downloads a SHA256-verified zip, then
launches `Update.exe` to extract over the install dir and restart. User templates and
saved login info are never touched.

```
   Hiatme Tool Suite v3.exe                    hiatme.com
   ┌─────────────────────────────┐             ┌──────────────────────────────────────┐
   │ on launch:                  │   GET       │ /downloads/hiatme-tool-suite/        │
   │   UpdateClient.Fetch...     │ ──────────► │   latest.php                         │
   │                             │ ◄────────── │     → {version, downloadUrl, sha256} │
   │ if remote > local:          │   JSON      │   HiatmeToolSuite-1.0.1.0.zip        │
   │   show UpdatePrompt         │             │   HiatmeToolSuite-1.0.1.0.md         │
   │   download zip + verify     │ ──────────► │                                      │
   │   launch Update.exe, exit   │             └──────────────────────────────────────┘
   └─────────────────────────────┘
            │
            ▼
   Update.exe (copied to %TEMP% first so it can replace itself)
     → waits for main pid to exit
     → extracts zip OVER install dir (does NOT delete user-created folders)
     → restarts main exe
```

---

## One-time setup on the website

Drop this folder's `latest.php` into:

```
hiatme.com/downloads/hiatme-tool-suite/latest.php
```

The endpoint:

* Scans its own directory for `HiatmeToolSuite-MAJOR.MINOR.BUILD.REV.zip` files.
* Picks the highest version.
* Computes the SHA256 of the picked zip on every request (no caching — swap a zip and it's instantly live).
* Returns release notes from a sibling `HiatmeToolSuite-<version>.md` if one exists.

No auth required. The desktop client refuses to install a zip whose SHA256 doesn't
match what `latest.php` returned, so a man-in-the-middle can't ship arbitrary code.

---

## Cutting a new release

1. **Bump the version**. Edit
   `Hiatme Tool Suite v3\Properties\AssemblyInfo.cs`:

   ```csharp
   [assembly: AssemblyVersion("1.0.1.0")]
   [assembly: AssemblyFileVersion("1.0.1.0")]
   ```

   The app reads this at runtime and shows it in the title bar (e.g.
   `Hiatme Tool Suite v3 Blackout — v1.0.1.0`). The updater only fires when the
   manifest's `version` is **strictly greater** than the running build's
   `AssemblyVersion`.

2. **Package**. From a regular PowerShell prompt:

   ```powershell
   cd "Hiatme Tool Suite v3\release"
   .\package.ps1 -ReleaseNotes "What's new in 1.0.1.0:`n- Fixed X`n- Improved Y"
   ```

   This will:
   * Locate MSBuild via `vswhere`.
   * Build the solution in `Release`.
   * Stage `bin\Release` of both `Hiatme Tool Suite v3` and `Update`.
   * Produce `HiatmeToolSuite-1.0.1.0.zip` in this folder.
   * Print the SHA256 for sanity checking.
   * Write `HiatmeToolSuite-1.0.1.0.md` if you passed `-ReleaseNotes`.

3. **Upload** the produced files to `hiatme.com/downloads/hiatme-tool-suite/`:
   * `HiatmeToolSuite-1.0.1.0.zip` (required)
   * `HiatmeToolSuite-1.0.1.0.md` (optional, surfaces inside the in-app prompt)

   No PHP touching needed for ongoing releases — `latest.php` re-scans the folder
   on every request.

4. **Verify** by clicking *Check for updates* in the bottom-right of the running
   app. You should see `Update available v1.0.1.0 · v1.0.0.0`, the dialog should
   open, the download should hit ~100% with a green-bar verify, then `Update.exe`
   should flash and the new build should launch with the bumped version in the
   title bar.

---

## What's preserved across updates

The updater **only writes files that are in the zip**. Anything else in the
install directory is left in place. That means:

| Data                                | Where it lives                                            | Touched? |
|-------------------------------------|-----------------------------------------------------------|---------:|
| Templates (Monday…Sunday folders)   | `<installDir>\Monday\…`, `<installDir>\Tuesday\…`, etc.   |       No |
| Template Temps working folder       | `<installDir>\Template Temps\`                            |       No |
| User-added fonts                    | `<installDir>\Fonts\`                                     |  Overwritten only if the new zip ships the same filename |
| Saved Modivcare / Hiatme creds      | `%LOCALAPPDATA%\Hiatme_Tool_Suite_v3_…\user.config`       |       No |
| Anything you side-loaded            | Anywhere in `<installDir>` not in the zip                 |       No |

If you ever need to **delete** a stale file in a future release, the cleanest
options are:

* Rename the file in the new release (the old one stays harmlessly on disk).
* Or have a future build of the main app delete it on first run after detecting
  its own new version (small one-off migration step).

The updater intentionally does not delete arbitrary files, because doing so
would be the single easiest way to nuke a user's templates by mistake.

---

## Rolling back

If a release is bad:

1. Delete the bad `HiatmeToolSuite-X.Y.Z.W.zip` from the downloads folder.
2. The next-highest version still in the folder becomes the manifest's "latest"
   automatically.
3. Users on the bad version stay on it until you ship a newer zip with a
   **higher** version than the bad one (you cannot push a downgrade — the client
   only installs strictly newer builds).

So: bump to e.g. `1.0.1.1` containing the rollback, build, upload. Done.

---

## Files in this folder

* `latest.php` — drop-in update manifest endpoint for the website.
* `package.ps1` — local packaging script that produces the upload zip.
* `README.md`   — you are here.
