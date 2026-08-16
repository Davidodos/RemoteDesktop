# Wie Handy und Rechner zueinander finden

RemoteDesktop braucht **kein** Tailscale. Es braucht nur einen Weg, auf dem dein
Handy den Rechner erreicht. Dafür gibt es vier Möglichkeiten; du wählst sie im
Einrichtungsassistenten und später jederzeit im Fenster unter **Einstellungen →
Netz**.

| | Wann | Von unterwegs | Aufwand |
|---|---|---|---|
| **Heimnetz** | Handy und Rechner hängen am selben Router | nein | keiner |
| **Tailscale** | du willst auch von unterwegs ran | ja | Konto + Programm |
| **Headscale** | du betreibst den Koordinator selbst | ja | Server + Programm |
| **Anderer VPN-Anbieter** | du betreibst schon WireGuard, OpenVPN, ZeroTier … | ja | du richtest es ein |

In jedem Modus gehört **eine Adresse** dazu: der Name oder die IP, unter der
dieser Rechner erreichbar ist. Sie steht später im QR-Code, und genau sie muss
dein Handy auflösen können. Ohne sie geht es im Assistenten nicht weiter.

---

## Heimnetz — der einfachste Fall

Der Rechner steht zuhause, das Handy hängt im WLAN. Mehr braucht es nicht: kein
VPN, kein Konto, keine Freigabe am Router.

Im Fenster steht unter **Einstellungen → Netz** bereits die Adresse, die dein Router diesem
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
ist der Rechner weg. Dafür gibt es die anderen Modi.

> **Der Agent läuft in deiner Sitzung** (als geplante Aufgabe, ausgelöst bei der
> Anmeldung) und nicht als Dienst. Nur dort sieht er einen Bildschirm und darf
> Eingaben machen. Das heißt auch: nach dem Aufwecken ist der Rechner erst
> erreichbar, wenn jemand angemeldet ist.

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

**Der Name in deinem Tailscale-Netz gehört ins Feld „Adresse dieses Rechners“**
— etwa `pc.tailnet-1234.ts.net`. Genau er landet im QR-Code, und genau ihn muss
das Handy auflösen. „Vorschlag“ liest ihn aus Tailscale aus. Er ist **Pflicht**:
bliebe das Feld leer, nähme der Agent den Namen aus seinem Zertifikat, und bei
einem selbst ausgestellten ist das der Windows-Rechnername — unter dem findet
das Handy dort nichts.

Der Einrichtungsassistent lässt „Weiter“ deshalb erst zu, wenn beides steht: die
Adresse **und** ein Zertifikat von Tailscale, das auf genau diese Adresse
lautet. Ein Assistent, den man mit einer halben Einrichtung verlassen kann,
verschiebt den Fehlschlag nur auf das Handy.

Nach einer Änderung im Fenster unter **Einstellungen → Netz** den Agent einmal
beenden und
starten.

---

## Headscale — dein eigener Koordinator

Headscale ist derselbe Tailscale-Client an einem Koordinator, den du selbst
betreibst. Seit v1.3.0 ist es ein **eigener Modus** und kein Zusatzfeld unter
„Tailscale“ mehr: die Schritte sind fast dieselben, einer aber nicht.

1. Im Assistenten **Headscale** wählen.
2. Die Adresse deines Servers eintragen, etwa `https://headscale.example.org`.
   Ohne sie geht es nicht weiter — sonst wäre es Tailscale.
3. **Jetzt anmelden**: der Client meldet sich mit `--login-server` an deinem
   Server an.
4. Den Namen dieses Rechners eintragen — „Vorschlag“ liest ihn aus.

**Der eine Unterschied:** Zertifikate stellt der Dienst von Tailscale aus, ein
Headscale-Server nicht — die Stelle, die `tailscale cert` dafür braucht, gehört
zum Dienst von Tailscale. Der Schritt entfällt hier deshalb ganz. Der Agent
stellt sich selbst eins aus, und dein Handy bestätigt es einmal beim Koppeln —
genau wie im Heimnetz.

---

## Anderer VPN-Anbieter — du hast schon eins

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
3. Im Assistenten **Anderer VPN-Anbieter** wählen und die Adresse eintragen.
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
| **Headscale** | Eigener Modus *Headscale* — siehe oben. |

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

In den Modi *Heimnetz*, *Headscale* und *Anderer VPN-Anbieter* gibt es keine
öffentliche Stelle, die ein Zertifikat für `192.168.178.20` ausstellen würde.
Also stellt der Agent es sich selbst aus:

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
**Übersicht**), damit er sein Zertifikat auf die neue Adresse ausstellt.
Gekoppelte Geräte bleiben gekoppelt — die Kopplung hängt an Schlüsseln, nicht an
Adressen. Nur die Adresse musst du in der App am Gerät nachziehen.
