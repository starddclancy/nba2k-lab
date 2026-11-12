param(
    [string]$OutputRoot,
    [int]$FrontendTestConcurrency = 0
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/Nba2k27Editor.WebHost/Nba2k27Editor.WebHost.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if (!$OutputRoot) {
    $OutputRoot = Join-Path $repo 'artifacts'
}
$directory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $version
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$runtime = Join-Path (Split-Path $repo -Parent) 'dotnet8'
if (Test-Path -LiteralPath (Join-Path $runtime 'dotnet.exe')) {
    $env:DOTNET_ROOT = $runtime
    $env:PATH = "$runtime;$env:PATH"
}

Push-Location (Join-Path $repo 'web')
try {
    $testArguments = @('--test')
    if ($FrontendTestConcurrency -gt 0) {
        $testArguments += "--test-concurrency=$FrontendTestConcurrency"
    }
    & node @testArguments tests/*.test.ts *> (Join-Path $directory 'frontend-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed' }
    & node node_modules/typescript/bin/tsc --noEmit *> (Join-Path $directory 'frontend-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frontend type checks failed' }
    & node node_modules/vite/bin/vite.js build *>> (Join-Path $directory 'frontend-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
}
finally {
    Pop-Location
}

Push-Location $repo
try {
    & dotnet run --project tests/Nba2k27Editor.Tests -c Release -- --out (Join-Path $directory 'offline-tests.json') *> (Join-Path $directory 'offline-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'C# checks failed' }
    & dotnet publish src/Nba2k27Editor.WebHost -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -o (Join-Path $directory 'build') *> (Join-Path $directory 'publish.log')
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $exe = Join-Path $directory "Nba2k27Editor-$version.exe"
    Copy-Item -LiteralPath (Join-Path $directory 'build/Nba2k27Editor.exe') -Destination $exe -Force
    $zip = Join-Path $directory "nba2k27-editor-$version-win-x64.zip"
    Compress-Archive -LiteralPath (Join-Path $directory 'build/Nba2k27Editor.exe') -DestinationPath $zip -Force
    foreach ($file in @('README.md', 'FEATURE_PARITY.md', 'CHANGELOG.md', 'DELIVERY.md')) {
        Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $directory -Force
    }
    $checks = Get-Content -LiteralPath (Join-Path $directory 'offline-tests.json') -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        format = 'nba2k27-editor-release/1'
        version = $version
        executable = [IO.Path]::GetFileName($exe)
        executable_sha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
        zip = [IO.Path]::GetFileName($zip)
        zip_sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        csharp_checks_passed = $checks.passed
        frontend_tests = 'passed; see frontend-tests.log'
        status = 'candidate'
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'release.json') -Encoding utf8
    Write-Output "Published $version to $directory"
}
finally {
    Pop-Location
}
