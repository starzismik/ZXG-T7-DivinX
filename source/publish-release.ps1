$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/build-current.ps1"
New-Item -ItemType Directory -Force "$PSScriptRoot/payload/plugins/divinium", "$PSScriptRoot/payload/notices" | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/build/divinium/Release/ZXG Divinium.dll", "$PSScriptRoot/build/full/Release/ZXG DivinX.dll" -Destination "$PSScriptRoot/payload/plugins"
Copy-Item -LiteralPath "$PSScriptRoot/assets/divinium/artwork.png" -Destination "$PSScriptRoot/payload/plugins/divinium/artwork.png"
Copy-Item -Path "$PSScriptRoot/../notices/*" -Destination "$PSScriptRoot/payload/notices"
& dotnet publish "$PSScriptRoot/desktop/ZXGT7Guard.Desktop.csproj" -c Release -o "$PSScriptRoot/build/release"
if ($LASTEXITCODE) { throw 'Publication failed' }
Move-Item -LiteralPath "$PSScriptRoot/build/release/ZXG T7 DivinX.exe" -Destination "$PSScriptRoot/build/release/[ZXG] T7 DivinX.exe" -Force
