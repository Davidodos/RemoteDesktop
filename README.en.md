# RemoteDesktop

Control your Windows PC from your Android phone. Screen, mouse, keyboard, media
keys, power — plus your own actions on a single tap, without even opening the
app.

No router port forwarding, no third-party account, no server in the middle that
can watch. The connection goes straight from your phone to your PC.

> **Note on language:** the user interface and the documentation are **German**.
> This file exists so you can tell whether the project is for you before
> installing anything. The German readme is [`README.md`](README.md).

## What it does

- Live screen, touchpad and on-screen keyboard, multi-monitor switching
- Media control (play/pause, volume, now playing) — works on a locked screen
- Sleep, shutdown, restart, and Wake-on-LAN when another device on the same LAN
  is awake
- **Actions**: programs, PowerShell scripts, key chords, URLs or sequences of
  those, declared on the PC and invoked by id — the phone never sends a command
  line
- Home-screen widget, quick-settings tile and app shortcuts for those actions

## How it is built

| Folder | Contents |
|---|---|
| `agent/` | C# / .NET 8 Windows service on the controlled machine, port 8443 |
| `desktop/` | C# WinForms + WebView2 tray app and setup window |
| `setup/` | C# library with the setup logic, shared by installer and window |
| `app/` | React + Vite UI, shared by the PWA, the APK and the Windows window |
| `clients/android/` | Capacitor + Kotlin APK |
| `waker/` | Node service for a NAS or Raspberry Pi that sends the magic packet |
| `installer/` | Inno Setup script with selectable components |

Networking is Tailscale only — MagicDNS names, no LAN addresses in the code, no
"at home vs. away" branching, nothing reachable from the internet.

## Security in one paragraph

Pairing generates an ECDSA P-256 key pair on the phone; the agent stores only
the public half and issues short-lived session tokens against a signed
challenge. There is no shared secret that unlocks every machine, revoking a
device takes effect mid-session, and the action catalogue can only be edited
locally on the machine itself — there is no write path over the network. The
full review, including what was deliberately left as is, lives in
[`docs/SICHERHEIT.md`](docs/SICHERHEIT.md) (German).

## Building

Everything except actually running the Windows parts builds on Linux too.

```bash
cd app         && npm install && npm test && npm run build
cd agent       && dotnet build
cd desktop     && dotnet build
cd setup.Tests && dotnet test
cd clients/android && npm run apk
```

## License

[Apache-2.0](LICENSE). No warranty — running this software hands another device
control over your computer, and that is your decision to make.

Tailscale is a separate product, not part of this project. It is downloaded from
its vendor during setup and covered by their terms.
