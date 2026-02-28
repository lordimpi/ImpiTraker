# IMPITrack — Documentación Técnica (Arquitectura + DDD Light + Protocolos + Roadmap)

> **Stack objetivo (2026):**  
> **Backend:** .NET 10 (**Worker TCP** + **Web API**), SignalR, Serilog, OpenTelemetry  
> **Frontend:** Angular **21**  
> **Data:** SQL Server + PostgreSQL (según módulo / entorno)  
> **Infra:** GCP (Compute Engine o GKE), NGINX, Redis (backplane + cache), opcional Pub/Sub  
>
> **Propósito:** Plataforma empresarial para **ingestión, normalización, almacenamiento y visualización** de telemetría GPS **multi-dispositivo** y **multi-protocolo**, con **tiempo real** y trazabilidad de payloads.

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

**IMPITrack** recibe mensajes GPS desde dispositivos vía **GPRS/TCP**, valida identidad (IMEI), **normaliza** (parseo por protocolo), **persiste** (posiciones/eventos/raw) y expone al frontend (mapas + dashboards) con **actualización en tiempo real**.

### Objetivos no funcionales

- **Alta concurrencia:** miles de conexiones TCP simultáneas (sin *thread-per-connection*).
- **Backpressure:** el sistema debe degradar con gracia si DB se pone lenta (colas/buffers).
- **Escalabilidad por configuración:** agregar puertos/protocolos sin tocar el core.
- **Trazabilidad:** preservar *raw payload* y correlación por sesión/paquete.
- **Modularidad:** parsers por protocolo como ACL (Anti-Corruption Layer).
- **Seguridad:** IMEI autorizado (TCP) + JWT/roles (API).
- **Operabilidad:** logs estructurados, métricas, health checks, alertas.

---

## 2. Alcance y principios

### 2.1 Alcance (MVP → Enterprise)
- Ingestión TCP multi-puerto multi-protocolo
- Parsers Coban/TK y Cantrack/G06L (base inicial)
- Persistencia de posiciones, eventos, sesiones y raw
- Web API lectura (y luego administración)
- Frontend Angular para mapa + realtime
- Observabilidad completa desde el día 1

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
      ├──► SignalR (Realtime al Frontend Angular)
      ├──► Auth (JWT)
      ├──► Admin/Devices/Queries
      └──► Observabilidad (Serilog + OTel)
```

### 3.2 Reglas de acoplamiento
- Worker TCP **NO** debe llamar a la Web API para validar IMEI o persistir.  
  - Validación IMEI preferida: **cache + DB** (evita caer si API cae).
- La Web API **consume DB** (y opcionalmente bus) para “empujar” realtime.

### 3.3 Escalado
- Worker TCP: horizontal por **puerto** (preferido) o por **LB TCP** (avanzado).
- Web API: horizontal libre.
- SignalR: requiere **backplane** (Redis) si hay múltiples instancias.

---

## 4. Decisiones clave

### 4.1 Monolito modular → microservicios cuando duela
Arranque como **modular monolith** (proyectos separados), listo para:
- Worker TCP (servicio independiente)
- Web API (servicio independiente)
- Event bus para desacoplar ingestión/persistencia/realtime

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
- Auth JWT, endpoints, SignalR hub, swagger
- Queries optimizadas

6) **IMPITrack.Frontend**
- Angular 21
- Mapa, dashboards, realtime, admin

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

### 9.1 Endpoints propuestos

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

### 11.3 `RawPackets`
- `PacketId` (PK uuid)
- `SessionId` (FK)
- `DeviceId` (nullable)
- `Protocol`
- `ReceivedAtUtc`
- `PayloadText` o `PayloadBytes`
- `PayloadHash` (opcional)
- `ParseStatus`, `ParseError`

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
- CORS estricto

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

**Fase 1 (MVP Core)**
- Worker multi-puerto + framing robusto
- Parsers iniciales + persistencia completa
- API lectura + frontend mapa básico

**Fase 2**
- API admin + SignalR realtime + Redis backplane/cache

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
