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

[Dirs]
; Zertifikate, privater Schlüssel, gekoppelte Geräte und das Netzprofil — alles
; in einem Ordner neben dem Programm. Nur Administratoren und das System dürfen
; hinein: der Schlüssel des Agents liegt im Klartext, und wer ihn hat, ist der
; Agent.
;
; Der Installer kennt den Inhalt nicht, also überschreibt ein Update ihn auch
; nicht — Kopplungen und die eigene Zertifizierungsstelle überstehen jede neue
; Fassung. Beim Deinstallieren wird er dagegen mit weggeräumt, siehe
; [UninstallDelete]: was zum Programm gehört, soll auch mit ihm verschwinden.
Name: "{app}\data"; Permissions: admins-full system-full

[UninstallDelete]
; Der Datenordner. Er entsteht zur Laufzeit, deshalb weiß der Uninstaller sonst
; nichts von ihm — und ein „deinstalliert" mit zurückbleibendem privatem
; Schlüssel wäre die unangenehmste Art von Rückstand.
Type: filesandordirs; Name: "{app}\data"

[Icons]
Name: "{group}\RemoteDesktop"; Filename: "{app}\{#Exe}"
Name: "{group}\RemoteDesktop deinstallieren"; Filename: "{uninstallexe}"

[Run]
; Nur noch das Fenster. Der Dienst wird dort eingetragen und gestartet, nicht
; hier — siehe die Begründung unter [Tasks].
Filename: "{app}\{#Exe}"; Description: "RemoteDesktop einrichten"; \
    Flags: postinstall nowait skipifsilent

[UninstallRun]
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

{ Vor dem Kopieren den Dienst anhalten.

  Eine laufende .exe lässt sich unter Windows nicht ersetzen. Ohne diesen
  Schritt scheitert jedes Update an genau der Datei, um die es geht — und der
  Installer meldet einen Dateizugriffsfehler, mit dem niemand etwas anfangen
  kann. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';

  if ServiceExists then
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#Service}', '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Nichts weiter zu entscheiden.

  Was früher hier stand — ob Tailscale mitkommt, welchen Starttyp der Dienst
  bekommt, ob er gleich losläuft —, entscheidet die Einrichtung im Fenster. Der
  Installer weiß von alldem nichts mehr, und das ist der Punkt: er kann nur
  Dateien ablegen, und genau das tut er. }
