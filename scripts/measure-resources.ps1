param(
    [int]$TargetProcessId,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts\performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [int[]]$Checkpoints = @(30, 60, 90, 300, 600)
)
# Resolve paths from the checkout rather than the current working directory.
$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ErrorActionPreference = 'Stop'
$Checkpoints = @($Checkpoints | Where-Object { $_ -gt 0 } | Sort-Object -Unique)
if (-not $Checkpoints.Count) { throw 'Provide at least one positive checkpoint.' }
if (-not $TargetProcessId) {
    . (Join-Path $PSScriptRoot 'Get-PublishDirectory.ps1')
    $exe = Join-Path (Get-PublishDirectory -RepositoryRoot $RepositoryRoot) 'GameLibrary.exe'
    $target = @(Get-Process GameLibrary -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    if ($target.Count -ne 1) { throw 'Open Game Library first, or specify -TargetProcessId.' }
    $TargetProcessId = $target[0].Id
}
$process = Get-Process -Id $TargetProcessId
$started = Get-Date
$logicalProcessors = [int]$env:NUMBER_OF_PROCESSORS
if ($logicalProcessors -lt 1) { $logicalProcessors = [Environment]::ProcessorCount }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$samples = [Collections.Generic.List[object]]::new()
$reports = [Collections.Generic.List[string]]::new()
$csv = [IO.StreamWriter]::new((Join-Path $OutputDirectory 'samples.csv'), $false)
$csv.AutoFlush = $true
$csv.WriteLine('Timestamp,ElapsedSeconds,IntervalSeconds,CpuSeconds,CpuPercentMachine,CpuPercentOneCore,WorkingSetMiB,PrivateCommitMiB,Threads,Handles')
$culture = [Globalization.CultureInfo]::InvariantCulture
function Number([double]$value) { $value.ToString('0.000', $culture) }
function Write-Report([double]$end, [double]$previous, [string]$name) {
    $all = @($samples | Where-Object { $_.Elapsed -le $end + 0.1 })
    $window = @($all | Where-Object { $_.Elapsed -gt $previous })
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("## $name")
    $lines.Add('')
    $lines.Add('| Period | CPU average (whole PC) | CPU peak (1 s) | RAM average / peak | Private commit peak |')
    $lines.Add('|---|---:|---:|---:|---:|')
    foreach ($entry in @(@('Since start', $all), @('Since previous checkpoint', $window))) {
        $set = @($entry[1])
        if (-not $set.Count) { continue }
        $duration = ($set | Measure-Object Interval -Sum).Sum
        $cpu = 100 * ($set | Measure-Object CpuSeconds -Sum).Sum / $duration / $logicalProcessors
        $peakCpu = ($set | Measure-Object CpuPercent -Maximum).Maximum
        $ram = $set | Measure-Object Ram -Average -Maximum
        $commit = ($set | Measure-Object Commit -Maximum).Maximum
        $lines.Add(('| {0} | {1:N3}% | {2:N3}% | {3:N1} / {4:N1} MiB | {5:N1} MiB |' -f $entry[0], $cpu, $peakCpu, $ram.Average, $ram.Maximum, $commit))
    }
    $lines.Add('')
    $section = $lines -join [Environment]::NewLine
    $reports.Add($section)
    $header = @(
        '# Game Library resource usage', '',
        "Started: $($started.ToString('o')); PID: $TargetProcessId; logical processors: $logicalProcessors.", '',
        'Sampled approximately every second. CPU percentages are normalized across the whole PC. CSV also includes one-core CPU percentage (100% = one fully occupied core). RAM is resident working set; private commit is allocated private memory, not necessarily physical RAM.', '',
        'Only the selected GameLibrary process is measured. Game, Steam, Wallpaper Engine, GPU usage, and the sampler itself are excluded. This measures app overhead, not game FPS or frame-time impact. Checkpoint times include minor scheduler delay.', ''
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($name + '.md')), $header + $section)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'report.md'), $header + ($reports -join [Environment]::NewLine))
}
$watch = [Diagnostics.Stopwatch]::StartNew()
$lastTime = 0.0
$lastCpu = $process.TotalProcessorTime.TotalSeconds
$checkpointIndex = 0
$previousCheckpoint = 0.0
$nextSample = 1.0
@{ Started = $started.ToString('o'); ProcessId = $TargetProcessId; OutputDirectory = $OutputDirectory; Checkpoints = $Checkpoints } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'started.json')
try {
    while ($checkpointIndex -lt $Checkpoints.Count) {
        $delay = [Math]::Max(1, [int](1000 * ($nextSample - $watch.Elapsed.TotalSeconds)))
        Start-Sleep -Milliseconds $delay
        $process.Refresh()
        if ($process.HasExited) { Write-Report $lastTime $previousCheckpoint 'stopped-early'; break }
        $elapsed = $watch.Elapsed.TotalSeconds
        $cpu = $process.TotalProcessorTime.TotalSeconds
        $interval = $elapsed - $lastTime
        $delta = [Math]::Max(0, $cpu - $lastCpu)
        $oneCore = 100 * $delta / $interval
        $sample = [pscustomobject]@{ Elapsed = $elapsed; Interval = $interval; CpuSeconds = $delta;
            CpuPercent = $oneCore / $logicalProcessors; Ram = $process.WorkingSet64 / 1MB; Commit = $process.PrivateMemorySize64 / 1MB }
        $samples.Add($sample)
        $csv.WriteLine((@((Get-Date).ToString('o'), (Number $elapsed), (Number $interval), (Number $delta),
            (Number $sample.CpuPercent), (Number $oneCore), (Number $sample.Ram), (Number $sample.Commit),
            $process.Threads.Count, $process.HandleCount) -join ','))
        if ($elapsed -ge $Checkpoints[$checkpointIndex]) {
            Write-Report $elapsed $previousCheckpoint ($Checkpoints[$checkpointIndex].ToString() + 's')
            $previousCheckpoint = $elapsed
            $checkpointIndex++
        }
        $lastTime = $elapsed; $lastCpu = $cpu
        $nextSample = [Math]::Max($nextSample + 1, [Math]::Floor($watch.Elapsed.TotalSeconds) + 1)
    }
    "Completed $(Get-Date -Format o)" | Set-Content -LiteralPath (Join-Path $OutputDirectory 'completed.txt')
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $OutputDirectory 'error.txt')
    throw
}
finally { $csv.Dispose(); $process.Dispose() }
