# IMPITrack — Documentación Técnica (Backend)  
**Arquitectura + DDD Light + Worker TCP + Web API (Ops/Toolbox) + Protocolos + Roadmap**

> **Stack objetivo (2026):**  
> **Backend:** .NET 10 (**Worker TCP** + **Web API**), SignalR (futuro), Serilog, OpenTelemetry  
> **Frontend:** (fuera de este repo / proyecto aparte)  
> **Data:** SQL Server + PostgreSQL (según módulo / entorno)  
> **Infra:** GCP (Compute Engine o GKE), NGINX, Redis (backplane + cache), opcional Pub/Sub  
>
> **Propósito:** Plataforma empresarial para **ingestión, normalización, almacenamiento y visualización** de telemetría GPS **multi-dispositivo** y **multi-protocolo**, con **tiempo real** (futuro) y trazabilidad de payloads.

---

## Estado actual (Mar 2026)

- Backend core operativo y estable en .NET 10.
- Fase 0, 1, 2, 3 y 4 del backend cerradas en alcance actual.
- SQL Server validado para desarrollo local y smoke.
- Event Bus interno disponible con `InMemory` y `EMQX`.
- Observabilidad base disponible (logs estructurados + metricas + alertas iniciales).

### Deuda abierta explicita

1. Identity sobre PostgreSQL en .NET 10: diferido por estabilidad del stack EF/Npgsql/Identity.
2. CI/CD formal en GitHub Actions para build/test/smoke automatico.
3. Afinar dashboards/umbrales de alerta con trafico real de produccion.

---

## 0. Tabla de contenidos

