# Publishing ScreenBreak to the Microsoft Store

Checked against **Microsoft Store Policies version 7.20** (September 2026):
<https://learn.microsoft.com/windows/apps/publish/store-policies>

## Where ScreenBreak stands

| Policy | Requirement | Status |
|---|---|---|
| 10.1 | Does what the listing says | Ready. Write the listing from the README "What it does" section |
| 10.2.2 | No downloaded or dynamic code | Ready. The app has no network access at all |
| 10.2.3 | No malware, no bundled software | Ready |
| 10.2.7 | Clear, clean uninstall | Ready. Listed in Installed apps, and removes files, shortcuts and registry entries |
| 10.2.8 | Consent before changing the Windows experience | Ready. Start-with-Windows is opt-in: a checkbox in setup, or a question on first run |
| 10.2.9 | EXE installer: **every exe code-signed** with a CA in the Microsoft Trusted Root Program | **To do.** Needs a code-signing certificate (see below) |
| 10.2.9 | Versioned HTTPS download URL that never changes | **To do.** For example, a GitHub Release asset per version |
| 10.2.9 | Silent install | Ready. The install command is `ScreenBreak-Setup.exe /quiet` |
| 10.2.9 | Standalone installer, not a web downloader | Ready |
| 10.4.2 | Starts fast, handles errors without crashing | Ready. Errors go to `error.log` and the app keeps running |
| 10.5.1 | Privacy policy (required for every Win32 app) | **To do.** Host [PRIVACY.md](../PRIVACY.md) at a public URL and fill in the contact line |
| 11.11 | Age rating | **To do.** Fill in the rating questionnaire in Partner Center (expect a 3+ / Everyone rating) |

## Steps

1. **Get a code-signing certificate.** It must come from a CA in the Microsoft Trusted
   Root Program. Options:
   - a standard (OV) code-signing certificate from a commercial CA, or
   - Microsoft's Azure Artifact Signing service (formerly Trusted Signing). It uses its own
     signing tool instead of `-CertThumbprint`.
2. **Build signed binaries:**
   ```powershell
   .\build.ps1 -CertThumbprint <your certificate thumbprint>
   ```
   This signs `ScreenBreak.exe` *before* it is embedded in the installer, then signs the
   installer, so every exe that ends up on the user's PC is signed. The uninstaller is a
   copy of the signed installer. It needs `signtool.exe` from the Windows SDK.
3. **Publish the installer at a versioned URL**, for example
   `https://github.com/<you>/screenbreak/releases/download/v1.0.0/ScreenBreak-Setup.exe`.
   Never replace the file behind a URL. Each new version gets a new URL.
4. **Host the privacy policy** and note its URL.
5. **In Partner Center** create the app as an *EXE or MSI app* and enter:
   - the installer URL, architecture x64 and x86 (it's AnyCPU), and the silent switch `/quiet`
   - the privacy policy URL
   - the description. Say plainly that ScreenBreak shows always-on-top overlays that you
     can click through, and that it can optionally start with Windows.
   - screenshots (use `ScreenBreak.exe --preview`), the age rating questionnaire, and
     category *Health & fitness* or *Productivity*
   - Notes for certification: "Run the app, then right-click the tray icon > Preview popup
     to see the break overlay without waiting."

## Alternative: MSIX package

Submitting as MSIX means the Store signs the app for you, so no certificate is needed.
It takes more work:

- It needs the Windows SDK (`MakeAppx`) and a package manifest.
- Start-with-Windows must move from the `Run` registry key to an MSIX `StartupTask`, because
  packaged apps can't register startup through the registry.
- The custom installer and uninstaller aren't used. The Store installs and removes the package.

## Security hardening already in place

- Installs per user with no admin rights. It never asks to elevate, and the manifest
  stops Windows from auto-elevating "Setup".
- DLLs are loaded only from System32, never from the exe's folder (protects against DLL
  planting in Downloads).
- The uninstaller starts `cmd.exe` by its full System32 path, so a planted copy can't be used.
- Setup stops only its own installed `ScreenBreak.exe`, never an unrelated process with the same name.
- Recursive deletes never follow folder links (junctions), so they can't be tricked into
  deleting outside ScreenBreak's own folders.
- The background image must be ScreenBreak's own copy in its data folder. Network paths
  are refused, so a tampered `settings.ini` can't leak your Windows login to a remote server.
  Images are capped at 20 MB and scaled to 4K at most.
- Settings and log files that are unexpectedly large are ignored instead of read into memory.
- `build.ps1` writes `bin\SHA256SUMS.txt` so downloads can be checked.

## Not a security boundary

ScreenBreak is a nudge, not parental control. Anyone using the PC can exit it from the
tray, end it in Task Manager, edit `settings.ini`, or uninstall it. Making it tamper-proof
would need a different design: settings owned by an administrator account and a
protected background service.
