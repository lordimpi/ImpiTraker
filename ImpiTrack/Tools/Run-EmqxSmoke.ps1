param(
    [Parameter(Mandatory = $false)]
    [string]$TcpHost = "127.0.0.1",

    [Parameter(Mandatory = $false)]
    [int]$TcpPort = 5001,

    [Parameter(Mandatory = $false)]
    [int]$EmqxPort = 1883,

    [Parameter(Mandatory = $false)]
    [int]$StartupTimeoutSeconds = 60,

    [Parameter(Mandatory = $false)]
    [int]$TopicTimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$tcpProject = Join-Path $repoRoot "TcpServer/TcpServer.csproj"
$sendPayloadScript = Join-Path $repoRoot "Tools/Send-TcpPayload.ps1"
$artifactsDirectory = Join-Path $repoRoot ".artifacts"
New-Item -ItemType Directory -Force -Path $artifactsDirectory | Out-Null

$stdoutPath = Join-Path $artifactsDirectory "smoke-emqx-worker.out.log"
$stderrPath = Join-Path $artifactsDirectory "smoke-emqx-worker.err.log"
if (Test-Path $stdoutPath) { Remove-Item $stdoutPath -Force }
if (Test-Path $stderrPath) { Remove-Item $stderrPath -Force }

function Set-EnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowNull()][AllowEmptyString()][string]$Value
    )

    if ($null -eq $Value) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
        return
    }

    Set-Item "Env:$Name" $Value
}

function Wait-TcpListener {
    param(
        [Parameter(Mandatory = $true)][string]$Host,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $client = $null
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $client.Connect($Host, $Port)
            return $true
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
        finally {
            if ($null -ne $client) {
                $client.Dispose()
            }
        }
    }

    return $false
}

