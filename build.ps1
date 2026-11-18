param(
    [switch]$AppOnly,
    [switch]$Checks,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The system x64 .NET Framework compiler was not found.' }
$bin = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName

$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll', 'Accessibility.dll',
    'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'WPF\WindowsBase.dll'
)
$compile = @('/nologo', '/noconfig', '/platform:x64', '/target:winexe', '/langversion:5', '/codepage:65001', '/optimize+')
foreach ($reference in $references) {
    $dependency = Join-Path $framework $reference
    if (-not (Test-Path -LiteralPath $dependency -PathType Leaf)) { throw "Missing system reference $dependency" }
    $compile += "/reference:$dependency"
}

$source = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -File -Filter '*.cs')
if ($Checks) {
    $checkOptions = @($compile | Where-Object { $_ -ne '/target:winexe' })
    $checkSource = Join-Path $PSScriptRoot 'tests\CoreChecks.cs'
    $checkExe = Join-Path $bin 'CoreChecks.exe'
    & $compiler @checkOptions '/target:exe' '/main:Explorer2Dialog.CoreChecks' "/out:$checkExe" @($source.FullName) $checkSource
    if ($LASTEXITCODE -ne 0) { throw 'CoreChecks compilation failed.' }
    & $checkExe
    if ($LASTEXITCODE -ne 0) { throw 'CoreChecks failed.' }
    return
}

$app = Join-Path $bin 'Explorer2Dialog.exe'
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
& $compiler @compile "/win32manifest:$manifest" "/out:$app" @($source.FullName)
if ($LASTEXITCODE -ne 0) { throw 'Explorer2Dialog compilation failed.' }
$app

if ($AppOnly) { return }

$testApp = Join-Path $bin 'DialogTestApp.exe'
$testSource = Join-Path $PSScriptRoot 'tests\DialogTestApp\Program.cs'
& $compiler @compile "/out:$testApp" $testSource
if ($LASTEXITCODE -ne 0) { throw 'DialogTestApp compilation failed.' }
$testApp
