Role: runbook  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15  
When to use: validate the backend locally across API, TCP ingestion, and Ops endpoints

# Guia E2E: API + TCP Server (IMPITrack)

Canonical state note: this is an operational runbook. For the current backend/runtime truth, use [`CURRENT_STATE.md`](CURRENT_STATE.md).

Esta guia valida backend local completo: auth, vinculacion de GPS, ingesta TCP y observabilidad Ops.

Para una ejecucion manual paso a paso (con comandos copy/paste), usa tambien:
- `ImpiTrack/Docs/MANUAL_E2E_EXECUTION_PLAN.md`

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
- `TcpServerConfig:Pipeline:ConsumerWorkers`
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

## 9) EMQX local (bus interno)

Configura en `TcpServer/appsettings.Development.json`:
- `EventBus:Provider = Emqx`
- `EventBus:Host = 127.0.0.1`
- `EventBus:Port = 1883`

Comportamiento esperado:
- Publica `v1/telemetry/{imei}` y `v1/status/{imei}`.
- Si falla la publicacion y se agotan reintentos, envia `v1/dlq/{topic}` cuando `EnableDlq=true`.

### 9.1 Configuracion recomendada (ejemplo)

```json
"EventBus": {
  "Provider": "Emqx",
  "Host": "127.0.0.1",
  "Port": 1883,
  "ClientId": "imptrack-worker-dev",
  "Username": "",
  "Password": "",
  "UseTls": false,
  "TelemetryQoS": 1,
  "StatusQoS": 1,
  "DlqQoS": 1,
  "MaxPublishRetries": 3,
  "RetryBackoffMs": 500,
  "EnableDlq": true
}
```

### 9.2 Arranque de EMQX en local

```powershell
docker run -d --name emqx-local -p 1883:1883 -p 18083:18083 emqx/emqx:latest
```

Luego reinicia `TcpServer` para que tome el provider `Emqx`.

### 9.3 Verificar publicacion MQTT

Suscribete a todos los topics `v1/#`:

```powershell
docker run --rm eclipse-mosquitto:2 mosquitto_sub -h host.docker.internal -p 1883 -t "v1/#" -v
```

En otra terminal, envia payloads TCP (Coban/Cantrack).  
Debes observar eventos en topics `v1/telemetry/{imei}` y `v1/status/{imei}`.

Si no ves mensajes:
- valida que `EventBus:Provider` este en `Emqx`,
- confirma que EMQX este arriba (`docker ps`),
- revisa logs del worker para errores de publish/reintentos/DLQ.

### 9.4 EMQX produccion (auth + ACL + TLS)

Objetivo de produccion:
- `EventBus:Provider = Emqx`
- `EventBus:UseTls = true`
- `EventBus:Port = 8883`
- `EventBus:Username` y `EventBus:Password` definidos

ACL minima recomendada para el usuario del worker:
- publish: `v1/telemetry/+`
- publish: `v1/status/+`
- publish: `v1/dlq/#`
- subscribe (solo diagnostico): `v1/#` opcional

Checklist TLS:
1. Broker EMQX con listener TLS activo (8883) y certificado valido.
2. Host donde corre el worker confia en el certificado/CA del broker.
3. Conexion MQTT exitosa sin errores de handshake/certificate.
4. Publicacion en topics `v1/telemetry/{imei}` y `v1/status/{imei}` con QoS esperado.

Estado actual recomendado para cierre:
- Fase 3: cerrada con SQL Server.
- Fase 4: cerrada para capa de negocio multi-proveedor (SqlServer/Postgres).
- Identity en Postgres: diferido hasta version estable compatible con .NET 10 (ver ADR-001).

## 11) Smoke en CI (GitHub + Azure DevOps)

Rutas:
- GitHub Actions: `.github/workflows/backend-smoke.yml`
- Azure DevOps: `azure-pipelines.yml`

Cobertura de ambos:
- `dotnet restore/build/test`
- smoke SQL Server con `Run-ProviderSmoke.ps1 -Provider SqlServer`
- smoke EMQX con `Run-EmqxSmoke.ps1`
- publicacion de artefactos `.artifacts/*.log`
- validacion de alertas SLO (reglas Prometheus cargadas).

## 10) Troubleshooting de proveedores

- SQL Server `SSPI` (`No se puede generar contexto SSPI`):
  usa un usuario SQL dedicado en vez de `Integrated Security=True`, o valida SPN/Kerberos del host SQL.
- SQL Server `TcpTestSucceeded=False` en `SANTIAGO:1433`:
  habilita TCP/IP en SQL Server Configuration Manager y revisa firewall/regla del puerto 1433.
- PostgreSQL `28P01 password authentication failed`:
  corrige `Username/Password` o crea el usuario/DB objetivo antes del smoke.
- PostgreSQL en Identity (`MissingMethodException`):
  en net10 estable, configura `IdentityStorage:Provider=InMemory` o `SqlServer`.
  Si pones `Postgres`, la API ahora falla al inicio con error explicito.

## 11) Estado y deuda abierta

- Backend Fase 0, 1, 2, 3 y 4: cerrado para alcance actual.
- SQL Server: proveedor principal validado para desarrollo local.
- EMQX interno: habilitado para pruebas locales.
- Deuda abierta: `IdentityStorage:Provider=Postgres` diferido hasta estabilidad .NET 10 + EF/Npgsql/Identity.
