param([string]$CodePath, [string]$Body, [string]$ClassName = 'Probe', [string]$MethodName = 'Run', [int]$Timeout = 30000)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$inputPath = Join-Path $projectRoot 'Library/web-ui-tool-input.json'
if ($CodePath) {
    $payload = @{ csharpCode = Get-Content -LiteralPath $CodePath -Raw -Encoding UTF8; className = $ClassName; methodName = $MethodName }
} else {
    $payload = @{ csharpCode = $Body; isMethodBody = $true }
}
$payload | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $inputPath -Encoding UTF8
& 'C:/Program Files/nodejs/node.exe' (Join-Path $projectRoot 'Library/web-ui-tools/node_modules/unity-mcp-cli/bin/unity-mcp-cli.js') run-tool script-execute --url 'http://localhost:21195/p/3b424d94' --input-file $inputPath --timeout $Timeout --raw
