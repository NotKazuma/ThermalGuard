# Builds a self-contained, single-file ThermalGuard.exe (no .NET runtime required on the target machine).
# Output: dist\ThermalGuard.exe
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    Remove-Item dist -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/ThermalGuard -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o dist
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    # Single-file publish still drops a handful of files that must sit beside the exe; keep only those.
    Get-ChildItem dist -File | Where-Object {
        $_.Name -notin @("ThermalGuard.exe", "ThermalGuard.pdb")
    } | Remove-Item -Force

    # PresentMon (bundled for FPS; see tools/PresentMon/README-provenance.txt for its verified source/hash).
    $pm = Get-ChildItem tools/PresentMon -Filter "PresentMon*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pm) { Copy-Item $pm.FullName dist/ -Force }

    $exe = Join-Path (Resolve-Path dist) "ThermalGuard.exe"
    $size = (Get-Item $exe).Length / 1MB
    "Published: $exe  ({0:N1} MB)" -f $size
    if ($pm) { "Bundled:   $($pm.Name)" }
}
finally { Pop-Location }