1. [Visión y objetivos](#1-visión-y-objetivos)  
2. [Alcance y principios](#2-alcance-y-principios)  
3. [Arquitectura general](#3-arquitectura-general)  
4. [Decisiones clave](#4-decisiones-clave)  
5. [DDD light por Bounded Contexts](#5-ddd-light-por-bounded-contexts)  
6. [Estructura de solución por proyectos](#6-estructura-de-solución-por-proyectos)  
7. [Worker TCP (Ingestión)](#7-worker-tcp-ingestión)  
8. [Protocolos: diseño de ACL + plantillas](#8-protocolos-diseño-de-acl--plantillas)  
9. [Web API](#9-web-api)  
10. [SignalR (Tiempo real)](#10-signalr-tiempo-real)  
11. [Modelo de datos](#11-modelo-de-datos)  
12. [Persistencia, performance e idempotencia](#12-persistencia-performance-e-idempotencia)  
13. [Seguridad (TCP + API)](#13-seguridad-tcp--api)  
14. [Observabilidad y monitoreo](#14-observabilidad-y-monitoreo)  
15. [Despliegue en GCP](#15-despliegue-en-gcp)  
16. [Roadmap](#16-roadmap)  
17. [Checklist de producción](#17-checklist-de-producción)  
18. [Anexos: payloads, simuladores y troubleshooting](#18-anexos-payloads-simuladores-y-troubleshooting)

---

## 1. Visión y objetivos

**IMPITrack** recibe mensajes GPS desde dispositivos vía **GPRS/TCP**, valida identidad (IMEI), **normaliza** (parseo por protocolo), **persiste** (posiciones/eventos/raw) y expone al backend (API) funcionalidades de consulta y operación con **diagnóstico de producción** (Ops/Toolbox).

### Objetivos no funcionales

- **Alta concurrencia:** miles de conexiones TCP simultáneas (sin *thread-per-connection*).
- **Backpressure:** el sistema debe degradar con gracia si DB se pone lenta (colas/buffers).
- **Escalabilidad por configuración:** agregar puertos/protocolos sin tocar el core.
- **Trazabilidad:** preservar *raw payload* y correlación por sesión/paquete.
- **Modularidad:** parsers por protocolo como ACL (Anti-Corruption Layer).
- **Seguridad:** IMEI autorizado (TCP) + JWT/roles (API).
- **Operabilidad:** logs estructurados, métricas, health checks, alertas.
- **Documentación:** APIs públicas documentadas con **XML documentation**.

---

## 2. Alcance y principios

### 2.1 Alcance (MVP → Enterprise)
- Ingestión TCP multi-puerto multi-protocolo
- Parsers Coban/TK y Cantrack/G06L (base inicial)
- Persistencia de posiciones, eventos, sesiones y raw
- Web API lectura + administración + Ops/Toolbox
- Observabilidad completa desde el día 1

> El frontend se desarrolla como **proyecto aparte** (Angular) y no se aborda en este documento.

### 2.2 No alcance inmediato (pero diseñado para)
- Streaming (Kafka / Pub/Sub / Redis Streams) para desacople fuerte
- Multi-tenant completo con aislamiento de datos
- Comandos bidireccionales con estados y reintentos “enterprise”
- Geocercas, analítica avanzada, reporteado masivo

### 2.3 Principios
- **Separación fuerte:** ingestión no depende de API (modo core).  
- **Configuración manda:** puertos, protocolos, pools, límites, TTL, etc.
- **Fallos son normales:** reintentos, circuit breakers, timeouts, degradación.
- **Evidencia primero:** raw y correlación son obligatorios.

---

## 3. Arquitectura general

### 3.1 Flujo macro

```text
GPS Devices (Coban / TK303G / G06L / Box Tracker / ...)
      │  (GPRS/TCP)
      ▼
Worker TCP (BackgroundService / Worker Service)
      │
      ├──► DB (SQL Server / PostgreSQL)
      │        ├─ RawPackets
      │        ├─ Positions
      │        ├─ Events
      │        └─ Sessions/Devices
      │
      └──► (Opcional) Event Bus (Pub/Sub / Redis Streams / Kafka)
               │
               ▼
Web API (.NET 10)
      │
      ├──► Ops/Toolbox (diagnóstico sin UI)
      ├──► Auth (JWT)
      ├──► Admin/Devices/Queries
      ├──► (Futuro) SignalR
      └──► Observabilidad (Serilog + OTel)
```

### 3.2 Reglas de acoplamiento
- Worker TCP **NO** debe llamar a la Web API para validar IMEI o persistir.  
  - Validación IMEI preferida: **cache + DB** (evita caer si API cae).
- La Web API **consume DB** (y opcionalmente bus/outbox) para diagnósticos, consultas y (futuro) realtime.

### 3.3 Escalado
- Worker TCP: horizontal por **puerto** (preferido) o por **LB TCP** (avanzado).
- Web API: horizontal libre.
- SignalR (futuro): requiere **backplane** (Redis) si hay múltiples instancias.

---

## 4. Decisiones clave

### 4.1 Monolito modular → microservicios cuando duela
Arranque como **modular monolith** (proyectos separados), listo para:
- Worker TCP (servicio independiente)
- Web API (servicio independiente)
- Event bus/outbox para desacoplar ingestión/persistencia/realtime

### 4.2 Multi-puerto por HostedServices
Un `BackgroundService` por puerto/listener:
- aislamiento (un puerto ruidoso no tumba los otros)
- métricas por puerto
- routing sencillo protocolo↔puerto

### 4.3 DDD “light”
DDD donde sí hay dominio:
- Tracking (posiciones/eventos/sesiones/dispositivos)
- Commands (comandos y estados)
- Admin/Identity (usuarios/roles/tenancy futuro)
Protocolos → **ACL** en Infrastructure.

### 4.4 Dual DB: SQL Server + PostgreSQL
Opciones típicas:
- **SQL Server**: core operacional (OLTP), consultas admin, compatibilidad enterprise.
- **PostgreSQL**: analítica, particionado por tiempo, extensiones (PostGIS futuro).
Regla: **una fuente de verdad por agregado**. Evita “escritura en dos DB” si no hay bus.

---

## 5. DDD light por Bounded Contexts

### 5.1 Bounded Contexts

**Tracking (Core)**
- Aggregates/Entidades: `Device`, `DeviceSession`, `Position`, `DeviceEvent`, `RawPacket`
- Value Objects: `Imei`, `GeoPoint`, `SpeedKmh`, `HeadingDeg`, `UtcInstant`, `ProtocolName`

**Protocols/Ingestion (ACL)**
- `IProtocolParser` por protocolo
- `IFrameDecoder` por protocolo (framing TCP)
- Traduce `RawPacket` → `TrackingMessage` (modelo interno)

**Commands**
- `CommandRequest`, `CommandResponse`, correlación, reintentos, estados
- `CommandGateway` (estrategia de envío: TCP directo, cola, etc.)

**Identity/Admin**
- Usuarios, roles, tenants (futuro)
- Administración de dispositivos, límites, reglas

### 5.2 Regla de oro
> El dominio **no** debe conocer strings raros del protocolo.  
> El protocolo se queda en ACL y entrega datos limpios.

---

## 6. Estructura de solución por proyectos

Propuesta:

1) **Common**
- ValueObjects y primitives (IMEI, GeoPoint)
- Helpers (knots→km/h, parsing fechas)
- Contratos internos compartidos

2) **DataAccess**
- Repositorios (Dapper o EF Core)
- Migrations (EF) / scripts SQL (Dapper)
- Implementaciones `IDeviceRepository`, `IPositionRepository`, etc.

3) **Infrastructure**
- TCP engine (listener, sessions, framing)
- Parsers por protocolo (ACL)
- Cache (Memory/Redis), rate limiting, ban temporal
- Observabilidad (Serilog + OpenTelemetry)
- Integraciones futuras (Pub/Sub, etc.)

4) **IMPITrack.Worker**
- Program.cs, DI, IOptions
- HostedServices por puerto
- Wiring de pipeline (Channels + workers)

5) **IMPITrack.Api**
- Web API .NET 10
- Auth JWT, endpoints, swagger
- Ops/Toolbox endpoints `/api/ops/*`
- Queries optimizadas

---

## 7. Worker TCP (Ingestión)

### 7.1 Contrato del Worker
1) Escuchar puertos TCP  
2) Aceptar conexiones concurrentes  
3) Leer **frames completos**  
4) Identificar protocolo  
5) Extraer IMEI  
6) Validar IMEI  
7) Parsear a `TrackingMessage`  
8) Persistir raw + normalizado  
9) Responder ACK correcto  
10) Loguear + medir  

### 7.2 Config multi-puerto con IOptions

```json
{
  "TcpServerConfig": {
    "Servers": [
      { "Name": "CobanTrack",   "Port": 5000, "Protocol": "COBAN" },
      { "Name": "CantrackG06L", "Port": 5001, "Protocol": "CANTRACK" }
    ],
    "Socket": {
      "ReceiveBufferBytes": 8192,
      "MaxFrameBytes": 16384,
      "IdleTimeoutSeconds": 180,
      "ReadTimeoutSeconds": 30
    },
    "Pipeline": {
      "ChannelCapacity": 20000,
      "ParserWorkers": 8,
      "DbWorkers": 4
    },
    "Security": {
      "MaxFramesPerMinutePerIp": 600,
      "InvalidFrameThreshold": 40,
      "BanMinutes": 15
    }
  }
}
```

### 7.3 Pipeline (alta concurrencia)
- Accept loop por puerto (async)
- Read loop por conexión (async)
- `IFrameDecoder` arma frames robustos (concatenados/incompletos)
- `Channel<RawPacket>` **bounded** (backpressure)
- `ParserWorkers` → `TrackingMessage + AckResponse`
- `DbWorkers` → persistencia (raw + posiciones/eventos)
- Enviar ACK rápido (idealmente no esperando DB si el protocolo lo permite)

### 7.4 Framing TCP (hard mode)
- Frame concatenado: un `read()` puede traer 2–10 frames
- Frame incompleto: un frame puede llegar partido
- Basura/ruido: caracteres no válidos, payload enorme
- Solución: buffer acumulativo + delimitadores + límites de tamaño

### 7.5 Sesiones (estado)
Estados: `Connected` → `Identified` → `Authorized` → `Tracking` → `Disconnected`  
Campos: `SessionId`, `RemoteIp`, `Port`, `ConnectedAt`, `LastSeenAt`, `LastHeartbeatAt`, `DeviceId`.

### 7.6 Validación de IMEI (rápida)
- Memory cache TTL (2–5 min)
- Redis cache TTL (opcional)
- DB como fuente de verdad

### 7.7 ACK y reconexión constante
Regla operativa:
- Si un dispositivo se reconecta a cada rato: **o framing está mal, o ACK está mal, o ACK llega tarde**.
- Log obligatorio: `ack_sent`, `ack_payload`, `latency_ms`, `imei`, `port`.

---

## 8. Protocolos: diseño de ACL + plantillas

### 8.1 Interfaces (conceptual)

- `IFrameDecoder` (framing)
- `IProtocolParser` (parseo)
- `IAckStrategy` (ACK)
- `ICommandBuilder` (comandos, futuro)

### 8.2 Plantilla de documentación por protocolo

| Campo | Ejemplo |
|---|---|
| Delimitador | `;` o `#` |
| Login | `...` |
| ACK login | `LOAD` / `echo` |
| Heartbeat | `...` |
| ACK heartbeat | `ON` / `echo` |
| GPS data | estructura |
| Status/Eventos | hex/bits |
| Notas | timezone, knots, checksum |

### 8.3 Conversiones
- knots → km/h = `knots * 1.852`
- fecha/hora: documentar si viene UTC o local (normalizar a UTC)

---

## 9. Web API

### 9.1 Endpoints propuestos (negocio)

**Devices**
- `GET /api/devices`
- `POST /api/devices`
- `GET /api/devices/{imei}`
- `PUT /api/devices/{imei}`
- `GET /api/devices/{imei}/last-position`
- `GET /api/devices/{imei}/positions?from&to&page&pageSize`
- `GET /api/devices/{imei}/events?from&to&page&pageSize`

**Sessions**
- `GET /api/sessions/active`
- `GET /api/devices/{imei}/sessions?from&to`

**Commands**
- `POST /api/devices/{imei}/commands`
- `GET /api/devices/{imei}/commands?from&to`

**Health**
- `GET /health`
- `GET /ready`

### 9.2 Paginación y límites
- Obligatoria en Positions/Events
- `pageSize` máximo (ej 500/1000)
- Rango de fechas obligatorio en tablas grandes

---

## 10. SignalR (Tiempo real)
*(futuro, backend)*

- Rooms:
  - `device:{imei}`
  - `tenant:{id}` (futuro)
- Eventos:
  - `PositionUpdated`
  - `DeviceEvent`
  - `DeviceOnline` / `DeviceOffline`

**Escalado:** Redis backplane si hay múltiples instancias.  
**Fuente de eventos:** polling → outbox → bus (evolutivo).

---

## 11. Modelo de datos

### 11.1 `Devices`
- `DeviceId` (PK)
- `Imei` (unique)
- `Protocol`
- `IsActive`
- `Alias` (nullable)
- `CreatedAtUtc`, `UpdatedAtUtc`

### 11.2 `DeviceSessions`
- `SessionId` (PK uuid)
- `DeviceId` (FK)
- `ConnectedAtUtc`, `DisconnectedAtUtc` (nullable)
- `RemoteIp`, `Port`
- `LastSeenAtUtc`, `LastHeartbeatAtUtc`
- `CloseReason` (nullable)
- `FramesIn`, `FramesInvalid` (recomendado)

### 11.3 `RawPackets`
- `PacketId` (PK uuid)
- `SessionId` (FK)
- `DeviceId` (nullable)
- `Imei` (nullable, recomendado)
- `Protocol`
- `Port`, `RemoteIp`
- `ReceivedAtUtc`
- `PayloadText` o `PayloadBytes`
- `PayloadHash` (opcional)
- `ParseStatus`, `ParseError`
- `AckSent`, `AckPayload` (truncado), `AckAtUtc` (recomendado)
- `LatencyParseMs`, `LatencyPersistMs` (opcional)

### 11.4 `Positions`
- `PositionId`
- `DeviceId`, `SessionId`, `PacketId`
- `GpsTimeUtc`, `ReceivedAtUtc`
- `Latitude`, `Longitude`
- `SpeedKmh`, `HeadingDeg`, `AltitudeM` (nullable)
- `MessageType`
- `StatusHex` (nullable)
- `IsValid`
- `ExtraJson` (json/jsonb)

### 11.5 `DeviceEvents`
- `EventId`
- `DeviceId`, `SessionId`, `PacketId`
- `EventType`
- `EventTimeUtc`, `ReceivedAtUtc`
- `RawStatusHex`
- `ExtraJson` (json/jsonb)

### 11.6 `Commands`
- `CommandId`
- `DeviceId`
- `Protocol`
- `CommandText`
- `CreatedAtUtc`, `SentAtUtc`
- `Status` (Queued/Sent/Ack/Failed/Timeout)
- `CorrelationId`
- `ResponseRaw`
- `Retries`, `LastError`

---

## 12. Persistencia, performance e idempotencia

### 12.1 Índices
- `Devices(Imei) UNIQUE`
- `Positions(DeviceId, GpsTimeUtc DESC)`
- `DeviceEvents(DeviceId, EventTimeUtc DESC)`
- `RawPackets(DeviceId, ReceivedAtUtc DESC)`
- `DeviceSessions(DeviceId, ConnectedAtUtc DESC)`

### 12.2 Retención / archivado
- Particionado mensual (recomendado en Postgres)
- Hot retention + archive a storage para RawPackets si crece mucho

### 12.3 Idempotencia
- Hash por posición para deduplicar
- Opcional unique index según carga

### 12.4 Backpressure
- Channel bounded + política (Wait / DropOldest / DropNewest)
- Métricas de backlog + alertas

---

## 13. Seguridad (TCP + API)

### 13.1 TCP
- IMEI allowlist + active flag
- Rate limit por IP + ban temporal
- MaxFrameBytes y validación de caracteres
- Firewall GCP minimalista

### 13.2 API
- JWT + roles
- Rate limit en gateway
- CORS estricto (si se expone a un frontend futuro)

### 13.3 Secrets
- Secret Manager / variables seguras
- nada sensible en repos

---

## 14. Observabilidad y monitoreo

### 14.1 Logs (Serilog)
Campos mínimos: `imei`, `protocol`, `port`, `remoteIp`, `sessionId`, `packetId`, `latencyMs`, `error`.

### 14.2 Métricas (OTel/Prometheus)
- conexiones activas
- frames/s
- parse_ok / parse_fail
- ack_sent
- backlog
- persist latency p95

### 14.3 Health
Worker: puertos ok, backlog ok, db ok (opcional)  
API: liveness, readiness, db/redis ok.

---

## 14.5 Backend Toolbox (Ops) — Operación y diagnóstico sin UI

> Objetivo: que el backend sea **depurable y operable** desde el día 1, sin depender de un frontend.  
> Esto evita “adivinar” por qué un GPS se reconecta, por qué falla el parseo o por qué hay backlog.

### 14.5.1 Principio de oro
- **Siempre** se guarda evidencia (`RawPackets`), incluso si el parse falla.
- Cada frame debe tener correlación:
  - `SessionId` (por conexión)
  - `PacketId` (por frame)
  - `Imei` (cuando exista)
  - `RemoteIp`, `Port`, `Protocol`, `MessageType`

### 14.5.2 Datos mínimos a persistir (para soportar el Toolbox)

#### A) RawPackets (Evidencia / auditoría)
Guardar por cada frame completo:
- `PacketId` (GUID)
- `SessionId` (GUID)
- `DeviceId` (nullable hasta autorizar)
- `Imei` (nullable si aún no se extrajo)
- `Protocol` (string)
- `Port` (int), `RemoteIp` (string)
- `ReceivedAtUtc` (datetime)
- `PayloadText` o `PayloadBytes` (según estrategia)
- `PayloadHash` (opcional, SHA-256)
- `ParseStatus` (Ok / Failed / Partial / Rejected)
- `ParseError` (string corto, sin stacktrace)
- `AckSent` (bool), `AckPayload` (string/bytes truncado), `AckAtUtc` (datetime nullable)
- `LatencyParseMs` (opcional), `LatencyPersistMs` (opcional)

> Nota: evitar loguear el payload completo en logs; el payload vive en DB/Storage, y el log referencia `PacketId`.

#### B) DeviceSessions (Estado de conexión)
- `SessionId`, `DeviceId` (nullable), `RemoteIp`, `Port`
- `ConnectedAtUtc`, `DisconnectedAtUtc` (nullable), `CloseReason`
- `LastSeenAtUtc`, `LastHeartbeatAtUtc` (nullable)
- contadores: `FramesIn`, `FramesInvalid`

#### C) Normalizado (Positions/Events)
- `PacketId`, `SessionId`, `DeviceId` (correlación)
- `GpsTimeUtc`, `ReceivedAtUtc`
- `Lat`, `Lon`, `SpeedKmh`, `HeadingDeg`, `StatusHex`, `MessageType`
- `ExtraJson` (json/jsonb) para campos específicos no estándar

### 14.5.3 Ops API (Toolbox sin UI)

> Ruta sugerida: `/api/ops/*` (protegida con JWT rol Admin).

#### A) Salud y carga de ingestión
1) **Estado por puerto**
- `GET /api/ops/ingestion/ports`
  - conexiones activas por puerto
  - frames/s (aprox)
  - backlog del channel (si aplica)
  - parse_ok / parse_fail ratio (últimos N min)

2) **Sesiones activas**
- `GET /api/ops/sessions/active?port=5000`
  - `SessionId`, `RemoteIp`, `Port`, `Imei/DeviceId` (si existe)
  - `LastSeenAtUtc`, `FramesIn`, `FramesInvalid`

#### B) Evidencia (raw) y depuración de parseo
3) **Últimos raw packets por IMEI**
- `GET /api/ops/raw/latest?imei=...&limit=50`
  - `ReceivedAtUtc`, `ParseStatus`, `ParseError` (si existe)
  - `AckSent`, `AckPayload` (truncado), `AckAtUtc`
  - tamaño del payload (bytes)

