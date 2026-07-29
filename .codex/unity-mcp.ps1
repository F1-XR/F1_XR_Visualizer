$projectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$relayPath = Join-Path $env:USERPROFILE ".unity\relay\relay_win.exe"

if (-not (Test-Path -LiteralPath $relayPath)) {
    throw "Unity MCP relay was not found at: $relayPath"
}

& $relayPath --mcp --project-path $projectPath
exit $LASTEXITCODE
