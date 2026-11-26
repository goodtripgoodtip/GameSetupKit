param(
    [string] $Output = (Join-Path $PSScriptRoot '..\artifacts\sample'),
    [string] $Bootstrap,
    [string] $RuntimeVersion
)

$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($Output)
if (!$Bootstrap) {
    $project = Join-Path $PSScriptRoot 'GameInstaller.Sample\GameInstaller.Sample.csproj'
    $publish = Join-Path $destination 'bootstrap'
    $publishArgs = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true', '-o', $publish)
    if ($RuntimeVersion) { $publishArgs += "-p:RuntimeFrameworkVersion=$RuntimeVersion" }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'The sample launcher could not be published.' }
    $Bootstrap = Join-Path $publish 'GameInstaller.Sample.exe'
}
$launcher = (Resolve-Path -LiteralPath $Bootstrap).Path
$process = Start-Process -FilePath $launcher -ArgumentList @('--package-sample', ('"' + $launcher + '"'), ('"' + $destination + '"')) `
    -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw 'Sample packaging failed.' }
Get-Item -LiteralPath (Join-Path $destination 'GameSetupKit-Demo-Setup.exe'), (Join-Path $destination 'GameSetupKit-Demo-Setup-Offline.exe'), (Join-Path $destination 'SHA256SUMS.txt')