4) **Raw packet por PacketId**
- `GET /api/ops/raw/{packetId}`
  - payload completo (con límites: truncar/paginar si excede)
  - parse error y metadata
  - ack info completa

5) **Top errores recientes**
- `GET /api/ops/errors/top?from=...&to=...&groupBy=protocol|port|errorCode`
  - conteos por agrupación
  - ejemplo de PacketId por error para investigar rápido

#### C) Reconexión / ACK troubleshooting
6) **Tasa de reconexión por dispositivo o IP**
- `GET /api/ops/reconnects?from=...&to=...&groupBy=imei|ip`
  - #sessions en el rango
  - duración promedio
  - últimos close reasons
  - indicador heurístico (opcional): “ACK missing / parse_fail alto / idle timeout”

7) **Historial de ACK por sesión**
- `GET /api/ops/sessions/{sessionId}/acks?limit=200`
  - `PacketId`, `AckPayload`, `AckAtUtc`, `MessageType`, `ParseStatus`

### 14.5.4 Métricas mínimas (aunque sea en logs al inicio)
Métricas recomendadas (OTel/Prometheus cuando esté listo):
- `tcp_connections_active{port}`
- `tcp_frames_in_total{port,protocol}`
- `tcp_frames_invalid_total{port,protocol}`
- `tcp_parse_ok_total{port,protocol}`
- `tcp_parse_fail_total{port,protocol}`
- `tcp_ack_sent_total{protocol,ackType}`
- `pipeline_backlog{port}`
- `pipeline_dropped_total{port,reason}` (si hay drop)
- `db_persist_latency_ms` (histograma p50/p95/p99)

