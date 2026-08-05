# RemoteDesktop

Steuere deinen Windows-PC vom Handy. Bild, Maus, Tastatur, Lautstärke,
Herunterfahren — und selbst geschriebene Aktionen auf Knopfdruck, ohne die App
überhaupt zu öffnen.

Kein Port am Router, kein Konto bei einem fremden Anbieter, kein Rechner in der
Mitte, der mitliest. Die Verbindung geht direkt von deinem Handy zu deinem PC.

> **Sprache:** Programm und Doku sind deutsch. English readme:
> [`README.en.md`](README.en.md).

---

## Was du brauchst

- Einen **Windows-10-** oder **Windows-11-Rechner**, den du steuern willst
- Ein **Android-Handy** (ab Android 8)
- Einen Weg, auf dem das Handy den Rechner erreicht. Du hast die Wahl:

| | Wann | Von unterwegs |
|---|---|---|
| **Heimnetz** | Handy und Rechner hängen am selben Router. Kein VPN, kein Konto, nichts einzurichten | nein |
| **Tailscale** | Du willst auch aus dem Mobilfunknetz ran. Kostenlos, Anmeldung mit einem Konto, das du schon hast | ja |
| **Eigenes VPN** | Du betreibst schon WireGuard, OpenVPN, ZeroTier … — dann behältst du es | ja |

Das Fenster fragt dich beim Einrichten danach und führt dich durch den Rest.
Ausführlich steht das in [`docs/NETZ.md`](docs/NETZ.md).

Wichtig in allen drei Fällen: **von außen ist nichts erreichbar**. Es gibt keine
offene Tür ins Internet, die jemand ausprobieren könnte.

---

## Loslegen

### 1. Auf dem Rechner installieren

