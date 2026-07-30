---
description: Nächste offene Phase des V2-Umbaus umsetzen, prüfen, eintragen, committen
---

Setze die nächste offene Phase des V2-Umbaus um.

## Vorgehen

1. Lies `docs/TASKS-V2.md` vollständig. Das ist die Arbeitsanweisung — sie
   enthält Umfang, Abnahmekriterien und die Regeln, die für alle Phasen gelten.
2. **Prüfe zuerst „Offene Aufräumarbeiten"** ganz oben. Steht dort etwas,
   erledige es, bevor du eine Phase anfängst, und streiche es aus der Liste.
   Dieser Kram gehört zu keiner offenen Phase und würde sonst nie drankommen.
3. Prüfe den Arbeitsbaum: `git status`. Liegt dort unversionierte Arbeit einer
   abgebrochenen Sitzung, **beurteile sie erst**, statt sie zu überschreiben —
   siehe „Abgebrochene Vorgänger" unten.
5. Brauchst du das Warum hinter einer Entscheidung, steht es in
   `docs/PLAN-V2.md`. Lies dort gezielt den genannten Abschnitt, nicht alles.
6. Arbeite den Umfang ab. Tests zuerst, wo es sinnvoll ist.
7. Prüfe **jeden** Punkt unter „Abnahme" und zeige die Belege (Testausgabe,
   grep-Ergebnis). Nichts abhaken, was du nicht nachgewiesen hast.
8. Trage in `docs/TASKS-V2.md` ein: Status `erledigt`, Datum, Notizen zu allem,
   was abweicht. Punkte, die hier nicht prüfbar sind, kommen als
   `offen: Hardware` in die Sammelliste am Dokumentende. Kleinkram, der zu
   keiner Phase gehört, kommt nach oben unter „Offene Aufräumarbeiten".
9. Committe die Phase einzeln: `<typ>: <beschreibung>`, kein Push.

## Abgebrochene Vorgänger

Sitzungen können mitten in einer Phase enden. Liegt bei `git status`
unversionierte Arbeit:

- **Nicht wegwerfen und nicht blind übernehmen.** Erst lesen, dann die
  Abnahmekriterien der Phase dagegen halten.
- `git diff -- "*.test.*"` prüfen: wurden **bestehende** Tests verändert? Das
  ist fast immer ein Fehler des Vorgängers, kein legitimer Schritt.
- `git diff docs/` prüfen: wurden offene Punkte **gelöscht** statt abgehakt?
  Das ist schon einmal passiert. Solche Zeilen wiederherstellen.
- Ist die Arbeit brauchbar, ergänze das Fehlende und schließe die Phase ab.
  Ist sie es nicht, verwirf sie ausdrücklich und fang neu an — sag im Bericht,
  was du warum verworfen hast.

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