### 14.5.5 Runbook (operación en 60 segundos)

#### Síntoma: “Reconexión constante”
1) `GET /api/ops/raw/latest?imei=...`
   - ¿`AckSent=false`? → ACK no se está enviando.
   - ¿`AckPayload` no coincide? → ACK incorrecto.
   - ¿`AckAtUtc` tarda mucho? → ACK amarrado a DB o backlog alto.
2) `GET /api/ops/errors/top?...`
   - Si parse_fail alto → framing/delimitadores/MaxFrameBytes/encoding.
3) `GET /api/ops/sessions/active`
   - Si muchas sesiones cortas → timeouts, inválidos, rate limit o ACK.

#### Síntoma: “Backlog sube”
1) `GET /api/ops/ingestion/ports`
   - backlog alto sostenido → DB lenta o workers insuficientes.
2) Revisar:
   - p95 persist latency, índices DB, tamaño payload raw.
3) Si aplica:
   - activar política de backpressure (Wait/DropOldest/Disconnect) explícita.

#### Síntoma: “Parse_fail alto”
1) Revisar delimitador/framing:
   - concatenados e incompletos
   - MaxFrameBytes
2) Revisar encoding:
   - ASCII/UTF-8 esperado por protocolo
3) Comparar un `PacketId` concreto:
   - `GET /api/ops/raw/{packetId}`

