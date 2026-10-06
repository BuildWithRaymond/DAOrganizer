# DAOrganizer for Windows

Start **DAOrganizer** from the Start menu after installation. In the portable ZIP, run the root `DAOrganizer.exe`; keep all companion folders together. The app includes .NET and WorldLogs routes.

Open **Settings**, select the supported `Darkages.exe` if needed, add a character, and choose **Launch client**. The local [getting started guide](docs/GETTING_STARTED.md) explains sorting, bank scans, consolidation and item maintenance.

Optional automatic updates are off by default. Enable **Automatically download and install updates on safe exit**, or use manual check/download/install controls. Finish current actions and close organizer-launched game clients before installation. Downloads are unsigned.

To explore fictional accounts without opening your saved profile:

```powershell
.\DAOrganizer.exe --demo
```

Collection data stays in `%LOCALAPPDATA%\DAOrganizer` and optional passwords in Windows Credential Manager. Updates retain these locations. Ground drops can be lost; review item actions carefully. Included routes describe observations and do not guarantee access through every door or live obstacle.

[Project and screenshots](https://github.com/BuildWithRaymond/DAOrganizer) · [Releases](https://github.com/BuildWithRaymond/DAOrganizer/releases/latest) · [Report an issue](https://github.com/BuildWithRaymond/DAOrganizer/issues/new/choose)

Original code: MIT. See `LICENSE`, `THIRD_PARTY_NOTICES.md` and `licenses/` for attribution.
