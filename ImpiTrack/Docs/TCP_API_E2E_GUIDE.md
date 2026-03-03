# Guía E2E: API + TCP Server (IMPITrack)

Esta guía te permite validar el backend completo en local: autenticación, vinculación de GPS, ingestión TCP y diagnóstico por Ops API.

## 1) Prerrequisitos

- SQL Server activo (`ImpiTrakDB`).
- .NET 10 instalado.
- User Secrets configurados para correo (opcional para pruebas de verify por link).

## 2) Arranque correcto

Levanta primero la API (owner de migraciones SQL), luego el TCP Server.

```powershell
dotnet run --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet run --project ImpiTrack/TcpServer/TcpServer.csproj
```

Valores actuales en Development:
- API: `https://localhost:54124` (`http://localhost:54125`)
- TCP Coban: `5001`
- TCP Cantrack: `5002`

## 3) Flujo API (usuario final)

### 3.1 Registrar usuario

`POST https://localhost:54124/api/auth/register`

```json
{
  "userName": "demo.user",
  "email": "demo.user@imptrack.local",
  "password": "ChangeMe!123",
  "fullName": "Demo User"
}
```

### 3.2 Verificar correo

Opción A (recomendada para humanos): usa el link del correo (`GET /api/auth/verify-email/confirm?...`).

Opción B (técnica): `POST /api/auth/verify-email` con `userId` y `token`.

### 3.3 Login y token

`POST https://localhost:54124/api/auth/login`

```json
{
  "userNameOrEmail": "demo.user",
  "password": "ChangeMe!123"
}
```

Guarda `data.accessToken`.

### 3.4 Vincular IMEI al usuario

`POST https://localhost:54124/api/me/devices` con Bearer token:

```json
{
  "imei": "359586015829802"
}
```

## 4) Simular GPS por TCP

Usa el script `Documents/Send-TcpPayload.ps1`.

### 4.1 Coban (puerto 5001)

```powershell
.\Documents\Send-TcpPayload.ps1 -Port 5001 -Payload "##,imei:359586015829802,A;"
.\Documents\Send-TcpPayload.ps1 -Port 5001 -Payload "359586015829802;"
.\Documents\Send-TcpPayload.ps1 -Port 5001 -Payload "imei:359586015829802,tracker,250301123045,,A;"
```

ACK esperado:
- Login: `LOAD`
- Heartbeat/Tracking: `ON`

### 4.2 Cantrack (puerto 5002)

```powershell
.\Documents\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V0#"
.\Documents\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,HTBT#"
.\Documents\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V1,250301,123045,A#"
```

ACK esperado:
- Eco del payload recibido.

## 5) Validar por Ops API (admin)

Login admin:
- Usuario: `admin`
- Password: `ChangeMe!123`

Con token admin, consulta:

- `GET /api/ops/raw/latest?imei=359586015829802&limit=20`
- `GET /api/ops/sessions/active`
- `GET /api/ops/ingestion/ports`
- `GET /api/ops/errors/top`

Debes ver `packetId`, `sessionId`, `protocol`, `messageType`, `ackSent`.

## 6) Troubleshooting rápido

- `401` en `/api/me` o `/api/ops`: token inválido o faltante.
- `403` en `/api/ops`: no usaste token admin.
- No llega ACK TCP: revisa delimitador (`;` en Coban, `#` en Cantrack).
- Link de correo no abre: verifica `Email:VerifyEmailBaseUrl` en user-secrets y que apunte a `https://localhost:54124/api/auth/verify-email/confirm`.
- No aparecen datos en Ops: confirma que el IMEI esté vinculado a un usuario y que ambos procesos (API + TCP) estén levantados.

## 7) Cambiar proveedor de base de datos por variable de entorno

Puedes alternar proveedor sin tocar codigo usando variables `Database__*`.

### 7.1 SQL Server

```powershell
$env:Database__Provider = "SqlServer"
$env:Database__ConnectionString = "Data Source=SANTIAGO;Initial Catalog=ImpiTrakDB;Integrated Security=True;Connect Timeout=30;Encrypt=True;Trust Server Certificate=True;"
$env:Database__EnableAutoMigrate = "true"
```

### 7.2 PostgreSQL

```powershell
$env:Database__Provider = "Postgres"
$env:Database__ConnectionString = "Host=localhost;Port=5432;Database=imptrack;Username=postgres;Password=postgres"
$env:Database__EnableAutoMigrate = "true"
```

Luego reinicia API y TCP Server para que tomen la nueva configuracion.

## 8) Criterio de cierre Fase 3 y 4

- Migraciones aplican limpio en el proveedor seleccionado.
- `dotnet test` pasa en verde.
- `/ready` responde `200` solo cuando storage esta disponible.
- Ingesta guarda `RawPackets` siempre y `Positions` con telemetria cuando el payload trae lat/lon.
- Ops API muestra correlacion por `sessionId` y `packetId`.
- ACK correcto:
  - Coban: `LOAD` (login), `ON` (heartbeat/tracking)
  - Cantrack: echo del payload
