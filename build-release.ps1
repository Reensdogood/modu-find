param(
    [string]$Version,
    [string]$OutputDirectory,
    [string]$InnoCompiler
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
if(-not $Version){[xml]$versionProps=Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props');$Version=[string]$versionProps.Project.PropertyGroup.Version}
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must be a stable three-part version.'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'artifacts'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$publish=Join-Path $OutputDirectory 'app'
$release=Join-Path $OutputDirectory 'release'
New-Item -ItemType Directory -Path $publish,$release -Force | Out-Null
if(-not $InnoCompiler){
    $candidates=@("${env:ProgramFiles}\Inno Setup 7\ISCC.exe","${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","${env:ProgramFiles}\Inno Setup 6\ISCC.exe")
    $InnoCompiler=$candidates | Where-Object {Test-Path -LiteralPath $_} | Select-Object -First 1
}
if(-not $InnoCompiler){throw 'Inno Setup compiler not found. Install Inno Setup 6 or 7, or provide -InnoCompiler.'}
& dotnet run --project (Join-Path $root 'DocumentFinder.Tests') -c Release
if($LASTEXITCODE -ne 0){throw 'Core/update tests failed.'}
& dotnet publish (Join-Path $root 'DocumentFinder.App') -c Release -r win-x64 -o $publish --nologo "-p:Version=$Version" '-p:DebugType=None' '-p:DebugSymbols=false'
if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
& python (Join-Path $root 'tools\prepare_filee.py') --publish-dir $publish
if($LASTEXITCODE -ne 0){throw 'Filee runtime preparation failed.'}
& python (Join-Path $root 'tools\prepare_kordoc.py') --publish-dir $publish
if($LASTEXITCODE -ne 0){throw 'kordoc runtime preparation failed.'}
& dotnet run --project (Join-Path $root 'DocumentFinder.Research.Tests') -c Release -- (Join-Path $publish 'parsing\kordoc')
if($LASTEXITCODE -ne 0){throw 'Research/parser integration checks failed.'}
& dotnet run --project (Join-Path $root 'DocumentFinder.Conversion.Tests') -c Release -- (Join-Path $publish 'conversion\filee-2.4.1')
if($LASTEXITCODE -ne 0){throw 'Document conversion integration checks failed.'}
& python (Join-Path $root 'verify_runtime.py') (Join-Path $publish 'ModuFind.exe')
if($LASTEXITCODE -ne 0){throw 'Worker integration checks failed.'}
& python (Join-Path $root 'verify_index.py') (Join-Path $publish 'ModuFind.exe')
if($LASTEXITCODE -ne 0){throw 'Index integration checks failed.'}
& python (Join-Path $root 'package_release.py') --publish-dir $publish --release-dir $release --version $Version --no-zip
if($LASTEXITCODE -ne 0){throw 'License/package preparation failed.'}
& $InnoCompiler '/Q' "/DAppVersion=$Version" "/DPublishDir=$publish" "/DReleaseDir=$release" (Join-Path $root 'installer\modufind.iss')
if($LASTEXITCODE -ne 0){throw 'Installer build failed.'}
$archive=Join-Path $release "ModuFind-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
$hashes=Get-ChildItem -LiteralPath $release -File | Where-Object {$_.Name -eq "ModuFind-Setup-$Version-win-x64.exe" -or $_.Name -eq "ModuFind-$Version-win-x64-portable.zip"} | ForEach-Object {
    $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $hash.Hash.ToLowerInvariant()+'  '+$_.Name
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),[string[]]$hashes,[Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