### 14.5.6 Seguridad de Ops
- `/api/ops/*` solo para rol Admin.
- No exponer payload completo sin límites (truncar, paginar o proteger).
- Evitar payloads completos en logs (usar `PacketId`, hash, tamaños).
- Rate limit en Ops endpoints si se expone públicamente.

---

## 14.6 Ops API como Web API separada (decisión definitiva)

> Decisión: el **Ops Toolbox** se implementa en un **proyecto Web API separado** (`IMPITrack.Api`).  
> El Worker TCP (`IMPITrack.Worker`) **no expone HTTP**. Su responsabilidad es: TCP → framing → parse → persistencia → ACK.

### 14.6.1 Responsabilidades por servicio

#### Worker TCP (IMPITrack.Worker)
- Escucha puertos TCP, gestiona sesiones, framing robusto.
- Parseo por protocolo (ACL).
- Envía ACK correcto y rápido.
- Persiste: `RawPackets` (siempre), `DeviceSessions`, `Positions`, `DeviceEvents`.
- Escribe métricas/logs (observabilidad).
- **No depende** de la Web API para operar.

#### Web API (IMPITrack.Api)
- Expone endpoints:
  - negocio: devices/positions/events/sessions/commands (cuando aplique)
  - **ops/toolbox**: `/api/ops/*` (diagnóstico)
