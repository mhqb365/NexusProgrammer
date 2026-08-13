param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$InputPath
)

$header = 'Device	Manufacturer	RawId	SizeBytes	PageSize	Volts	Protocol	CommandSet	Type	Supported'
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add($header)

foreach ($path in ($InputPath -split ';')) {
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    foreach ($line in [System.IO.File]::ReadLines($path)) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#') -or $line.StartsWith('Device	')) {
            continue
        }

        $fields = $line -split "`t", -1
        if ($fields.Count -lt 10 -or [string]::IsNullOrWhiteSpace($fields[2])) {
            continue
        }

        $rawId = -join ($fields[2].ToCharArray() | Where-Object { $_ -match '[0-9A-Fa-f]' })
        if ([string]::IsNullOrWhiteSpace($rawId)) {
            continue
        }

        $deviceKey = -join ($fields[0].ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
        $key = "$deviceKey|$rawId|$($fields[3])|$($fields[5])|$($fields[8])"
        if (-not $seen.Add($key)) {
            continue
        }

        $lines.Add($line)
    }
}

$directory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

[System.IO.File]::WriteAllLines($OutputPath, $lines, [System.Text.UTF8Encoding]::new($false))
