[CmdletBinding()]
param([string]$OutputPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'ZG1-Control.exe'))
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path -LiteralPath $taskCompiler)){$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'}
if(-not (Test-Path -LiteralPath $taskCompiler)){throw '.NET Framework compiler not found.'}
Push-Location $PSScriptRoot
try{
    & $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/out:'+$OutputPath) /win32manifest:app.manifest /win32icon:app.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll Core.cs UI.cs Program.cs Metadata.cs
    if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
}finally{Pop-Location}