function Start-TopicSubscriberJob {
    param(
        [Parameter(Mandatory = $true)][string]$Topic,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    return Start-Job -ScriptBlock {
        param($TopicArg, $PortArg, $TimeoutArg)
        docker run --rm eclipse-mosquitto:2 sh -lc "mosquitto_sub -h host.docker.internal -p $PortArg -t '$TopicArg' -C 1 -W $TimeoutArg -v"
    } -ArgumentList $Topic, $Port, $TimeoutSeconds
}

function Receive-TopicMessage {
    param(
        [Parameter(Mandatory = $true)][System.Management.Automation.Job]$Job,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$Topic
    )

    $completed = Wait-Job -Job $Job -Timeout ($TimeoutSeconds + 8)
    if ($null -eq $completed) {
        Stop-Job -Job $Job -ErrorAction SilentlyContinue
        Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
        throw "smoke_topic_timeout topic=$Topic"
    }

    $message = (Receive-Job -Job $Job -ErrorAction SilentlyContinue) -join "`n"
    Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue

    if ([string]::IsNullOrWhiteSpace($message)) {
        throw "smoke_topic_empty topic=$Topic"
    }

    return $message
}

$dockerContainer = docker ps --filter "name=emqx-local" --format "{{.Names}}"
if (-not ($dockerContainer -contains "emqx-local")) {
    throw "smoke_emqx_container_missing expected=emqx-local action='docker run -d --name emqx-local -p 1883:1883 -p 18083:18083 emqx/emqx:latest'"
}

$envBackup = @{
    DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    DOTNET_NOLOGO = $env:DOTNET_NOLOGO
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
    Database__Provider = $env:Database__Provider
    Database__EnableAutoMigrate = $env:Database__EnableAutoMigrate
    EventBus__Provider = $env:EventBus__Provider
    EventBus__Host = $env:EventBus__Host
    EventBus__Port = $env:EventBus__Port
    EventBus__ClientId = $env:EventBus__ClientId
    EventBus__EnableDlq = $env:EventBus__EnableDlq
    EventBus__MaxPublishRetries = $env:EventBus__MaxPublishRetries
    EventBus__EnablePublishFailureSimulation = $env:EventBus__EnablePublishFailureSimulation
    EventBus__SimulateFailureEventType = $env:EventBus__SimulateFailureEventType
    EventBus__SimulateFailureOnce = $env:EventBus__SimulateFailureOnce
}

$workerProcess = $null
try {
    $dotnetCliHome = Join-Path $repoRoot ".dotnet"
    New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null

    Set-EnvValue -Name "DOTNET_CLI_HOME" -Value $dotnetCliHome
    Set-EnvValue -Name "DOTNET_SKIP_FIRST_TIME_EXPERIENCE" -Value "1"
    Set-EnvValue -Name "DOTNET_NOLOGO" -Value "1"
    Set-EnvValue -Name "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH" -Value "0"
    Set-EnvValue -Name "ASPNETCORE_ENVIRONMENT" -Value "Development"
    Set-EnvValue -Name "Database__Provider" -Value "InMemory"
    Set-EnvValue -Name "Database__EnableAutoMigrate" -Value "false"
    Set-EnvValue -Name "EventBus__Provider" -Value "Emqx"
    Set-EnvValue -Name "EventBus__Host" -Value "127.0.0.1"
    Set-EnvValue -Name "EventBus__Port" -Value $EmqxPort.ToString()
    Set-EnvValue -Name "EventBus__ClientId" -Value "imptrack-worker-smoke"
    Set-EnvValue -Name "EventBus__EnableDlq" -Value "true"
    Set-EnvValue -Name "EventBus__MaxPublishRetries" -Value "0"
    Set-EnvValue -Name "EventBus__EnablePublishFailureSimulation" -Value "true"
    Set-EnvValue -Name "EventBus__SimulateFailureEventType" -Value "telemetry_v1"
    Set-EnvValue -Name "EventBus__SimulateFailureOnce" -Value "true"

    $workerProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--no-launch-profile", "--project", $tcpProject) `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    if (-not (Wait-TcpListener -Host $TcpHost -Port $TcpPort -TimeoutSeconds $StartupTimeoutSeconds)) {
        throw "smoke_tcp_start_timeout host=$TcpHost port=$TcpPort"
    }

    Write-Host "Smoke EMQX: listener TCP listo en $TcpHost:$TcpPort"

    $dlqJob = Start-TopicSubscriberJob -Topic "v1/dlq/#" -Port $EmqxPort -TimeoutSeconds $TopicTimeoutSeconds
    & $sendPayloadScript -ServerHost $TcpHost -Port $TcpPort -Payload "##,imei:359586015829802,A;" | Out-Null
    $dlqMessage = Receive-TopicMessage -Job $dlqJob -TimeoutSeconds $TopicTimeoutSeconds -Topic "v1/dlq/#"
    Write-Host "[OK] DLQ detectado: $dlqMessage"

    $telemetryJob = Start-TopicSubscriberJob -Topic "v1/telemetry/+" -Port $EmqxPort -TimeoutSeconds $TopicTimeoutSeconds
    $statusJob = Start-TopicSubscriberJob -Topic "v1/status/+" -Port $EmqxPort -TimeoutSeconds $TopicTimeoutSeconds
    & $sendPayloadScript -ServerHost $TcpHost -Port $TcpPort -Payload "imei:359586015829802,tracker,250301123045,,A;" | Out-Null

    $telemetryMessage = Receive-TopicMessage -Job $telemetryJob -TimeoutSeconds $TopicTimeoutSeconds -Topic "v1/telemetry/+"
    $statusMessage = Receive-TopicMessage -Job $statusJob -TimeoutSeconds $TopicTimeoutSeconds -Topic "v1/status/+"

    Write-Host "[OK] Telemetry detectado: $telemetryMessage"
    Write-Host "[OK] Status detectado: $statusMessage"
    Write-Host "[OK] Smoke EMQX completado. Logs: $stdoutPath | $stderrPath"
}
catch {
    Write-Host "[FAIL] $($_.Exception.Message)"
    Write-Host "Revisa logs: $stdoutPath | $stderrPath"
    exit 1
}
finally {
    if ($workerProcess -and -not $workerProcess.HasExited) {
        Stop-Process -Id $workerProcess.Id -Force
        Start-Sleep -Milliseconds 300
    }

    foreach ($entry in $envBackup.GetEnumerator()) {
        Set-EnvValue -Name $entry.Key -Value $entry.Value
    }
}

exit 0
