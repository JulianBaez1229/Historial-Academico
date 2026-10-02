; Instalador de Windows de Historial Académico (Inno Setup 6). Lo arma .github/workflows/release.yml:
;   ISCC.exe /DAppVersion=1.0.0 /DOrigenPrograma=<carpeta publicada> /O<carpeta de salida> installer\HistorialAcademico.iss
; Instala sin permisos de administrador, en la carpeta del usuario, con acceso directo en el menú Inicio (y en el escritorio si la persona quiere)
; y con desinstalador. No toca los datos de la persona: viven en %LOCALAPPDATA%\HistorialAcademico y se borran desde «Mis datos» en la aplicación.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef OrigenPrograma
  #define OrigenPrograma "..\salida\HistorialAcademico"
#endif

#define NombreApp "Historial Académico"
#define Ejecutable "HistorialAcademico.Web.exe"

[Setup]
; Siempre el mismo AppId: una versión nueva se instala encima de la anterior.
AppId={{8E1A5E57-2D0B-4C3E-9C57-5B6F0A1D7C42}
AppName={#NombreApp}
AppVersion={#AppVersion}
AppPublisher=Julian Baez Mena
AppPublisherURL=https://github.com/JulianBaez1229/Historial-Academico
AppSupportURL=https://github.com/JulianBaez1229/Historial-Academico/issues
AppUpdatesURL=https://github.com/JulianBaez1229/Historial-Academico/releases
DefaultDirName={localappdata}\Programs\HistorialAcademico
DefaultGroupName={#NombreApp}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=HistorialAcademico-Instalador-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#Ejecutable}
UninstallDisplayName={#NombreApp}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el &Escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#OrigenPrograma}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#NombreApp}"; Filename: "{app}\{#Ejecutable}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#NombreApp}"; Filename: "{app}\{#Ejecutable}"; WorkingDir: "{app}"; Tasks: escritorio

[Run]
Filename: "{app}\{#Ejecutable}"; WorkingDir: "{app}"; Description: "Abrir {#NombreApp} ahora"; Flags: nowait postinstall skipifsilent
