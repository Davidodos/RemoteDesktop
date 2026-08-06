; Installer für RemoteDesktop — Inno Setup 6.
;
; Seit V3 legt er **immer alles** ab: die Oberfläche, den Agent und die
; Weboberfläche. Das ist keine Bequemlichkeit, sondern die Antwort auf einen
; Befund aus dem ersten Durchlauf — wer nur den Agent gewählt hatte, saß ohne
; Fenster da und hatte keinen Weg, den Rest nachzuholen, außer den Installer
; wiederzufinden. Was auf diesem Rechner *aktiv* ist, entscheidet danach die
; Oberfläche; sie kann den Dienst eintragen, starten und wieder entfernen.
;
; Übrig bleibt eine einzige echte Wahl: ob Tailscale mitinstalliert werden soll.
; Es ist ein fremdes Programm mit eigenem Aktualisierungsweg — und seit V3 auch
; gar nicht mehr nötig, wenn Handy und Rechner im selben Netz hängen.
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

[Tasks]
; Tailscale wird heruntergeladen und nicht mitgeliefert: eine mitgelieferte
; Fassung veraltet im Paket, und niemand merkt es. Standardmäßig **aus** — wer
; den Rechner nur aus dem eigenen WLAN steuert, braucht es nicht.
Name: "tailscale"; Description: "Tailscale mitinstallieren (nur nötig, wenn du von unterwegs ranwillst)"; \
    Flags: unchecked; Check: NeedsTailscale

Name: "agentservice"; Description: "Diesen Rechner fernsteuerbar machen (Agent als Dienst eintragen)"
; Als untergeordnete Aufgabe geschrieben (Backslash im Namen) und nicht mit
; einem `Tasks:`-Verweis — den gibt es in dieser Sektion nicht. Inno rückt sie
; dadurch ein und lässt sie nur ankreuzen, solange die übergeordnete steht.
Name: "agentservice\autostart"; Description: "Agent beim Hochfahren starten"
Name: "autostartclient"; Description: "RemoteDesktop beim Anmelden starten"

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
; Zertifikate, privater Schlüssel, gekoppelte Geräte und das Netzprofil. Nur
; Administratoren und das System dürfen hinein — der Schlüssel des Agents liegt
; im Klartext, und wer ihn hat, ist der Agent.
Name: "{commonappdata}\RemoteDesktopAgent"; Permissions: admins-full system-full

[Icons]
Name: "{group}\RemoteDesktop"; Filename: "{app}\{#Exe}"
Name: "{group}\RemoteDesktop deinstallieren"; Filename: "{uninstallexe}"

[Run]
; Reihenfolge mit Absicht: erst Tailscale, dann der Dienst, dann das Fenster.
; Tailscale kommt als MSI, also über msiexec und nicht direkt. /qn installiert
; ohne weitere Rückfragen — die eine Frage, ob es überhaupt soll, ist auf der
; Aufgabenseite schon gestellt worden.
Filename: "{sys}\msiexec.exe"; Parameters: "/i ""{tmp}\tailscale-setup.msi"" /qn /norestart"; \
    Tasks: tailscale; Check: NeedsTailscale and TailscaleDownloaded; \
    StatusMsg: "Tailscale wird installiert…"; Flags: runhidden waituntilterminated

; Beim ersten Mal anlegen, danach nur den Starttyp nachziehen. Ein zweites
; `sc create` schlüge fehl, und der Fehler wäre für den Nutzer nicht von einem
; echten zu unterscheiden.
Filename: "{sys}\sc.exe"; Parameters: "create {#Service} binPath= ""{app}\RemoteDesktopAgent.exe"" start= {code:AgentStartType} DisplayName= ""RemoteDesktop Agent"""; \
    Tasks: agentservice; Check: not ServiceExists; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "config {#Service} binPath= ""{app}\RemoteDesktopAgent.exe"" start= {code:AgentStartType}"; \
    Check: ServiceExists; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "description {#Service} ""Macht diesen Rechner über RemoteDesktop fernsteuerbar."""; \
    Tasks: agentservice; Flags: runhidden waituntilterminated

; Nach einem Update wieder anwerfen — sonst wäre der Rechner nach jeder
; Aktualisierung stumm, bis jemand ihn neu startet. Nur, wenn er auch von allein
; starten soll.
Filename: "{sys}\sc.exe"; Parameters: "start {#Service}"; \
    Tasks: agentservice\autostart; Flags: runhidden waituntilterminated

Filename: "{app}\{#Exe}"; Description: "Einrichtung jetzt abschließen"; \
    Flags: postinstall nowait skipifsilent

[Registry]
; Der Autostart hängt am angemeldeten Benutzer, nicht am Rechner: das Fenster
; gehört einem Menschen. Derselbe Schlüssel, den die Oberfläche später
; umschaltet (siehe setup/Autostart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "RemoteDesktopClient"; \
    ValueData: """{app}\{#Exe}"""; \
    Tasks: autostartclient; Flags: uninsdeletevalue

; Ohne das Häkchen den alten Eintrag entfernen — er zeigte auf die Datei, die es
; seit V3 nicht mehr gibt, und Windows meldete bei jeder Anmeldung einen Fehler.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "RemoteDesktopClient"; \
    Tasks: not autostartclient; Flags: deletevalue

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#Service}"; Check: ServiceExists; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#Service}"; Check: ServiceExists; Flags: runhidden

[Code]
var
  Downloaded: Boolean;

function TailscaleDownloaded: Boolean;
begin
  Result := Downloaded;
end;

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

{ Ob Tailscale schon da ist. Wer es längst benutzt, soll es nicht ein zweites
  Mal installiert bekommen — und schon gar nicht ungefragt. }
function NeedsTailscale: Boolean;
begin
  Result := not FileExists(ExpandConstant('{pf}\Tailscale\tailscale.exe'));
end;

{ Der Starttyp des Dienstes kommt aus dem Häkchen und nicht aus einer Vorgabe.
  „auto" heißt: läuft, sobald der Rechner an ist, auch ohne Anmeldung — genau
  das, was man von einem fernsteuerbaren Rechner erwartet. „demand" lässt ihn
  liegen, bis jemand ihn startet. Deinstalliert wird er dabei nie; siehe die
  Begründung in setup/Autostart.cs. }
function AgentStartType(Param: string): String;
begin
  if WizardIsTaskSelected('agentservice\autostart') then
    Result := 'auto'
  else
    Result := 'demand';
end;

{ Tailscale wird zur Laufzeit geholt, nicht mitgepackt.

  Scheitert der Download, bricht die Installation *nicht* ab. Alles andere ist
  dann installiert und die Oberfläche führt zum fehlenden Schritt hin — das ist
  ungleich besser, als wegen eines fremden Servers alles zurückzurollen. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and WizardIsTaskSelected('tailscale') and NeedsTailscale then
  begin
    try
      DownloadTemporaryFile(
        'https://pkgs.tailscale.com/stable/tailscale-setup-latest-amd64.msi',
        'tailscale-setup.msi', '', nil);
      Downloaded := True;
    except
      Downloaded := False;
      MsgBox('Tailscale ließ sich nicht herunterladen. Du kannst es später von Hand ' +
             'installieren — die Einrichtung im RemoteDesktop-Fenster führt dich hin.',
             mbInformation, MB_OK);
    end;
  end;
end;
