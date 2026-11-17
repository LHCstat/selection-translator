$ErrorActionPreference = 'Stop'

# Shortcut name: 划译
$name = ([char]0x5212).ToString() + ([char]0x8BD1).ToString()

$source = Join-Path $PSScriptRoot '..\publish\framework-dependent\SelectionTranslator.App.exe'
if (-not (Test-Path $source)) {
    throw "Publish output not found: $source. Run dotnet publish first."
}

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\SelectionTranslator'
$target = Join-Path $targetDir 'SelectionTranslator.App.exe'

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -Force $source $target

$shell = New-Object -ComObject WScript.Shell
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$name.lnk")
)

foreach ($path in $shortcuts) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $targetDir
    $shortcut.IconLocation = "$target,0"
    $shortcut.Description = $name
    $shortcut.Save()
}

Write-Host "Installed: $target"
Write-Host "Shortcuts: $($shortcuts -join '; ')"