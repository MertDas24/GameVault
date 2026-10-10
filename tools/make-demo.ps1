# Converts the recorded demo (demo.webm from tests/GameVault.E2E/DemoVideo.cs) into:
#   docs/demo.gif  - small looping GIF that plays inline in the README
#   demo.mp4       - H.264 MP4 for LinkedIn (written next to the WebM, not committed)
#
# Usage:
#   $env:GAMEVAULT_DEMO_VIDEO_DIR = "$env:TEMP\gamevault-demo"
#   dotnet test tests/GameVault.E2E -c Release --filter "FullyQualifiedName~DemoVideo"
#   ./tools/make-demo.ps1 -VideoDir $env:GAMEVAULT_DEMO_VIDEO_DIR
#
# Requires ffmpeg on PATH (for example: winget install Gyan.FFmpeg).
param(
    [Parameter(Mandatory = $true)][string]$VideoDir,
    [string]$Ffmpeg = "ffmpeg",
    [int]$GifWidth = 800,
    [int]$GifFps = 8
)

$ErrorActionPreference = "Stop"
$webm = Join-Path $VideoDir "demo.webm"
if (-not (Test-Path $webm)) { throw "Not found: $webm" }

$repo = Split-Path $PSScriptRoot -Parent
$gif = Join-Path $repo "docs/demo.gif"
$mp4 = Join-Path $VideoDir "demo.mp4"
$palette = Join-Path $VideoDir "palette.png"

# MP4: H.264 + yuv420p plays everywhere; faststart lets LinkedIn start playback before the download finishes.
& $Ffmpeg -y -loglevel error -i $webm -c:v libx264 -preset slow -crf 20 -pix_fmt yuv420p -movflags +faststart -an $mp4
if ($LASTEXITCODE -ne 0) { throw "MP4 conversion failed" }

# GIF: two passes with a generated palette keep colors clean at a small file size.
$filters = "fps=$GifFps,scale=${GifWidth}:-1:flags=lanczos"
& $Ffmpeg -y -loglevel error -i $webm -vf "$filters,palettegen=stats_mode=diff" $palette
if ($LASTEXITCODE -ne 0) { throw "Palette generation failed" }
& $Ffmpeg -y -loglevel error -i $webm -i $palette -lavfi "$filters [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" $gif
if ($LASTEXITCODE -ne 0) { throw "GIF conversion failed" }
Remove-Item $palette

"{0,-12} {1,8:N1} MB" -f "demo.gif", ((Get-Item $gif).Length / 1MB)
"{0,-12} {1,8:N1} MB" -f "demo.mp4", ((Get-Item $mp4).Length / 1MB)
