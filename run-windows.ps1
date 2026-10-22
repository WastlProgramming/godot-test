# Startet das Spiel unter Windows (Forward+ / Vulkan auf der Grafikkarte).
# Baut vorher das C#-Projekt und lädt Godot 4.6.3 .NET bei Bedarf herunter.
# Liegt das Projekt in WSL (\\wsl.localhost\...), wird es über einen Laufwerksbuchstaben eingebunden,
# weil Godot die Projekt-Assembly nicht von einem UNC-Pfad lädt.
#
#   .\run-windows.ps1                                      # Hauptmenü
#   .\run-windows.ps1 res://scenes/Main.tscn               # direkt ins Spiel
#   .\run-windows.ps1 -GameArgs '--character=knight','--quality=low'
#   .\run-windows.ps1 -Editor                              # Godot-Editor öffnen
#   .\run-windows.ps1 -Console                             # mit Log-Ausgabe im Terminal
param(
    [string]$Scene,
    [string[]]$GameArgs,    # Schalter fürs Spiel, landen hinter "--"
    [switch]$Editor,
    [switch]$Console,
    [switch]$NoBuild,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$GodotArgs
)
$ErrorActionPreference = 'Stop'

$GodotVersion = '4.6.3-stable'
$GodotName = "Godot_v${GodotVersion}_mono_win64"
$GodotUrl = "https://github.com/godotengine/godot/releases/download/$GodotVersion/$GodotName.zip"

# --- Projektpfad (UNC -> Laufwerksbuchstabe) ---
$project = $PSScriptRoot
if ($project.StartsWith('\\')) {
    $parts = $project.TrimStart('\').Split('\')
    $share = '\\' + $parts[0] + '\' + $parts[1]
    $rest = ($parts | Select-Object -Skip 2) -join '\'
    $drive = Get-PSDrive -PSProvider FileSystem | Where-Object { $_.DisplayRoot -eq $share } | Select-Object -First 1
    if ($drive) {
        $letter = $drive.Name
    } else {
        $used = (Get-PSDrive -PSProvider FileSystem).Name
        $letter = [char[]]'WVUTSRQPONMLKJIHGFE' | Where-Object { $used -notcontains [string]$_ } | Select-Object -First 1
        if (-not $letter) { throw 'Kein freier Laufwerksbuchstabe, um das WSL-Projekt einzubinden.' }
        net use "${letter}:" $share /persistent:no | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Konnte $share nicht als ${letter}: einbinden." }
        Write-Host "WSL-Projekt eingebunden als ${letter}:"
    }
    $project = "${letter}:\$rest"
}

# --- Godot suchen bzw. herunterladen ---
$candidates = @(
    $env:GODOT,
    (Join-Path $project ".tools\godot\$GodotName\$GodotName.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Godot\$GodotName\$GodotName.exe")
) | Where-Object { $_ }
$godot = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $godot) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Godot'
    Write-Host "Godot $GodotVersion .NET nicht gefunden, lade nach $target ..."
    New-Item -ItemType Directory -Force $target | Out-Null
    $zip = Join-Path $target "$GodotName.zip"
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $GodotUrl -OutFile $zip
    Expand-Archive $zip -DestinationPath $target -Force
    Remove-Item $zip
    $godot = Join-Path $target "$GodotName\$GodotName.exe"
}

# --- C# bauen (Godot findet sonst die Skriptklassen nicht) ---
if (-not $NoBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK fehlt. Installieren z. B. mit: winget install Microsoft.DotNet.SDK.8'
    }
    Write-Host 'Baue C#-Projekt ...'
    dotnet build (Join-Path $project 'JumpRun.csproj') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
}

# --- Starten ---
$argList = @('--path', $project)
if ($Editor) { $argList += '-e' } else { $argList += '--maximized' }
if ($Scene) { $argList += $Scene }
if ($GodotArgs) { $argList += $GodotArgs }
# Über -File (z. B. aus run-windows.cmd) kommt "a,b" als ein String an.
if ($GameArgs) { $argList += '--'; $argList += $GameArgs | ForEach-Object { $_ -split ',' } | Where-Object { $_ } }

if ($Console) {
    & ($godot -replace '\.exe$', '_console.exe') @argList
} else {
    # Start-Process braucht bei Leerzeichen eigene Anführungszeichen.
    $quoted = $argList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process $godot -ArgumentList $quoted -PassThru
    Write-Host "JumpRun gestartet (PID $($p.Id))."
}
