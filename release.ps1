<#
.SYNOPSIS
  Builds SprunkMapper and packages it as a Velopack release. -Publish also uploads it to GitHub Releases.

.DESCRIPTION
  The version comes from <Version> in CodeWalker\CodeWalker.csproj: bump it (3 parts, e.g. 1.0.1 or
  1.1.0-beta.1) and commit before releasing. Never go below 1.0.0 - see the comment in the csproj.

  Output goes to .\Releases (gitignored). Give your friends Releases\SprunkMapper-win-Setup.exe once;
  after that the app updates itself from GitHub on startup.

  -Publish needs a GitHub token that can write to the repo, in $env:GITHUB_TOKEN
  (a fine-grained token with "Contents: Read and write" on SprunkMapper is enough).
  Releases are published as normal (not "pre-release") releases: the app ignores pre-releases.

.EXAMPLE
  .\release.ps1             # build and package only; check Releases\ before publishing
  .\release.ps1 -Publish    # build, package, create the GitHub release and upload
#>
param(
    [switch]$Publish,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/BryanFrancotte/SprunkMapper'
$root    = $PSScriptRoot
$csproj  = Join-Path $root 'CodeWalker\CodeWalker.csproj'
$outDir  = Join-Path $root 'publish\SprunkMapper'
$relDir  = Join-Path $root 'Releases'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe'

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

# --- version --------------------------------------------------------------------------------------
[xml]$proj = Get-Content $csproj
$versionNode = $proj.SelectSingleNode('//Version')
if ($null -eq $versionNode) { throw "No <Version> in $csproj" }
$version = $versionNode.InnerText.Trim()
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$version' is not 3-part SemVer (e.g. 1.0.1 or 1.1.0-beta.1). Velopack rejects anything else."
}
$assemblyVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
Write-Host "SprunkMapper $version (assembly $assemblyVersion)"

# a release should be exactly a commit, so it can be rebuilt later
if (-not $AllowDirty) {
    $dirty = git -C $root status --porcelain
    if ($dirty) { throw "Uncommitted changes - commit first (or pass -AllowDirty for a test package):`n$dirty" }
}

$vpk = (Get-Command vpk -ErrorAction SilentlyContinue).Source
if (-not $vpk) { $vpk = Join-Path $env:USERPROFILE '.dotnet\tools\vpk.exe' }
if (-not (Test-Path $vpk)) { throw 'vpk not found. Install it with: dotnet tool install -g vpk --version 1.2.158' }

# --- clean build ----------------------------------------------------------------------------------
# a leftover intermediate app.config once got copied into a fresh output folder instead of the
# regenerated one, so always start from a clean obj\Release and an empty output folder
Step 'Clean build'
foreach ($d in @($outDir, (Join-Path $root 'CodeWalker\obj\Release'))) {
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
}
& $msbuild $csproj -restore -p:Configuration=Release "-p:OutDir=$outDir" -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# --- check the output before packaging it -------------------------------------------------------
Step 'Checking build output'
$exe = Join-Path $outDir 'SprunkMapper.exe'
$builtVersion = [Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString()
if ($builtVersion -ne $assemblyVersion) { throw "SprunkMapper.exe is version $builtVersion, expected $assemblyVersion" }

$shaders = @(Get-ChildItem (Join-Path $outDir 'Shaders') -Filter *.cso)
if ($shaders.Count -ne 90) { throw "Expected 90 shaders in the output, found $($shaders.Count)" }
foreach ($s in $shaders) {
    # Debug-compiled shaders carry an SPDB (debug info) chunk; dad93fa once committed those by accident
    $b = [IO.File]::ReadAllBytes($s.FullName)
    $count = [BitConverter]::ToUInt32($b, 28)
    for ($i = 0; $i -lt $count; $i++) {
        $off = [BitConverter]::ToUInt32($b, 32 + 4 * $i)
        if ([Text.Encoding]::ASCII.GetString($b, $off, 4) -eq 'SPDB') {
            throw "$($s.Name) is a Debug build of the shader (has an SPDB chunk). Restore the optimized shaders."
        }
    }
}
if (-not (Test-Path (Join-Path $outDir 'icons'))) { throw 'icons\ missing from the output' }
if (-not (Select-String -Path "$exe.config" -Pattern 'UpgradeRequired' -Quiet)) {
    throw 'SprunkMapper.exe.config is stale (no UpgradeRequired setting)'
}
Write-Host "OK: version $builtVersion, $($shaders.Count) optimized shaders, icons, current config"

# --- package ----------------------------------------------------------------------------------------
Step 'Fetching the previous release (for delta updates)'
New-Item -ItemType Directory -Force $relDir | Out-Null
& $vpk download github --repoUrl $repoUrl --outputDir $relDir
if ($LASTEXITCODE -ne 0) { Write-Host 'No previous Velopack release found - this one will be full-only.' -ForegroundColor Yellow }

Step 'Packaging'
# --runtime: the Release build is x64 (PlatformTarget in the csproj); vpk would otherwise assume x86
& $vpk pack --packId SprunkMapper --packVersion $version --packDir $outDir --mainExe SprunkMapper.exe --runtime win-x64 `
    --packTitle SprunkMapper --packAuthors BryanFrancotte --icon (Join-Path $root 'CodeWalker\CW.ico') `
    --outputDir $relDir
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }

if (-not $Publish) {
    Step "Done. Packaged in $relDir - nothing was uploaded. Run again with -Publish to release it."
    return
}

# --- publish ----------------------------------------------------------------------------------------
Step "Publishing v$version to GitHub"
if (-not $env:GITHUB_TOKEN) { throw 'Set $env:GITHUB_TOKEN to a GitHub token with write access to the repo first.' }
& $vpk upload github --repoUrl $repoUrl --outputDir $relDir --token $env:GITHUB_TOKEN `
    --publish --releaseName "SprunkMapper $version" --tag "v$version"
if ($LASTEXITCODE -ne 0) { throw 'Upload failed' }
Step "Published: $repoUrl/releases/tag/v$version"
