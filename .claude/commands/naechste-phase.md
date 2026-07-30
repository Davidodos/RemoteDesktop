---
description: Nächste offene Phase des V2-Umbaus umsetzen, prüfen, eintragen, committen
---

Setze die nächste offene Phase des V2-Umbaus um.

## Vorgehen

1. Lies `docs/TASKS-V2.md` vollständig. Das ist die Arbeitsanweisung — sie
   enthält Umfang, Abnahmekriterien und die Regeln, die für alle Phasen gelten.
2. Nimm die **erste Phase, deren Status nicht `erledigt` ist**. Nur diese.
3. Brauchst du das Warum hinter einer Entscheidung, steht es in
   `docs/PLAN-V2.md`. Lies dort gezielt den genannten Abschnitt, nicht alles.
4. Arbeite den Umfang ab. Tests zuerst, wo es sinnvoll ist.
5. Prüfe **jeden** Punkt unter „Abnahme" und zeige die Belege (Testausgabe,
   grep-Ergebnis). Nichts abhaken, was du nicht nachgewiesen hast.
6. Trage in `docs/TASKS-V2.md` ein: Status `erledigt`, Datum, Notizen zu allem,
   was abweicht. Punkte, die hier nicht prüfbar sind, kommen als
   `offen: Hardware` in die Sammelliste am Dokumentende.
7. Committe die Phase einzeln: `<typ>: <beschreibung>`, kein Push.

## Grenzen

- **Kein Vorgriff.** Fällt unterwegs etwas auf, das zu einer späteren Phase
  gehört, notiere es unter „Notizen" — baue es nicht ein.
- **Bestehende Tests bleiben grün und unverändert.** Musst du einen Test
  ändern, begründe es in den Notizen. Der Code passt sich den Tests an, nicht
  umgekehrt.
- Ist die Phase als **TOR** markiert, halte nach Abschluss an und berichte,
  statt die nächste zu beginnen.
- Umgebung: `export PATH="$HOME/.dotnet:$PATH"` und
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (steht in `~/.bashrc`). Es gibt
  hier **kein** Windows, kein Android SDK und keine echte Hardware.

## Bericht am Ende

Kurz und faktisch: Welche Phase, was gebaut, Testzahlen vorher/nachher, welche
Abnahmepunkte offen blieben und warum, welcher Commit.
