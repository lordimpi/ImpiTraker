# ================================================================
# Seed test users via POST /api/auth/register + verify-email
# Usage: .\seed-users.ps1 -BaseUrl "http://localhost:5000"
# ================================================================

param(
    [string]$BaseUrl     = "http://localhost:5000",
    [int]   $Count       = 100,
    [string]$Password    = "Test@1234!",
    [string]$EmailDomain = "test.impitrack.local"
)

$results = @{ Created = 0; Skipped = 0; Failed = 0 }

for ($i = 1; $i -le $Count; $i++) {
    $index    = $i.ToString("D3")
    $userName = "testuser$index"
    $email    = "testuser$index@$EmailDomain"

    $body = @{
        userName = $userName
        email    = $email
        password = $Password
    } | ConvertTo-Json

    try {
        # 1. Register
        $regResponse = Invoke-WebRequest `
            -Uri         "$BaseUrl/api/auth/register" `
            -Method      POST `
            -Body        $body `
            -ContentType "application/json" `
            -ErrorAction Stop

        $regJson = $regResponse.Content | ConvertFrom-Json
        $token   = $regJson.data.registration.emailVerificationToken

        # 2. Verify email (token viene en el response en modo Development)
        if ($token) {
            $verifyBody = @{ email = $email; token = $token } | ConvertTo-Json
            Invoke-WebRequest `
                -Uri         "$BaseUrl/api/auth/verify-email" `
                -Method      POST `
                -Body        $verifyBody `
                -ContentType "application/json" `
                -ErrorAction SilentlyContinue | Out-Null
        }

        $results.Created++
        Write-Host "[$i/$Count] OK $email" -ForegroundColor Green
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__

        if ($status -eq 409) {
            $results.Skipped++
            Write-Host "[$i/$Count] SKIP $email (ya existe)" -ForegroundColor Yellow
        }
        else {
            $results.Failed++
            Write-Host "[$i/$Count] FAIL $email -- HTTP $status" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "==============================" -ForegroundColor Cyan
Write-Host " Creados : $($results.Created)"  -ForegroundColor Green
Write-Host " Skipped : $($results.Skipped)"  -ForegroundColor Yellow
Write-Host " Fallidos: $($results.Failed)"   -ForegroundColor Red
Write-Host "==============================" -ForegroundColor Cyan
