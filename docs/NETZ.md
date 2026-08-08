# Wie Handy und Rechner zueinander finden

RemoteDesktop braucht **kein** Tailscale. Es braucht nur einen Weg, auf dem dein
Handy den Rechner erreicht. Dafür gibt es drei Möglichkeiten; du wählst sie im
Fenster unter **Netz**.

| | Wann | Von unterwegs | Aufwand |
|---|---|---|---|
| **Heimnetz** | Handy und Rechner hängen am selben Router | nein | keiner |
| **Tailscale** | du willst auch von unterwegs ran | ja | Konto + Programm |
| **Eigenes VPN** | du betreibst schon WireGuard, OpenVPN, ZeroTier … | ja | du richtest es ein |

---

## Heimnetz — der einfachste Fall

Der Rechner steht zuhause, das Handy hängt im WLAN. Mehr braucht es nicht: kein
VPN, kein Konto, keine Freigabe am Router.

Im Fenster steht unter **Netz** bereits die Adresse, die dein Router diesem
Rechner gegeben hat — meist etwas wie `192.168.178.20`. Du bestätigst sie, und
das war's.

**Der eine Haken:** Router vergeben Adressen auf Zeit. Bekommt der Rechner nach
einem Neustart eine andere, findet das Handy ihn nicht mehr. Zwei Wege dagegen,
beide einmalig:

