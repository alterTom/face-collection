$ErrorActionPreference = 'Stop'
$serverScript = Join-Path $PSScriptRoot 'static-server.mjs'

Write-Host 'Open http://127.0.0.1:18080/demo/ in the browser.'
node $serverScript
