; Installer für RemoteDesktop — Inno Setup 6.
;
; Seit V3 legt er **immer alles** ab: die Oberfläche, den Agent und die
; Weboberfläche. Das ist keine Bequemlichkeit, sondern die Antwort auf einen
; Befund aus dem ersten Durchlauf — wer nur den Agent gewählt hatte, saß ohne
; Fenster da und hatte keinen Weg, den Rest nachzuholen, außer den Installer
; wiederzufinden. Was auf diesem Rechner *aktiv* ist, entscheidet danach die
; Oberfläche; sie kann den Dienst eintragen, starten und wieder entfernen.
;
; Seit v1.3.0 hat er gar keine Wahl mehr anzubieten: er legt Dateien ab, mehr
; nicht. Ob der Agent eingetragen wird, auf welchem Weg dieser Rechner erreichbar
; sein soll und was beim Hochfahren mitkommt, fragt die Einrichtung im Fenster —
; in der Reihenfolge, in der die Antworten aufeinander aufbauen, und ohne dass
; irgendetwas losläuft, bevor sie durch ist.
;
; Gebaut wird er unter Windows mit `iscc installer\RemoteDesktop.iss`, nachdem
; `publish\` aus dem Release-Workflow daliegt. Auf dem Linux-Container ist er
; nicht übersetzbar — was er *entscheidet*, steht deshalb in setup/ und ist dort
; geprüft.

#define Name "RemoteDesktop"
#define Publisher "RemoteDesktop"
#define Url "https://github.com/Davidodos/RemoteDesktop"
#define Service "RemoteDesktopAgent"
#define Exe "RemoteDesktop.exe"

; Beim Bauen mit /DVersion=1.2.0 aus dem Git-Tag übergeben.
#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{8E7C4C2A-6B1D-4C3E-9A55-1F0B7A934D62}
AppName={#Name}
AppVersion={#Version}
AppPublisher={#Publisher}
AppPublisherURL={#Url}
DefaultDirName={autopf}\{#Name}
DefaultGroupName={#Name}
OutputBaseFilename=RemoteDesktop-Setup-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Dasselbe Zeichen wie auf dem Handy — es steht auf dem Installer selbst, in der
; Programmliste von Windows und auf den Verknuepfungen. Es entsteht aus
; assets/icon.svg mit `node scripts/icons.mjs`.
SetupIconFile=..\desktop\RemoteDesktop.ico
UninstallDisplayIcon={app}\{#Exe}
; Der Agent kann als Dienst eingetragen werden — das geht nur mit Adminrechten.
; Die Oberfläche selbst braucht sie nicht und läuft später ohne.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

; Keine Aufgaben mehr.
;
; **Der Befund dahinter:** hier standen bis v1.2.0 vier Häkchen — Dienst
; eintragen, Agent beim Hochfahren starten, Fenster beim Anmelden starten,
; Tailscale mitinstallieren. Sie wurden gesetzt, bevor irgendjemand die Frage
; verstanden hatte, und der Agent lief danach, ob gewollt oder nicht. Alles
; davon entscheidet jetzt die Einrichtung im Fenster (desktop/Pages/SetupPage.cs):
; sie fragt in der Reihenfolge, in der die Antworten aufeinander aufbauen, und
; startet den Agent erst, wenn er weiß, unter welchem Namen er sich ausweisen
; soll.
;
; Der Installer legt Dateien ab. Mehr nicht.

[Files]
; Alles nebeneinander in einem Ordner. Die Oberfläche sucht die Programmdatei des
; Agents genau dort (siehe desktop/WindowsSetup.cs, AgentBinary.Locate).
Source: "..\publish\client\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\publish\release\RemoteDesktopAgent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\agent\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[InstallDelete]
; Bis V2 lag die Oberfläche in einem Unterordner und hieß anders. Bleibt sie
; liegen, startet nach einem Update womöglich die alte Fassung aus dem
; Autostart — und niemand sieht, warum sich nichts geändert hat.
Type: filesandordirs; Name: "{app}\client"

; Die Weboberfläche wird geleert und nicht überschrieben.
;
; **Der Befund dahinter:** Vite gibt jeder Datei einen Namen mit Prüfsumme.
; Ein Update legt die neuen daneben, und die alten bleiben für immer liegen —
; nach ein paar Fassungen steht dort ein Dutzend Stände übereinander. Die
; `index.html` verweist zwar nur auf den neuesten, aber „liegt nur herum" und
; „läuft nicht" sind zwei verschiedene Zusagen, und nur die zweite ist die,
; die hier gelten soll.
Type: filesandordirs; Name: "{app}\app"

; Die Rückstände des Agent-Selbst-Updates.
;
; Es legt die alte Fassung als `.old` beiseite, die geladene als `.new` und
; merkt sich den Versuch in `.update` (siehe agent/Services/AgentUpdater.cs).
; Das `.old` ist eine vollständige, startbare Programmdatei einer älteren
; Fassung — genau die Art Rückstand, die nach einem Update nichts mehr zu
; suchen hat. Das `.update` muss mit, sonst hält der frisch installierte Agent
; seine eigene neue Fassung für einen eben erst gescheiterten Versuch und
; überspringt sie eine halbe Stunde lang.
Type: files; Name: "{app}\RemoteDesktopAgent.exe.old"
Type: files; Name: "{app}\RemoteDesktopAgent.exe.new"
Type: files; Name: "{app}\RemoteDesktopAgent.exe.update"

; Der Zwischenspeicher der Anzeigekomponente: kompiliertes JavaScript der
; vorigen Oberfläche und ein etwaiger Service Worker aus einer Fassung vor
; v1.3.0. Der `localStorage` daneben — die Geräteliste des Fensters — bleibt
; ausdrücklich stehen: gekoppelte Geräte überleben ein Update.
Type: filesandordirs; Name: "{localappdata}\RemoteDesktop\EBWebView\Default\Code Cache"
Type: filesandordirs; Name: "{localappdata}\RemoteDesktop\EBWebView\Default\Service Worker"

[Dirs]
; Zertifikate, privater Schlüssel, gekoppelte Geräte und das Netzprofil — alles
; in einem Ordner neben dem Programm.
;
; Der Installer kennt den Inhalt nicht, also überschreibt ein Update ihn auch
; nicht — Kopplungen und die eigene Zertifizierungsstelle überstehen jede neue
; Fassung. Beim Deinstallieren wird er dagegen mit weggeräumt, siehe
; [UninstallDelete]: was zum Programm gehört, soll auch mit ihm verschwinden.
;
; „users-modify" seit 31h, und das ist eine bewusste Abwägung. Der Agent läuft
; erhöht und kommt ohnehin hinein; das Fenster nicht. Es muss aber zwei Dateien
; schreiben können: seinen eigenen Ausweis (clientkey.json) und die
; Gegenrichtung einer Kopplung (clients.json), wenn der Agent eingerichtet, aber
; gestoppt ist. Die Alternative wäre eine Rückfrage von Windows bei jeder
; Kopplung — für einen Rechner, der nur andere steuern soll, bei jeder einzelnen.
;
; Was das kostet: ein zweiter, nicht-administrativer Benutzer dieses Rechners
; könnte sich selbst in die clients.json eintragen. Lesen durfte er den Ordner
; ohnehin schon (er erbt die Rechte von „Programme"), und der Agent läuft in der
; Sitzung genau des Benutzers, der ihn eingerichtet hat.
Name: "{app}\data"; Permissions: admins-full system-full users-modify

[UninstallDelete]
; Der Datenordner. Er entsteht zur Laufzeit, deshalb weiß der Uninstaller sonst
; nichts von ihm — und ein „deinstalliert" mit zurückbleibendem privatem
; Schlüssel wäre die unangenehmste Art von Rückstand.
Type: filesandordirs; Name: "{app}\data"

; Und der Programmordner als Ganzes — als letzter Eintrag, denn die Reihenfolge
; ist hier die Reihenfolge der Ausführung.
;
; **Der Befund dahinter:** Inno räumt weg, was es abgelegt hat. Was zur Laufzeit
; dazukam, kennt es nicht: die `.old` und `.new` des Agent-Selbst-Updates, die
; `appsettings.json`, wenn sie jemand angefasst hat, ein Protokoll. Übrig blieb
; ein Ordner mit einer startbaren `RemoteDesktopAgent.exe.old` darin — nach
; einer Deinstallation, die sich für abgeschlossen hielt. Jetzt geht der Ordner
; mit, und danach ist nichts mehr manuell wegzuräumen.
Type: filesandordirs; Name: "{app}"

; Der Zwischenspeicher im Benutzerprofil: der Ordner von WebView2 (darin liegt
; der localStorage der Oberfläche — also die Geräteliste des Fensters samt
; Zugangsdaten) und die Absturzprotokolle.
;
; **Der Befund dahinter:** wer deinstallierte, den Programmordner löschte und
; neu installierte, fand seine alten Geräte wieder — mitsamt Zugangsdaten zu
; Kopplungen, die auf der Gegenseite längst weg waren. Der Uninstaller kannte
; diesen Ordner nicht, weil ihn niemand angelegt hatte außer WebView2 selbst.
;
; {localappdata} zeigt auf das Profil des Benutzers, der deinstalliert. Läuft
; die Deinstallation erhöht unter einem anderen Konto, greift zusätzlich die
; Schleife in CurUninstallStepChanged weiter unten.
Type: filesandordirs; Name: "{localappdata}\RemoteDesktop"

[Icons]
Name: "{group}\RemoteDesktop"; Filename: "{app}\{#Exe}"
Name: "{group}\RemoteDesktop deinstallieren"; Filename: "{uninstallexe}"

[Run]
; Der Agent lief vor dem Kopieren und soll danach wieder laufen.
;
; **Der Befund dahinter:** ein Update über die Oberfläche startet den Installer
; still. Dabei wird der Agent angehalten (PrepareToInstall) — und danach startete
; ihn niemand. Bis zur nächsten Anmeldung war der Rechner unerreichbar, und
; genau das ist der Fall, in dem niemand davorsitzt, der es merken könnte.
; Gibt es die Aufgabe nicht, weil dieser Rechner nur steuern soll, geht der
; Aufruf ins Leere und niemand sieht es.
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN {#Service}"; \
    Check: AgentTaskExists; Flags: runhidden

; Und das Fenster — über den Explorer.
;
; **Der Befund dahinter (19.08.2026):** nach einem Update, das von einem
; gekoppelten Gerät aus angestoßen wurde, blieb das Fenster zu und musste von
; Hand gestartet werden. Es war ausgeschlossen worden, weil bei einem Fernupdate
; „niemand davorsitzt" — nur stimmt das nicht: der Agent läuft ausschließlich in
; der Sitzung eines angemeldeten Benutzers, also sitzt dort immer jemand.
;
; „runasoriginaluser" allein genügt dafür nicht. Es benutzt den Token des
; Prozesses, der das Setup gestartet hat — und das war hier der Agent, der
; selbst schon erhöht läuft. Das Fenster wäre also erhöht gestartet worden und
; hätte seinen WebView2-Speicher in ein anderes Profil gelegt: die Geräteliste
; wäre nach jedem Update leer.
;
; Der Explorer läuft dagegen immer als der angemeldete Benutzer und ohne
; Erhöhung. Ein Programm, das er startet, erbt genau das.
;
; **Und ohne „postinstall"** (19.08.2026). Das war der Grund, warum das Fenster
; nach einem Fern-Update zublieb: ein Eintrag mit diesem Flag ist eine
; Ankreuzfläche auf der Abschlussseite des Assistenten — und die gibt es bei
; „/VERYSILENT" nicht. Ohne das Flag läuft er einfach, still wie laut.
;
; „runasoriginaluser" bleibt daneben stehen: bei einer Installation von Hand
; genügt es allein, und es schadet nicht, wo es nichts ausrichtet.
Filename: "{sys}\explorer.exe"; Parameters: """{app}\{#Exe}"""; \
    Check: ShouldOpenWindow; Flags: nowait runasoriginaluser

[UninstallRun]
; Erst anhalten, was läuft.
;
; **Der Befund dahinter:** eine Deinstallation ließ den Programmordner stehen.
; Der Grund war nicht der Uninstaller, sondern eine laufende Datei darin — eine
; .exe, die Windows in Benutzung hat, lässt sich nicht löschen. Der Agent wird
; über seine Aufgabe beendet; das Fenster und alles, was ein
; Selbst-Update davon hinterlassen hat, über den Namen der Programmdatei. Das
; „/F" ist hier richtig: es wird ohnehin gleich alles gelöscht.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM RemoteDesktopAgent.exe /F /T"; Flags: runhidden
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#Exe} /F /T"; Flags: runhidden

; Der Agent läuft seit v1.3.0 als geplante Aufgabe in der Sitzung des Benutzers
; und nicht mehr als Dienst — der Grund steht in setup/AgentTask.cs. Beides wird
; hier abgeräumt: die Aufgabe, und der Dienst einer älteren Installation.
Filename: "{sys}\schtasks.exe"; Parameters: "/End /TN {#Service}"; Flags: runhidden
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN {#Service} /F"; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "stop {#Service}"; Check: ServiceExists; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#Service}"; Check: ServiceExists; Flags: runhidden

[Code]
{ Ob der Dienst schon eingetragen ist — dann ist dies ein Update und keine
  Erstinstallation. }
function ServiceExists: Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Services\{#Service}');
end;

{ Ob die geplante Aufgabe des Agents eingetragen ist.

  Nur dann wird sie nach dem Kopieren wieder gestartet. Auf einem Rechner, der
  nur andere steuern soll, gibt es sie nicht — dort wäre ein fehlgeschlagener
  Aufruf die einzige Spur eines Vorgangs, der gar nicht vorgesehen ist. }
function AgentTaskExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN {#Service}', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

{ Ob nach der Installation das Fenster aufgehen soll.

  Es soll — auch nach einem Update von einem gekoppelten Gerät aus: der Agent
  läuft in der Sitzung eines angemeldeten Benutzers, also sitzt vor diesem
  Rechner immer jemand, und der hatte vor dem Update ein Fenster offen.

  /NOLAUNCH bleibt trotzdem, für den Fall, dass jemand von Hand still
  installieren will. Der Agent gibt es nicht mehr mit. }
function ShouldOpenWindow: Boolean;
var
  Index: Integer;
begin
  Result := True;

  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), '/NOLAUNCH') = 0 then
    begin
      Result := False;
      Exit;
    end;
end;

{ Beendet eine Programmdatei und wartet, bis sie wirklich weg ist.

  Ein `schtasks /End` kommt zurück, sobald der Auftrag abgesetzt ist, nicht
  wenn der Prozess weg ist. Zwischen beidem liegen unter Last durchaus ein paar
  Sekunden — und in dieser Lücke scheitert das Kopieren an genau der Datei, um
  die es geht. }
procedure StopAndWait(const ExeName: String);
var
  Attempt, ResultCode: Integer;
begin
  for Attempt := 1 to 20 do
  begin
    { tasklist meldet ohne Treffer den Text „Keine Tasks…" und trotzdem 0.
      Deshalb über taskkill mit /FI: das liefert einen Code, auf den Verlass
      ist — 128, wenn kein Prozess dieses Namens läuft. }
    if not Exec(ExpandConstant('{sys}\taskkill.exe'),
                '/IM ' + ExeName + ' /F', '',
                SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Exit;

    if ResultCode <> 0 then
      Exit;

    Sleep(250);
  end;
end;

{ Vor dem Kopieren alles anhalten, was aus dem Programmordner läuft.

  Eine laufende .exe lässt sich unter Windows nicht ersetzen. Ohne diesen
  Schritt scheitert jedes Update an genau der Datei, um die es geht — und der
  Installer meldet einen Dateizugriffsfehler, mit dem niemand etwas anfangen
  kann.

  Drei Dinge, und in dieser Reihenfolge: die geplante Aufgabe (damit sie den
  Agent nicht sofort neu startet), der Dienst einer älteren Installation, und
  danach die Prozesse selbst — mit Warten, bis sie wirklich weg sind. Das
  Fenster gehört dazu: bei einem Update von einem gekoppelten Gerät aus steht
  es offen, und niemand ist da, der es schließt. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';

  { Die geplante Aufgabe — der übliche Fall seit v1.3.0. }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN {#Service}', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Und der Dienst einer älteren Installation, falls er noch läuft. }
  if ServiceExists then
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#Service}', '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);

  StopAndWait('RemoteDesktopAgent.exe');
  StopAndWait('{#Exe}');
end;

// Die Zwischenspeicher aller Benutzerprofile wegräumen.
//
// Der Eintrag in UninstallDelete trifft nur das Profil des Kontos, unter dem
// die Deinstallation läuft — und das ist bei einer erhöhten Deinstallation
// nicht zwangsläufig das Konto, das RemoteDesktop benutzt hat. Deshalb hier
// noch einmal über alle Profile: derselbe Ordner, überall.
//
// Fehlschläge bleiben still. Ein Profil, an das der Uninstaller nicht
// herankommt, ist kein Grund, eine Deinstallation abzubrechen.
//
// Zwei Fallen stecken in diesem Kommentar, und beide haben zugeschlagen:
// Inno prüft **jede** Zeile auf ein Abschnitts-Tag und trimmt dabei die
// Einrückung — eine eingerückte Zeile, die mit einer eckigen Klammer beginnt,
// ist ein „Invalid section tag". Und ein Kommentar in geschweiften Klammern
// endet an der ersten schließenden Klammer, also mitten in einer Konstanten
// wie der für das lokale Anwendungsdatenverzeichnis. Deshalb hier // statt
// geschweifter Klammern.
procedure RemoveUserCaches;
var
  Profiles: String;
  Search: TFindRec;
begin
  Profiles := ExpandConstant('{sd}\Users');

  if not DirExists(Profiles) then
    Exit;

  if FindFirst(Profiles + '\*', Search) then
  begin
    try
      repeat
        if (Search.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0)
           and (Search.Name <> '.') and (Search.Name <> '..') then
          DelTree(Profiles + '\' + Search.Name
                  + '\AppData\Local\RemoteDesktop', True, True, True);
      until not FindNext(Search);
    finally
      FindClose(Search);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveUserCaches;
end;

{ Nichts weiter zu entscheiden.

  Was früher hier stand — ob Tailscale mitkommt, welchen Starttyp der Dienst
  bekommt, ob er gleich losläuft —, entscheidet die Einrichtung im Fenster. Der
  Installer weiß von alldem nichts mehr, und das ist der Punkt: er kann nur
  Dateien ablegen, und genau das tut er. }
