# DepoWise COMODO guard for Claude Code PreToolUse/Bash.
$ErrorActionPreference = 'SilentlyContinue'
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $event = $raw | ConvertFrom-Json
    $command = [string]$event.tool_input.command
    if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

    $segments = [regex]::Split($command, '(?:&&|\|\||;|\|)')
    $blockedReason = $null
    foreach ($segment in $segments) {
        $s = $segment.Trim()
        if (-not $s) { continue }
        $s = $s -replace '^(?i)call\s+', ''
        $s = $s -replace '^(?i)start\s+(?:""\s+)?', ''
        $s = $s -replace '^(?i)cmd(?:\.exe)?\s+/(?:c|k)\s+', ''
        $candidate = $null
        if ($s -match '^"([^"]+)"') { $candidate = $matches[1] }
        elseif ($s -match '^(\S+)') { $candidate = $matches[1] }
        if (-not $candidate) { continue }
        $leaf = [System.IO.Path]::GetFileName($candidate.Trim('"', "'"))
        if ($leaf -match '(?i)\.bat$') {
            $blockedReason = 'COMODO: BAT çalıştırma engellendi. Komutu güvenilir terminalde doğrudan çalıştır.'
            break
        }
        if ($leaf -match '(?i)^DepoWise.*\.exe$') {
            $blockedReason = 'COMODO: proje EXE çalıştırma engellendi. dotnet host ile DepoWise Desktop DLL çalıştır.'
            break
        }
    }
    if ($blockedReason) {
        @{hookSpecificOutput=@{hookEventName='PreToolUse';permissionDecision='deny';permissionDecisionReason=$blockedReason}} | ConvertTo-Json -Depth 5 -Compress
    }
} catch {}
exit 0
