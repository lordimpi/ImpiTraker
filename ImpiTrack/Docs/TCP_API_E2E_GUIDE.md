# Guia E2E: API + TCP Server (IMPITrack)

Esta guia valida backend local completo: auth, vinculacion de GPS, ingesta TCP y observabilidad Ops.

## 1) Prerrequisitos

- .NET 10 instalado.
- SQL Server disponible (si usaras SqlServer).
- PostgreSQL disponible (si usaras Postgres).
- User Secrets configurados para SMTP (opcional).

## 2) Arranque base

Levanta primero la API y luego TCP Server:

```powershell
dotnet run --project ImpiTrack/ImpiTrack.Api/ImpiTrack.Api.csproj
dotnet run --project ImpiTrack/TcpServer/TcpServer.csproj
```

Valores Development actuales:
- API: `https://localhost:54124` (`http://localhost:54125`)
- TCP Coban: `5001`
- TCP Cantrack: `5002`

## 3) Flujo API (usuario final)

1. `POST /api/auth/register`
2. Confirmar correo por `GET /api/auth/verify-email/confirm?...` (o `POST /api/auth/verify-email`)
3. `POST /api/auth/login` y guardar `data.accessToken`
4. `POST /api/me/devices` con Bearer token:

```json
{
  "imei": "359586015829802"
}
```

## 4) Simular GPS por TCP

Usa `ImpiTrack/Tools/Send-TcpPayload.ps1`.

### Coban (5001)
```powershell
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "##,imei:359586015829802,A;"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "359586015829802;"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5001 -Payload "imei:359586015829802,tracker,250301123045,,A;"
```

ACK esperado: `LOAD` (login), `ON` (heartbeat/tracking).

### Cantrack (5002)
```powershell
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V0#"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,HTBT#"
.\ImpiTrack\Tools\Send-TcpPayload.ps1 -Port 5002 -Payload "*HQ,359586015829802,V1,250301,123045,A#"
```

ACK esperado: eco del payload recibido.

## 5) Validar por Ops API (admin)

Con token admin:
- `GET /api/ops/raw/latest?imei=359586015829802&limit=20`
- `GET /api/ops/sessions/active`
- `GET /api/ops/ingestion/ports`
- `GET /api/ops/errors/top`

Debes ver `packetId`, `sessionId`, `protocol`, `messageType`, `ackSent`.

## 6) Cambiar proveedor desde appsettings (sin variables de entorno)

Archivos:
- API: `ImpiTrack/ImpiTrack.Api/appsettings.Development.json`
- TCP: `ImpiTrack/TcpServer/appsettings.json`

Claves que controlan el switch:
- `Database:Provider` (`SqlServer` o `Postgres`)
- `IdentityStorage:Provider` (`SqlServer` o `InMemory`) en API
- `ConnectionStrings:SqlServer` / `ConnectionStrings:Postgres`
- `ConnectionStrings:IdentitySqlServer` / `ConnectionStrings:IdentityPostgres`
- `EventBus:Provider` (`InMemory` o `Emqx` placeholder)
- `EventBus:Host` / `EventBus:Port` / `EventBus:ClientId`
- `EventBus:*QoS` (`TelemetryQoS`, `StatusQoS`, `DlqQoS`)
- `EventBus:MaxPublishRetries` / `EventBus:RetryBackoffMs` / `EventBus:EnableDlq`
- `TcpServerConfig:Pipeline:RawChannelCapacity`
- `TcpServerConfig:Pipeline:RawConsumerWorkers`
- `TcpServerConfig:Pipeline:RawFullMode` (`Wait`, `Drop`, `Disconnect`)

Ejemplo SQL Server:
- `Database:Provider = SqlServer`
- `IdentityStorage:Provider = SqlServer`

Ejemplo PostgreSQL:
- `Database:Provider = Postgres`
- `IdentityStorage:Provider = InMemory` (temporal en net10 estable)

## 7) Smoke por proveedor (automatizado)

Script: `ImpiTrack/Tools/Run-ProviderSmoke.ps1`