- Autenticación/autorización (JWT + roles).
- Puede emitir realtime (SignalR) leyendo DB (poll/outbox/bus).
- Puede ofrecer exportaciones (CSV/GPX) a futuro.

### 14.6.2 Flujo de datos (sin streaming todavía)

```text
TCP Worker
  ├─ guarda RawPackets + Sessions + Positions/Events (DB)
  └─ (opcional futuro) escribe Outbox

Web API
  ├─ lee DB (queries optimizadas e índices)
  ├─ expone /api/ops/* (diagnóstico)
  └─ (futuro) SignalR: push PositionUpdated/Event
```

### 14.6.3 Modelo mínimo para Ops (requisito)
Para soportar `/api/ops/*`, las tablas deben incluir:
- `RawPackets`: PacketId, SessionId, RemoteIp, Port, Protocol, ParseStatus, ParseError, AckSent, AckPayload(trunc), AckAtUtc, ReceivedAtUtc, Payload...
- `DeviceSessions`: SessionId, DeviceId, RemoteIp, Port, Connected/Disconnected, LastSeen, counters
- `Devices`: Imei, Protocol, IsActive
- `Positions` / `DeviceEvents`: correlación por PacketId/SessionId/DeviceId

### 14.6.4 Seguridad y exposición
- `/api/ops/*` requiere rol **Admin**.
- No exponer payload completo sin control:
  - truncar en listados
  - permitir payload completo solo en `GET /api/ops/raw/{packetId}`
  - limitar tamaño de respuesta / paginar
