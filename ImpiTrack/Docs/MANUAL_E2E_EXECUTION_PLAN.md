# Manual E2E Execution Plan (Backend)

Este runbook ejecuta una prueba E2E manual completa para IMPITrack en local:
- API (Auth + Me + Ops)
- Worker TCP (Coban + Cantrack)
- SQL Server
- EMQX opcional

## 1) Alcance y criterio de exito

La prueba se considera exitosa cuando:
1. API responde `/ready` en `200`.
2. Usuario se registra, verifica correo y hace login.
3. Usuario vincula IMEI `359586015829802`.
4. TCP recibe payloads y responde ACK correcto.
5. Ops muestra `sessionId`, `packetId`, `ackSent` y trazabilidad del IMEI.

## 2) Prerrequisitos

- .NET 10 instalado.
- SQL Server local disponible.
- Configuracion Development en:
  - `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`
  - `ImpiTrack/TcpServer/appsettings.Development.json`
- Recomendado para esta prueba:
  - `Database:Provider = SqlServer`
  - `IdentityStorage:Provider = SqlServer`
  - `EventBus:Provider = InMemory` (primera pasada)

Nota: Identity sobre PostgreSQL queda fuera de este E2E.

## 3) Validacion base (antes de arrancar)

```powershell
dotnet restore ImpiTrack/ImpiTrack.sln
dotnet build ImpiTrack/ImpiTrack.sln -c Debug
dotnet test ImpiTrack/ImpiTrack.sln -c Debug --no-build
```

## 4) Arranque de servicios

Terminal A:
```powershell
dotnet run --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
```

Terminal B:
```powershell
dotnet run --project ImpiTrack/TcpServer/TcpServer.csproj
```

Health check:
```powershell
Invoke-RestMethod -Method Get -Uri "http://localhost:54125/ready"
```

## 5) Flujo API completo (PowerShell copy/paste)

Usa una tercera terminal (Terminal C):

```powershell
$baseUrl = "http://localhost:54125"

# 1) Register
$registerBody = @{
  userName = "user.e2e"
  email    = "user.e2e@imptrack.local"
  password = "ChangeMe!123"
  fullName = "Usuario E2E"
}
$register = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/register" -ContentType "application/json" -Body ($registerBody | ConvertTo-Json)

$userId = $register.data.registration.userId
$emailToken = $register.data.registration.emailVerificationToken

# 2) Verify email
$verifyBody = @{ userId = $userId; token = $emailToken }
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/verify-email" -ContentType "application/json" -Body ($verifyBody | ConvertTo-Json)

# 3) Login user
$loginUserBody = @{ userNameOrEmail = "user.e2e"; password = "ChangeMe!123" }
$loginUser = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" -ContentType "application/json" -Body ($loginUserBody | ConvertTo-Json)
$userToken = $loginUser.data.accessToken
$userHeaders = @{ Authorization = "Bearer $userToken" }

# 4) Bind device to user
$bindBody = @{ imei = "359586015829802" }
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/me/devices" -Headers $userHeaders -ContentType "application/json" -Body ($bindBody | ConvertTo-Json)

# 5) Login admin for Ops
$loginAdminBody = @{ userNameOrEmail = "admin"; password = "ChangeMe!123" }
$loginAdmin = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" -ContentType "application/json" -Body ($loginAdminBody | ConvertTo-Json)
$adminToken = $loginAdmin.data.accessToken
$adminHeaders = @{ Authorization = "Bearer $adminToken" }
```

## 6) Envio TCP y validacion ACK

### Coban (puerto 5001)
```powershell
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "##,imei:359586015829802,A;"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "359586015829802;"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "imei:359586015829802,tracker,250301123045,,A;"
```
Esperado: ACK `LOAD` (login) y `ON` (heartbeat/tracker).

### Cantrack (puerto 5002)
```powershell
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V0#"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,HTBT#"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V1,250301,123045,A#"
```
Esperado: ACK eco del payload.

## 7) Validacion Ops (con token admin)

```powershell
Invoke-RestMethod -Method Get -Uri "$baseUrl/api/ops/raw/latest?imei=359586015829802&limit=20" -Headers $adminHeaders
Invoke-RestMethod -Method Get -Uri "$baseUrl/api/ops/sessions/active" -Headers $adminHeaders
Invoke-RestMethod -Method Get -Uri "$baseUrl/api/ops/ingestion/ports" -Headers $adminHeaders
Invoke-RestMethod -Method Get -Uri "$baseUrl/api/ops/errors/top" -Headers $adminHeaders
```

Validar en respuestas:
- `success = true`
- `sessionId` y `packetId`
- `ackSent = true` en mensajes validos
- `parseStatus` consistente con payload valido/invalido

## 8) Pasada opcional con EMQX

1. Levantar broker:
```powershell
docker run -d --name emqx-local -p 1883:1883 -p 18083:18083 emqx/emqx:latest
```
2. Cambiar `EventBus:Provider = Emqx` en `TcpServer/appsettings.Development.json`.
3. Reiniciar TcpServer y repetir envio TCP.
4. Validar que no hay errores de publish en logs del worker.

## 9) Criterios de fallo comunes

- `/ready` no responde `200`.
- Login falla despues de verify-email.
- ACK vacio o timeout repetitivo.
- Ops no muestra trazabilidad del IMEI enviado.

## 10) Evidencia minima a guardar

- Captura de `/ready`.
- Respuesta de login user/admin.
- Salida de `Send-TcpPayload.ps1` con ACK.
- Respuesta de `/api/ops/raw/latest` y `/api/ops/errors/top`.
