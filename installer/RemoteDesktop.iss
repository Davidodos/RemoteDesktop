; Installer für RemoteDesktop — Inno Setup 6.
;
; Modular: Agent, Client und Tailscale lassen sich einzeln wählen. Das ist keine
; Spielerei — ein Rechner im Keller braucht nur den Agent und nie ein Fenster,
; ein Arbeitslaptop nur den Client und niemals einen Dienst, der Fremdzugriff
; erlaubt.
;
; Gebaut wird er unter Windows mit `iscc installer\RemoteDesktop.iss`, nachdem
; `publish\` aus dem Release-Workflow daliegt. Auf dem Linux-Container ist er
; nicht übersetzbar — was er *entscheidet*, steht deshalb in setup/ und ist dort
; geprüft.

#define Name "RemoteDesktop"
#define Publisher "RemoteDesktop"
#define Url "https://github.com/Davidodos/RemoteDesktop"
#define Service "RemoteDesktopAgent"

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
; Der Agent wird als Dienst eingetragen — das geht nur mit Adminrechten. Der
; Client allein käme ohne aus; zwei Installer dafür wären dem Nutzer gegenüber
; die schlechtere Antwort als eine Rückfrage.
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Types]
Name: "full"; Description: "Agent und Client (empfohlen)"
Name: "agent"; Description: "Nur Agent — dieser Rechner soll fernsteuerbar sein"
Name: "client"; Description: "Nur Client — von diesem Rechner aus andere steuern"
Name: "custom"; Description: "Eigene Auswahl"; Flags: iscustom

[Components]
Name: "agent"; Description: "Agent — macht diesen Rechner fernsteuerbar"; Types: full agent
Name: "client"; Description: "Client — Fenster, mit dem du andere Rechner steuerst"; Types: full client
; Tailscale ist ein fremdes Programm mit eigenem Aktualisierungsweg. Deshalb
; wird es heruntergeladen und nicht mitgeliefert: eine mitgelieferte Fassung
; veraltet im Paket, und niemand merkt es.
Name: "tailscale"; Description: "Tailscale mitinstallieren (nötig, wenn es noch nicht da ist)"; Types: full agent client

[Tasks]
Name: "autostartagent"; Description: "Agent beim Hochfahren starten"; Components: agent
Name: "autostartclient"; Description: "Client beim Anmelden starten"; Components: client

[Files]
Source: "..\publish\release\RemoteDesktopAgent.exe"; DestDir: "{app}"; Components: agent; Flags: ignoreversion
Source: "..\agent\appsettings.json"; DestDir: "{app}"; Components: agent; Flags: onlyifdoesntexist
Source: "..\publish\client\*"; DestDir: "{app}\client"; Components: client; Flags: ignoreversion recursesubdirs

[Dirs]
; Zertifikat, privater Schlüssel und die gekoppelten Geräte. Nur Administratoren
; und das System dürfen hinein — der Schlüssel des Agents liegt im Klartext, und
; wer ihn hat, ist der Agent.
Name: "{commonappdata}\RemoteDesktopAgent"; Components: agent; Permissions: admins-full system-full

[Icons]
Name: "{group}\RemoteDesktop"; Filename: "{app}\client\RemoteDesktopClient.exe"; Components: client
Name: "{group}\RemoteDesktop deinstallieren"; Filename: "{uninstallexe}"

[Run]
; Reihenfolge mit Absicht: erst Tailscale, dann der Dienst, dann das Fenster.
; Der Agent braucht ein Zertifikat, und das stellt Tailscale aus.
; Tailscale kommt als MSI, also über msiexec und nicht direkt. /qn installiert
; ohne weitere Rückfragen — die eine Frage, ob es überhaupt soll, ist auf der
; Komponentenseite schon gestellt worden.
Filename: "{sys}\msiexec.exe"; Parameters: "/i ""{tmp}\tailscale-setup.msi"" /qn /norestart"; \
    Components: tailscale; Check: NeedsTailscale and TailscaleDownloaded; \
    StatusMsg: "Tailscale wird installiert…"; Flags: runhidden waituntilterminated

; Beim ersten Mal anlegen, danach nur den Starttyp nachziehen. Ein zweites
; `sc create` schlüge fehl, und der Fehler wäre für den Nutzer nicht von einem
; echten zu unterscheiden.
Filename: "{sys}\sc.exe"; Parameters: "create {#Service} binPath= ""{app}\RemoteDesktopAgent.exe"" start= {code:AgentStartType} DisplayName= ""RemoteDesktop Agent"""; \
    Components: agent; Check: not ServiceExists; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "config {#Service} binPath= ""{app}\RemoteDesktopAgent.exe"" start= {code:AgentStartType}"; \
    Components: agent; Check: ServiceExists; Flags: runhidden waituntilterminated

Filename: "{sys}\sc.exe"; Parameters: "description {#Service} ""Macht diesen Rechner über RemoteDesktop fernsteuerbar."""; \
    Components: agent; Flags: runhidden waituntilterminated

; Nach einem Update wieder anwerfen — sonst wäre der Rechner nach jeder
; Aktualisierung stumm, bis jemand ihn neu startet. Nur, wenn er auch von allein
; starten soll.
Filename: "{sys}\sc.exe"; Parameters: "start {#Service}"; \
    Components: agent; Tasks: autostartagent; Flags: runhidden waituntilterminated

Filename: "{app}\client\RemoteDesktopClient.exe"; Description: "Einrichtung jetzt abschließen"; \
    Components: client; Flags: postinstall nowait skipifsilent

[Registry]
; Der Autostart des Clients hängt am angemeldeten Benutzer, nicht am Rechner:
; das Fenster gehört einem Menschen. Derselbe Schlüssel, den das
; Einstellungsfenster später umschaltet (siehe setup/Autostart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "RemoteDesktopClient"; \
    ValueData: """{app}\client\RemoteDesktopClient.exe"""; \
    Tasks: autostartclient; Flags: uninsdeletevalue

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#Service}"; Components: agent; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "delete {#Service}"; Components: agent; Flags: runhidden

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
  if WizardIsTaskSelected('autostartagent') then
    Result := 'auto'
  else
    Result := 'demand';
end;

{ Tailscale wird zur Laufzeit geholt, nicht mitgepackt: eine mitgelieferte
  Fassung veraltet im Paket, und niemand merkt es.

  Scheitert der Download, bricht die Installation *nicht* ab. Agent und Client
  sind dann installiert und die Einrichtung im Fenster führt zum fehlenden
  Schritt hin — das ist ungleich besser, als wegen eines fremden Servers alles
  zurückzurollen. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and WizardIsComponentSelected('tailscale') and NeedsTailscale then
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