- No loguear payload completo (logs → PacketId/hash + metadata).

### 14.6.5 Contratos internos recomendados (para claridad de implementación)
- `RawPacket`: `PacketId`, `SessionId`, `RemoteIp`, `Port`, `ReceivedAtUtc`, `Protocol`, `Payload`
- `ParseResult`: `IsSuccess`, `MessageType`, `Imei?`, `TrackingMessage?`, `ParseError?`, `AckResponse?`
- `AckResponse`: `ShouldSend`, `AckType`, `Payload` (texto/bytes)

### 14.6.6 Recomendación de implementación (orden)
1) Worker persiste `RawPackets` y `DeviceSessions` con correlación.
2) API implementa `/api/ops/raw/latest`, `/api/ops/raw/{packetId}`, `/api/ops/errors/top`.
3) API implementa `/api/ops/ingestion/ports` y `/api/ops/sessions/active`
   - En MVP, “ports” puede venir de DB (últimos N min) o de métricas si ya existen.
4) Luego se agregan positions/events y dashboards de estado.

### 14.6.7 Realtime (futuro, sin romper esta decisión)
- MVP: API puede hacer polling por `Positions` recientes y emitir `PositionUpdated`.
- Escalado: patrón Outbox (Worker escribe evento) + API consume y emite SignalR.
- Enterprise: Pub/Sub/Streams/Kafka como bus.

