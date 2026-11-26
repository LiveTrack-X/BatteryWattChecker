$ErrorActionPreference = 'Stop'
$runtime = Join-Path $env:WINDIR 'Microsoft.NET'
$compiler = @('Framework64', 'Framework') |
    ForEach-Object { Join-Path $runtime "$_\v4.0.30319\csc.exe" } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework 4.x C# compiler is required.' }

$output = New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'bin')
$executable = Join-Path $output.FullName 'BatteryWattChecker.exe'
$source = Join-Path $PSScriptRoot 'BatteryWattChecker.cs'
$updates = Join-Path $PSScriptRoot 'Updates.cs'
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$executable" $source $updates
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Output $executable
