Role: runbook  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15  
When to use: enable real email delivery from `ImpiTrack.Api` with Gmail SMTP in controlled environments

# SMTP Gmail Setup (ImpiTrack API)

Canonical state note: this is an email setup procedure. For the current backend/runtime truth, use [`../CURRENT_STATE.md`](../CURRENT_STATE.md).

## 1) Initialize project secrets

```powershell
dotnet user-secrets init --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
```

## 2) Load Gmail SMTP configuration

Replace `TU_GMAIL@gmail.com` and `TU_APP_PASSWORD_16_CHARS`.

```powershell
dotnet user-secrets set "Email:Enabled" "true" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:FromName" "ImpiTrack" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:FromEmail" "TU_GMAIL@gmail.com" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:VerifyEmailBaseUrl" "https://localhost:54124/api/auth/verify-email/confirm" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:Smtp:Port" "587" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:Smtp:UseSsl" "true" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:Smtp:UserName" "TU_GMAIL@gmail.com" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet user-secrets set "Email:Smtp:Password" "TU_APP_PASSWORD_16_CHARS" --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
```

## 3) Validate delivery

1. Run the API.
2. Register a user with `POST /api/auth/register`.
3. Check logs for `email_queued` and `email_sent`.
4. Open the received verification link and confirm with `GET /api/auth/verify-email/confirm`.

## 4) Operational notes

- Gmail requires 2FA and an app password.
- Do not store credentials in `appsettings*.json`.
- If delivery fails, inspect `smtp_send_failed` in logs.