---

## 15. Despliegue en GCP

### 15.1 Compute Engine (MVP)
- VM Ubuntu + systemd
- NGINX reverse proxy API
- Firewall puertos: 22 (limitado), 80/443, 5000.. (TCP)

### 15.2 GKE (ambicioso)
- API autoscalable
- Redis (Memorystore)
- Pub/Sub (opcional)
- Cloud SQL (SQL Server/MySQL/Postgres según elección)

### 15.3 CI/CD
- build, tests, artefact registry
- despliegue por ambientes
- migrations controladas

---

## 16. Roadmap

> **Estado real:** Fase 0, 1, 2, 3 y 4 del backend ya estan cerradas para el alcance actual.
> Los bullets siguientes quedan como roadmap historico/evolutivo.

**Fase 1 (MVP Core)**
- Worker multi-puerto + framing robusto
- Parsers iniciales + persistencia completa
- API lectura + Ops Toolbox mínimo

**Fase 2**
- API admin + (futuro) SignalR + Redis backplane/cache

**Fase 3**
- Commands bidireccionales + estados + retries + auditoría

**Fase 4**
- Outbox + bus (Pub/Sub/Streams)
- Multi-tenant + analítica + geofencing

---

## 17. Checklist de producción

- [ ] systemd con restart policy
- [ ] logs + rotación
- [ ] firewall mínimo
- [ ] timeouts TCP + límites buffer/frame
- [ ] channel bounded + backpressure
- [ ] ACK correcto por protocolo
- [ ] índices + consultas críticas probadas
- [ ] retención/archivado definido
- [ ] métricas + alertas
- [ ] runbook de incidentes
- [ ] XML docs en APIs públicas (compilación con XML opcional)

---

## 18. Anexos: payloads, simuladores y troubleshooting

### 18.1 Plantilla para payloads reales
Pega payloads con:
- timestamp, ip/puerto
- ACK enviado
- resultado del parseo

### 18.2 Simulador (idea)
Script que:
- abre socket, envía login, espera ACK
- heartbeat
- gps cada N segundos
Sirve para pruebas y carga.

### 18.3 Troubleshooting

**Reconexión constante**
- ACK incorrecto/tardío o framing roto  
- revisar `ack_sent` y latencias

**Parse_fail alto**
- encoding/delimitadores/MaxFrameBytes/concatenados

**Backlog sube**
- DB lenta o pool insuficiente  
- revisar p95 persist latency, índices, workers

---

## Apéndice A — Definiciones rápidas

- **Frame:** mensaje lógico completo del protocolo (puede llegar fragmentado/concatenado).
- **RawPacket:** frame crudo + metadatos.
- **TrackingMessage:** modelo interno normalizado.
- **ACL:** traduce protocolo externo → dominio interno.
- **Backpressure:** evitar morir cuando downstream está lento.