```powershell
# Ejecuta SqlServer + Postgres
.\ImpiTrack\Tools\Run-ProviderSmoke.ps1 -Provider Both

# Solo SQL Server (conexion personalizada)
.\ImpiTrack\Tools\Run-ProviderSmoke.ps1 -Provider SqlServer `
  -SqlServerConnectionString "Data Source=SANTIAGO;Initial Catalog=ImpiTrakDB;Integrated Security=True;Encrypt=False;Trust Server Certificate=True;"

# Solo Postgres (conexion personalizada)
.\ImpiTrack\Tools\Run-ProviderSmoke.ps1 -Provider Postgres `
  -PostgresConnectionString "Host=localhost;Port=5432;Database=imptrack;Username=postgres;Password=postgres"
```

Notas del script:
- Fuerza `IdentityStorage:Provider=InMemory` para aislar la prueba de negocio/API del provider de Identity.
- Fuerza `Database:EnableAutoMigrate=true`.
- Valida `/ready` y deja logs en `ImpiTrack/.artifacts/smoke-*.out.log` y `ImpiTrack/.artifacts/smoke-*.err.log`.
- Soporta `-BuildBeforeRun` para escenarios CI sin binarios previos.

## 8) Criterio de cierre Fase 3/4

- Migraciones aplican limpio en proveedor seleccionado.
- `dotnet test` pasa en verde.
- `/ready` responde `200` con storage disponible.
- Ingesta persiste `raw_packets` y telemetria en `positions` cuando aplica.
- Ops expone correlacion por `sessionId` y `packetId`.
- Dedupe de tracking activo por `positions.dedupe_key` (sin duplicados por replay).

## 10) EMQX local (bus interno)

Configura en `TcpServer/appsettings.Development.json`:
- `EventBus:Provider = Emqx`
- `EventBus:Host = 127.0.0.1`
- `EventBus:Port = 1883`

Comportamiento esperado:
- Publica `v1/telemetry/{imei}` y `v1/status/{imei}`.
- Si falla la publicacion y se agotan reintentos, envia `v1/dlq/{topic}` cuando `EnableDlq=true`.

Smoke automatizado recomendado:

```powershell
.\ImpiTrack\Tools\Run-EmqxSmoke.ps1
```

Ejemplo CI (sin recompilar dentro del smoke):

```powershell
.\ImpiTrack\Tools\Run-EmqxSmoke.ps1 -NoBuild -StartupTimeoutSeconds 120 -TopicTimeoutSeconds 30
```

El script valida:
- publicacion en `v1/telemetry/+`
- publicacion en `v1/status/+`
- ruta DLQ en `v1/dlq/#` usando fallo simulado por configuracion
- logs de evidencia en `ImpiTrack/.artifacts/smoke-emqx-worker*.log`

Estado actual recomendado para cierre:
- Fase 3: cerrada con SQL Server.
- Fase 4: cerrada para capa de negocio multi-proveedor (SqlServer/Postgres).
- Identity en Postgres: habilitado para bootstrap en Development usando `EnsureCreated`.

## 11) Smoke en CI (GitHub + Azure DevOps)

Rutas:
- GitHub Actions: `.github/workflows/backend-smoke.yml`
- Azure DevOps: `azure-pipelines.yml`

Cobertura de ambos:
- `dotnet restore/build/test`
- smoke SQL Server con `Run-ProviderSmoke.ps1 -Provider SqlServer`
- smoke EMQX con `Run-EmqxSmoke.ps1`
- publicacion de artefactos `.artifacts/*.log`

## 9) Troubleshooting de proveedores

- SQL Server `SSPI` (`No se puede generar contexto SSPI`):
  usa un usuario SQL dedicado en vez de `Integrated Security=True`, o valida SPN/Kerberos del host SQL.
- SQL Server `TcpTestSucceeded=False` en `SANTIAGO:1433`:
  habilita TCP/IP en SQL Server Configuration Manager y revisa firewall/regla del puerto 1433.
- PostgreSQL `28P01 password authentication failed`:
  corrige `Username/Password` o crea el usuario/DB objetivo antes del smoke.
- PostgreSQL en Identity (schema inicial no creado):
  valida `IdentityStorage:Provider=Postgres`, cadena de conexion y ejecuta la API en `Development`
  para que `EnsureCreated` cree tablas base de Identity.
