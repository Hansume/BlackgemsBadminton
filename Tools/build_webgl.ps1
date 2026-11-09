# Headless WebGL build. Close the Unity Editor for this project first
# (batch mode can't open a project that's already open).
#
#   powershell -ExecutionPolicy Bypass -File Tools\build_webgl.ps1 [-Development] [-Serve]
param(
    [switch]$Development,
    [switch]$Serve,
    [string]$UnityVersion = "6000.3.27f1"
)

$ProjectPath = Resolve-Path (Join-Path $PSScriptRoot "..")
$Unity = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
if (-not (Test-Path $Unity)) { throw "Unity $UnityVersion not found at $Unity" }

$LogFile = Join-Path $ProjectPath "Logs\webgl-build.log"
$unityArgs = @("-batchmode", "-quit", "-projectPath", "$ProjectPath",
               "-executeMethod", "WebBuild.BuildFromCommandLine",
               "-buildPath", "Builds/WebGL", "-logFile", "$LogFile")
if ($Development) { $unityArgs += "-development" }

Write-Host "Building WebGL... (log: $LogFile)"
$proc = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
if ($proc.ExitCode -ne 0) {
    Get-Content $LogFile -Tail 40
    throw "WebGL build failed (exit $($proc.ExitCode))."
}
Write-Host "Build complete: $ProjectPath\Builds\WebGL"

if ($Serve) { python (Join-Path $PSScriptRoot "serve_webgl.py") }