Lade `RemoteDesktop-Setup.exe` aus den
[Releases](https://github.com/Davidodos/RemoteDesktop/releases) und starte sie.

Es wird immer alles abgelegt — die Oberfläche, der Agent und die Weboberfläche.
Der Installer fragt nur, was davon auch *laufen* soll:

| | wofür |
|---|---|
| **Diesen Rechner fernsteuerbar machen** | Trägt den Agent als Dienst ein. Auf einem Arbeitslaptop, der nur steuern und nie gesteuert werden soll, lässt du das Häkchen weg — dann läuft dort kein Dienst, der Zugriff erlaubt. |
| **Autostart** | Agent beim Hochfahren, Fenster beim Anmelden. |
| **Tailscale mitinstallieren** | Nur nötig, wenn du auch von unterwegs ranwillst. |

Alles davon lässt sich später im Fenster umstellen: der Agent kann dort
eingerichtet, gestartet, beendet und wieder entfernt werden. Du musst den
Installer nie wieder suchen.

### 2. Einrichtung abschließen

Nach der Installation öffnet sich das RemoteDesktop-Fenster und zeigt, was noch
fehlt. Zuerst fragt es, wie dein Handy den Rechner erreichen soll.

**Im Heimnetz** sind es zwei Schritte:

1. **Adresse festlegen** — sie steht meist schon da, du bestätigst sie nur
2. **Handy koppeln** — dazu gleich mehr

**Über Tailscale** sind es vier:

1. **Tailscale installieren** — falls es noch nicht da ist
2. **Bei Tailscale anmelden** — einmal im Browser
3. **Zertifikat holen** — damit die Verbindung verschlüsselt ist. Kostenlos,
   ein Knopfdruck
4. **Handy koppeln**

Abgehakte Schritte verschwinden nicht, sie werden grau. Wer Tailscale schon
benutzt, fängt einfach weiter hinten an.

### 3. App aufs Handy

`remotedesktop.apk` aus denselben Releases herunterladen und installieren.
Android fragt dabei einmal, ob es Apps aus dieser Quelle installieren darf — das
ist normal bei Apps außerhalb des Play Store.

### 4. Koppeln

Im Fenster am Rechner auf **Geräte koppeln…**. Dort steht ein sechsstelliger
Code und derselbe Code als QR-Bild. In der App auf **Gerät koppeln**, den
QR-Code scannen, fertig.

Der Code gilt **fünf Minuten** und funktioniert **einmal**. Danach kennen sich
die beiden Geräte dauerhaft — und zwar nur diese beiden.

---

## Was du danach hast

- **Bild und Steuerung** — Bildschirm live, Touchpad, Bildschirmtastatur.
  Mehrere Monitore lassen sich umschalten.
- **Medien** — Pause, weiter, Lautstärke, Titelanzeige. Auch mit gesperrtem
  Bildschirm.
- **Energie** — Ruhezustand, Herunterfahren, Neustart. Und **Aufwecken**, wenn
  ein zweites Gerät im selben Netz wach ist (siehe unten).
- **Aktionen** — was du in einer Textdatei auf dem Rechner hinterlegst: ein
  Programm starten, ein PowerShell-Skript, eine Tastenkombination, eine
  Webseite, oder eine Abfolge davon. Sie erscheinen als Knöpfe in der App.
- **Widget, Schnelleinstellungs-Kachel und App-Kürzel** — die Aktionen mit einem
  Tipp vom Startbildschirm, ohne die App zu öffnen.

### Aufwecken

Ein ausgeschalteter Rechner kann nichts empfangen — sein Netzwerkchip lauscht
auf ein Signal, das **nicht über einen Router kommt**. Ein wacher Rechner oder
ein kleiner Dienst im selben Netz muss es aussenden. Hast du nur einen einzigen
Rechner, ist der Weckknopf ausgegraut und erklärt warum. Das ist kein Fehler,
sondern der Normalfall.

Wer eine NAS oder einen Raspberry Pi hat, der ohnehin durchläuft, kann dort den
mitgelieferten **Waker** als Docker-Container betreiben — siehe
[`waker/README.md`](waker/README.md).

---

## Ist das sicher?

Der Agent hat vollständige Kontrolle über den Rechner. Das ist sein Zweck, und
deshalb ist der Zugang eng:

- **Kein Passwort, das man abtippt.** Beim Koppeln erzeugt dein Handy ein
  Schlüsselpaar. Der Rechner merkt sich nur den öffentlichen Teil; der private
  verlässt das Handy nie.
- **Jedes Gerät einzeln.** Es gibt kein Geheimnis, das für alle Rechner
  zugleich gilt. Ein verlorenes Handy wird an jedem Rechner einzeln
  widerrufen — sofort wirksam, auch mitten in einer Sitzung.
- **Nichts aus dem Internet erreichbar.** Alles läuft im Heimnetz oder im VPN.
- **Aktionen werden am Rechner festgelegt**, nie vom Handy geschickt. Die App
  kennt nur Kennungen wie `spotify`, keine Befehlszeilen. Es gibt keinen Weg
  über das Netz, diese Liste zu ändern.

Die ausführliche Durchsicht mit allen Befunden steht in
[`docs/SICHERHEIT.md`](docs/SICHERHEIT.md) — auch das, was bewusst so gelassen
wurde und warum.

**Verloren gegangenes Handy?** [`docs/SICHERHEIT.md`](docs/SICHERHEIT.md#wenn-ein-gerät-verloren-geht)
sagt in drei Schritten, was zu tun ist.

---

## Wenn etwas nicht geht

| Was du siehst | Was dahintersteckt |
|---|---|
| „… nicht erreichbar" | Der Rechner schläft, hat eine andere Adresse bekommen, oder das VPN läuft auf einem der beiden Geräte nicht. Im Fenster unter *Netz* nachsehen. |
| „… kennt dieses Gerät nicht mehr" | Die Kopplung wurde widerrufen oder der Rechner neu aufgesetzt. Einmal neu koppeln. |
| Der Weckknopf ist grau | Im Netz jenes Rechners ist gerade niemand wach, der ihn wecken könnte. Der Knopf sagt es beim Draufzeigen. |
| Das Fenster sagt, die Oberfläche fehle | Bei einer selbst gebauten Fassung: `cd app && npm run build` vergessen. |

---

## Selbst bauen

Alles außer der Windows-Ausführung baut auch unter Linux.

```bash
cd app        && npm install && npm test && npm run build   # Oberfläche
cd agent      && dotnet build                               # Dienst
cd desktop    && dotnet build                               # Fenster
cd setup.Tests && dotnet test                               # Einrichtungslogik
cd clients/android && npm run apk                           # APK
```

Der Aufbau im Überblick:

| Ordner | Was drin ist |
|---|---|
| `agent/` | C#, der Dienst auf dem gesteuerten Rechner |
| `desktop/` | C#, Tray-Programm und Fenster |
| `setup/` | C#, die Einrichtungslogik — geteilt von Installer und Fenster |
| `app/` | React, die Oberfläche für alle drei Plattformen |
| `clients/android/` | Capacitor + Kotlin, die APK |
| `waker/` | Node, der Weck-Dienst für NAS oder Pi |
| `installer/` | Inno-Setup-Skript |

Mehr dazu: [`docs/ARCHITEKTUR.md`](docs/ARCHITEKTUR.md).

---

## Lizenz

[Apache-2.0](LICENSE). Ohne Gewähr — wer dieses Programm einsetzt, gibt einem
weiteren Gerät die Kontrolle über seinen Rechner und entscheidet selbst, ob er
das will.

Tailscale ist ein eigenständiges Programm und gehört nicht zu diesem Projekt; es
wird bei der Einrichtung von seinen Herstellern geladen und steht unter deren
Bedingungen.
