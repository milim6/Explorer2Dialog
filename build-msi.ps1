param(
    [string]$WixBin,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
if ($WixBin) {
    $candle = Join-Path $WixBin 'candle.exe'
    $light = Join-Path $WixBin 'light.exe'
} else {
    $candle = (Get-Command candle.exe -ErrorAction SilentlyContinue).Source
    $light = (Get-Command light.exe -ErrorAction SilentlyContinue).Source
}
if (-not $candle -or -not $light -or
    -not (Test-Path -LiteralPath $candle -PathType Leaf) -or -not (Test-Path -LiteralPath $light -PathType Leaf)) {
    throw 'Set -WixBin to the folder containing candle.exe and light.exe from WiX Toolset 3.'
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\app.manifest') -Raw -Encoding UTF8
$version = ([version]$manifest.assembly.assemblyIdentity.version).ToString(3)
$dist = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$work = Join-Path $dist 'msi-build'
$app = Join-Path $work 'Explorer2Dialog.exe'
$object = Join-Path $work 'Explorer2Dialog.wixobj'
$package = Join-Path $dist "Explorer2Dialog-$version-x64.msi"
$source = Join-Path $PSScriptRoot 'installer\Explorer2Dialog.wxs'
$license = Join-Path $PSScriptRoot 'LICENSE'

& (Join-Path $PSScriptRoot 'build.ps1') -AppOnly -OutputDirectory $work
& $candle -nologo -arch x64 "-dProductVersion=$version" "-dAppExe=$app" "-dLicenseFile=$license" -out $object $source
if ($LASTEXITCODE -ne 0) { throw 'WiX compilation failed.' }
& $light -nologo -out $package $object
if ($LASTEXITCODE -ne 0) { throw 'WiX linking or MSI validation failed.' }

$package
