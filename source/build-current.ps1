param()
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$cmake='C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
function Run-CMake([string[]]$Arguments) {
 $si=[Diagnostics.ProcessStartInfo]::new();$si.FileName=$cmake;$si.UseShellExecute=$false;$si.WorkingDirectory=$root;$si.Environment.Clear()
 foreach($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()){$si.Environment[$entry.Key.ToUpperInvariant()]=$entry.Value}
 foreach($a in $Arguments){$si.ArgumentList.Add($a)}
 $p=[Diagnostics.Process]::Start($si);$p.WaitForExit();if($p.ExitCode){throw 'CMake failed'}
}
Run-CMake @('-S','unified','-B','build/divinium','-G','Visual Studio 17 2022','-A','x64','-DDIVINIUM_MODULE=ON')
Run-CMake @('--build','build/divinium','--config','Release','--target','divinx','security_hook_checks','divinium_target_checks','--parallel','4')
Run-CMake @('-S','unified','-B','build/full','-G','Visual Studio 17 2022','-A','x64','-DDIVINIUM_MODULE=OFF')
Run-CMake @('--build','build/full','--config','Release','--target','divinx','--parallel','4')
& "$root/build/divinium/Release/security_hook_checks.exe";if($LASTEXITCODE){throw 'Hook checks failed'}
& "$root/build/divinium/Release/divinium_target_checks.exe";if($LASTEXITCODE){throw 'Target checks failed'}
$p=Join-Path $root 'desktop/GuardServices.cs';$s=[IO.File]::ReadAllText($p)
$dh=(Get-FileHash -LiteralPath "$root/build/divinium/Release/ZXG Divinium.dll").Hash
$qh=(Get-FileHash -LiteralPath "$root/build/full/Release/ZXG DivinX.dll").Hash
$s=[regex]::Replace($s,'DiviniumHash = "[A-F0-9]+"',('DiviniumHash = "'+$dh+'"'))
$s=[regex]::Replace($s,'QolHash = "[A-F0-9]+"',('QolHash = "'+$qh+'"'))
[IO.File]::WriteAllText($p,$s)
& dotnet build "$root/desktop/ZXGT7Guard.Desktop.csproj" -c Release --output "$root/build/package"
if($LASTEXITCODE){throw 'Launcher build failed'}
New-Item -ItemType Directory -Force "$root/build/package/plugins/divinium" | Out-Null
Copy-Item -LiteralPath "$root/build/divinium/Release/ZXG Divinium.dll","$root/build/full/Release/ZXG DivinX.dll" -Destination "$root/build/package/plugins"
Copy-Item -LiteralPath "$root/assets/divinium/artwork.png" -Destination "$root/build/package/plugins/divinium/artwork.png"
Write-Output 'Rebuilt package: build/package. Preserve the original distribution notices when distributing.'