- **Feste Adresse im Router eintragen** (FritzBox: *Heimnetz → Netzwerk →
  Gerät → „Diesem Netzwerkgerät immer die gleiche IPv4-Adresse zuweisen"*).
  Der sicherste Weg.
- **Den Namen statt der Adresse verwenden.** Viele Router beantworten den
  Rechnernamen, bei der FritzBox etwa `pc.fritz.box`. Trag ihn dann statt der
  Zahl ein.

**Was Heimnetz nicht kann:** von unterwegs. Sobald das Handy im Mobilfunk ist,
ist der Rechner weg. Dafür gibt es die anderen beiden Modi.

> **Rechner wecken** geht im Heimnetz übrigens weiterhin — das Magic Packet
> läuft ohnehin nur innerhalb eines Netzes, egal welchen Modus du wählst.

---

## Tailscale — der bequeme Weg nach draußen

Tailscale verbindet deine Geräte direkt miteinander, auch über das Mobilfunknetz,
ohne dass du am Router etwas freigibst. Das Fenster führt dich durch die drei
Schritte: installieren, anmelden, Zertifikat holen.

Für Privatnutzer ist es kostenlos (bis 3 Nutzer und 100 Geräte). Du brauchst ein
Konto — ein bestehendes bei Google, Microsoft oder GitHub genügt.

Der Vorteil gegenüber den anderen Modi: Tailscale stellt ein **öffentlich
anerkanntes Zertifikat** aus. Dann muss auf keinem Handy jemand etwas
bestätigen.

Wer einen eigenen Koordinationsserver betreibt (Headscale), trägt dessen Adresse
im selben Bereich ein; alles andere bleibt gleich.

**Der Name im Tailnet gehört ins Feld „Adresse dieses Rechners“.** Er ist
freiwillig, aber er ist das, was im QR-Code landet und was das Handy nachher
auflösen muss — etwa `pc.tailnet-1234.ts.net`. „Vorschlag“ liest ihn aus
Tailscale aus. Bleibt das Feld leer, nimmt der Agent den Namen aus seinem
Zertifikat: bei einem Zertifikat von Tailscale ist das derselbe, bei einem
selbst ausgestellten dagegen der Windows-Rechnername — und unter dem findet das
Handy im Tailnet nichts. Nach dem Ändern den Agent einmal beenden und starten.

---

## Eigenes VPN — du hast schon eins

Wenn du bereits WireGuard, OpenVPN, ZeroTier, Nebula oder das VPN deines
Routers benutzt, musst du **nicht** wechseln. RemoteDesktop braucht davon nur
eines zu wissen: **unter welcher Adresse dieser Rechner in deinem VPN erreichbar
ist.**

RemoteDesktop startet dein VPN nicht, prüft es nicht und richtet es nicht ein.
Das ist Absicht — es müsste dafür fremde Programme mit konfigurierbaren
Kommandozeilen ausführen, und genau diese Fläche gibt es im Agent bewusst
nirgends.

### So gehst du vor

1. **VPN wie gewohnt einrichten**, sodass Handy und Rechner sich darüber sehen.
2. **Adresse des Rechners im VPN herausfinden** (siehe unten).
3. Im Fenster **Netz → Eigenes VPN** wählen und die Adresse eintragen.
4. Der Agent stellt sich ein Zertifikat auf genau diese Adresse aus. Beim
   Koppeln bestätigst du es einmal auf dem Handy.
5. **Prüfen:** Handy ins VPN, dann im Browser `https://<adresse>:8443/health`
   aufrufen. Kommt dort `{"status":"ok"}`, steht die Verbindung.

### Wo die Adresse steht

| VPN | So findest du sie |
|---|---|
| **WireGuard** | Die `Address`-Zeile im `[Interface]`-Abschnitt der Konfiguration dieses Rechners, ohne die `/24` dahinter — etwa `10.8.0.3`. |
| **OpenVPN** | `ipconfig` in der Eingabeaufforderung, dann der Adapter „TAP-Windows" bzw. „OpenVPN Wintun". |
| **ZeroTier** | In der ZeroTier-Oberfläche steht die „Managed IP" des Netzwerks, meist `10.147.x.x`. |
| **Router-VPN (FritzBox u. ä.)** | Meist dieselbe LAN-Adresse wie zuhause — das VPN holt dich ins Heimnetz. Dann ist der Modus *Heimnetz* mit fester Adresse die richtigere Wahl. |
| **Tailscale-kompatibel (Headscale)** | Modus *Tailscale* mit eigenem Koordinator. |

### Was dein VPN können muss

- **Das Handy muss den Rechner direkt erreichen.** Ein VPN, das nur den
  Internetverkehr umleitet (die meisten kommerziellen „VPN-Dienste" wie NordVPN
  oder Mullvad), taugt dafür **nicht** — dort sehen sich deine eigenen Geräte
  gegenseitig nicht. Gebraucht wird ein VPN, das ein eigenes Netz aufspannt.
- **Port 8443 muss durchkommen.** Innerhalb eines VPN ist das der Normalfall;
  eine Firewall auf dem Rechner kann trotzdem dazwischenfunken. Windows fragt
  beim ersten Start des Agents danach — die Antwort muss „zulassen" sein.
- **Port 8442**, falls du das Zertifikat vom Handy abholen lassen willst
  (siehe unten). Er trägt ausschließlich die Zertifikatsdatei.

---

## Das mit dem Zertifikat

In den Modi *Heimnetz* und *Eigenes VPN* gibt es keine öffentliche Stelle, die
ein Zertifikat für `192.168.178.20` ausstellen würde. Also stellt der Agent es
sich selbst aus:

- Er legt **einmalig** eine eigene kleine Zertifizierungsstelle an
  (`C:\Program Files\RemoteDesktop\data\agentca.pfx`) und stellt sich damit sein
  Serverzertifikat aus. Läuft das ab oder ändert sich die Adresse, erneuert er
  es still — du merkst nichts davon.
- Dein Handy muss dieser Stelle **einmal** vertrauen. Die App führt dich hin:
  sie holt die Datei, vergleicht ihren Fingerabdruck mit dem, den sie beim
  Koppeln bekommen hat, und übergibt sie dem System.

**Warum der Vergleich zählt:** die Zertifikatsdatei wird unverschlüsselt
ausgeliefert (sie muss, sonst gäbe es ein Henne-Ei-Problem). Sie ist aber
öffentlich und ohne Geheimnis. Was sie echt macht, ist der Fingerabdruck — und
der kam über den Kopplungscode, also über deinen Bildschirm und nicht über das
Netz.

**Im Browser statt in der App:** dort kannst du das Zertifikat nicht
installieren, ohne es dem ganzen Gerät beizubringen. Du bekommst dann beim
ersten Aufruf eine Warnung, die du einmal bestätigst. Wer das nicht will, nimmt
Tailscale — dort ist das Zertifikat öffentlich anerkannt.

---

## Wechseln geht jederzeit

Der Modus liegt in `C:\Program Files\RemoteDesktop\data\setup.json` und lässt sich
im Fenster umstellen. Danach den Agent einmal neu starten (im Fenster unter
**Komponenten**), damit er sein Zertifikat auf die neue Adresse ausstellt.
Gekoppelte Geräte bleiben gekoppelt — die Kopplung hängt an Schlüsseln, nicht an
Adressen. Nur die Adresse musst du in der App am Gerät nachziehen.
