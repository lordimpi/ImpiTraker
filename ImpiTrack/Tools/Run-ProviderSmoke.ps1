param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("SqlServer", "Postgres", "Both")]
    [string]$Provider = "Both",

    [Parameter(Mandatory = $false)]
    [string]$SqlServerConnectionString = "Data Source=SANTIAGO;Initial Catalog=ImpiTrakDB;Integrated Security=True;Connect Timeout=30;Encrypt=False;Trust Server Certificate=True;Application Intent=ReadWrite;Multi Subnet Failover=False;",

    [Parameter(Mandatory = $false)]
    [string]$PostgresConnectionString = "Host=localhost;Port=5432;Database=imptrack;Username=postgres;Password=postgres",

    [Parameter(Mandatory = $false)]
    [int]$StartupTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $repoRoot "ImpiTrack.Api/ImpiTrack.Api.csproj"

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

function Wait-Ready {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 3
            return @{
                Success = $true
                StatusCode = [int]$response.StatusCode
                Body = $response.Content
            }
        }
        catch {
            Start-Sleep -Milliseconds 800
        }
    }

    return @{
        Success = $false
        StatusCode = 0
        Body = ""
    }
}

function Invoke-Smoke {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("SqlServer", "Postgres")][string]$DbProvider,
        [Parameter(Mandatory = $true)][string]$ConnectionString
    )

    $apiPort = if ($DbProvider -eq "SqlServer") { 5510 } else { 5511 }
    $apiUrls = "http://127.0.0.1:$apiPort"
    $readyUrl = "$apiUrls/ready"

    $envBackup = @{
        DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
        DOTNET_NOLOGO = $env:DOTNET_NOLOGO
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
        ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
        ASPNETCORE_URLS = $env:ASPNETCORE_URLS
        IdentityStorage__Provider = $env:IdentityStorage__Provider
        IdentityStorage__ConnectionString = $env:IdentityStorage__ConnectionString
        IdentityBootstrap__SeedAdminOnStart = $env:IdentityBootstrap__SeedAdminOnStart
        Database__Provider = $env:Database__Provider
        Database__ConnectionString = $env:Database__ConnectionString
        Database__EnableAutoMigrate = $env:Database__EnableAutoMigrate
    }

    $logDirectory = Join-Path $repoRoot ".artifacts"
    $stdOutLogPath = Join-Path $logDirectory "smoke-$DbProvider.out.log"
    $stdErrLogPath = Join-Path $logDirectory "smoke-$DbProvider.err.log"
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    if (Test-Path $stdOutLogPath) {
        Remove-Item $stdOutLogPath -Force
    }
    if (Test-Path $stdErrLogPath) {
        Remove-Item $stdErrLogPath -Force
    }

    Write-Host ""
    Write-Host "===> Smoke $DbProvider"

    $process = $null
    try {
        $dotnetCliHome = Join-Path $repoRoot ".dotnet"
        New-Item -ItemType Directory -Path $dotnetCliHome -Force | Out-Null
        Set-EnvValue -Name "DOTNET_CLI_HOME" -Value $dotnetCliHome
        Set-EnvValue -Name "DOTNET_SKIP_FIRST_TIME_EXPERIENCE" -Value "1"
        Set-EnvValue -Name "DOTNET_NOLOGO" -Value "1"
        Set-EnvValue -Name "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH" -Value "0"

        Set-EnvValue -Name "ASPNETCORE_ENVIRONMENT" -Value "Development"
        Set-EnvValue -Name "ASPNETCORE_URLS" -Value $apiUrls
        Set-EnvValue -Name "IdentityStorage__Provider" -Value "InMemory"
        Set-EnvValue -Name "IdentityStorage__ConnectionString" -Value ""
        Set-EnvValue -Name "IdentityBootstrap__SeedAdminOnStart" -Value "false"
        Set-EnvValue -Name "Database__Provider" -Value $DbProvider
        Set-EnvValue -Name "Database__ConnectionString" -Value $ConnectionString
        Set-EnvValue -Name "Database__EnableAutoMigrate" -Value "true"

        $process = Start-Process -FilePath "dotnet" `
            -ArgumentList @("run", "--no-build", "--project", $apiProject) `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $stdOutLogPath `
            -RedirectStandardError $stdErrLogPath `
            -PassThru

        $ready = Wait-Ready -Url $readyUrl -TimeoutSeconds $StartupTimeoutSeconds
        if ($ready.Success) {
            Write-Host "[OK] /ready status=$($ready.StatusCode)"
            return @{
                Provider = $DbProvider
                Success = $true
                StatusCode = $ready.StatusCode
                LogPath = "$stdOutLogPath | $stdErrLogPath"
            }
        }

        Write-Host "[FAIL] /ready timeout. Revisa logs: $stdOutLogPath | $stdErrLogPath"
        return @{
            Provider = $DbProvider
            Success = $false
            StatusCode = 0
            LogPath = "$stdOutLogPath | $stdErrLogPath"
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            Start-Sleep -Milliseconds 400
        }

        foreach ($entry in $envBackup.GetEnumerator()) {
            Set-EnvValue -Name $entry.Key -Value $entry.Value
        }
    }
}

$targets = switch ($Provider) {
    "SqlServer" { @("SqlServer") }
    "Postgres" { @("Postgres") }
    default { @("SqlServer", "Postgres") }
}

$results = @()
foreach ($target in $targets) {
    if ($target -eq "SqlServer") {
        $results += Invoke-Smoke -DbProvider "SqlServer" -ConnectionString $SqlServerConnectionString
    }
    else {
        $results += Invoke-Smoke -DbProvider "Postgres" -ConnectionString $PostgresConnectionString
    }
}

Write-Host ""
Write-Host "===> Resumen smoke"
foreach ($result in $results) {
    $status = if ($result.Success) { "PASS" } else { "FAIL" }
    Write-Host "$($result.Provider): $status (readyStatus=$($result.StatusCode)) log=$($result.LogPath)"
}

$hasFail = $results | Where-Object { -not $_.Success }
if ($hasFail) {
    exit 1
}

exit 0
