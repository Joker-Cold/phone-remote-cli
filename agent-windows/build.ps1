param([string]$OutDir = '')
$ErrorActionPreference = 'Stop'
# Builds with the C# compiler that ships with Windows (.NET Framework 4.x); nothing needs to be installed.
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw "csc.exe not found: $compiler" }
if (!$OutDir) { $OutDir = Join-Path $PSScriptRoot 'bin' }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$common = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Security.dll', '/reference:System.Management.dll')
# RemoteCli.exe: the window, with the terminal agent inside. app.ico is the file's icon and, inside, the windows' and the tray's.
$icon = Join-Path $PSScriptRoot 'app.ico'
& $compiler @common "/out:$(Join-Path $OutDir 'RemoteCli.exe')" "/win32icon:$icon" "/resource:$icon,app.ico" /main:RemoteCli.App /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'RemoteCliApp.cs') (Join-Path $PSScriptRoot 'Controls.cs') (Join-Path $PSScriptRoot 'RemoteCliAgent.cs') (Join-Path $PSScriptRoot 'QrCode.cs') (Join-Path $PSScriptRoot 'CodexSessions.cs') (Join-Path $PSScriptRoot 'Tailnet.cs') (Join-Path $PSScriptRoot 'Remote.cs')
if ($LASTEXITCODE -ne 0) { throw 'RemoteCli.exe: compilation failed' }
# RemoteCliAgent.exe: the agent alone, without a window, reporting to a relay named in its settings (used by the tests).
& $compiler @common "/out:$(Join-Path $OutDir 'RemoteCliAgent.exe')" (Join-Path $PSScriptRoot 'RemoteCliAgent.cs') (Join-Path $PSScriptRoot 'CodexSessions.cs')
if ($LASTEXITCODE -ne 0) { throw 'RemoteCliAgent.exe: compilation failed' }
Get-ChildItem -LiteralPath $OutDir -Filter '*.exe' | ForEach-Object { '{0}  {1} bytes' -f $_.Name, $_.Length }
