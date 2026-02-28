# AI_PLAYBOOK — IMPITrack Backend (.NET 10 Worker TCP)

## 1) Propósito
Este playbook define cómo desarrollar IMPITrack (backend) de forma incremental y “production-first”.
Enfocado en:
- Worker TCP multi-puerto
- Framing robusto
- Parsers por protocolo (ACL)
- Persistencia de raw + normalizado
- Observabilidad y seguridad operativa

**Frontend queda fuera por ahora.**

---

## 2) Regla de oro (si se incumple, es bug)
1. IO TCP async (nada de thread-per-connection).
2. Framing robusto (concatenados, incompletos, ruido).
3. Backpressure obligatorio (bounded channel/queue).
4. ACK correcto y rápido (no esperar DB salvo necesidad del protocolo).
5. Límites/timeouts configurables (MaxFrameBytes, read/idle timeouts).
6. Correlación total (SessionId + PacketId + Imei cuando exista).
7. Logging estructurado con campos mínimos (ver AGENTS.md).

---

## 3) Definiciones internas (conceptuales)
- **Frame:** mensaje lógico completo del protocolo (puede llegar fragmentado/concatenado).
- **RawPacket:** frame crudo + metadatos (remoteIp, port, timestamps, SessionId, PacketId).
- **TrackingMessage:** modelo interno normalizado (imei, gpsTimeUtc, lat/lon, speedKmh, heading, status/eventos).
- **ACL (Protocols):** traduce protocolo externo → TrackingMessage y define ACK.
- **Backpressure:** control para no morir cuando DB/parsing va más lento que TCP.

---

## 4) Fases del proyecto (0–4)

### Fase 0 — Baseline del repo (control y disciplina)
**Entregables**
- AGENTS.md (ya existe)
- AI_PLAYBOOK.md (este documento)
- Config base appsettings (TcpServerConfig: Servers/Socket/Pipeline/Security)
- Logging estructurado mínimo (correlation fields)

**Definition of Done**
- `dotnet build ImpiTrack/ImpiTrack.sln -c Debug` OK
- `dotnet run --project ImpiTrack/TcpServer/TcpServer.csproj` levanta
- Config y logs no están hardcodeados (todo por options)

---

### Fase 1 — TCP Engine “industrial” (sin protocolos complejos todavía)
**Objetivo:** que el server aguante concurrencia y tráfico real.

**Entregables**
- Listener multi-puerto (por config)
- Session manager:
  - SessionId por conexión
  - lastSeen/heartbeats
  - close reasons
- Framing base por delimitador:
  - concatenados
  - incompletos (buffer acumulativo)
  - max size (MaxFrameBytes)
- Pipeline con backpressure:
  - `Channel<RawPacket>` bounded
  - ParserWorkers + DbWorkers (aunque DB sea stub al inicio)
- Política explícita cuando la cola está llena (Wait/Drop/Disconnect)

**Definition of Done**
- Simulación de múltiples conexiones no bloquea el proceso
- Framing pasa tests básicos (concatenado e incompleto)
- Backlog de channel medible por logs/métricas (aunque sea log)
- Se generan PacketId y se loguean con SessionId

---

### Fase 2 — Protocolos (ACL) + ACK correcto
**Objetivo:** soportar protocolos iniciales de verdad (Coban + Cantrack).

**Entregables**
- Estructura de ACL:
  - IFrameDecoder por protocolo (si difiere)
  - IProtocolParser por protocolo
  - IAckStrategy por protocolo
- Coban:
  - login → ACK `LOAD`
  - heartbeat → ACK `ON`
  - gps tracker parse mínimo (imei, time, lat, lon, speed)
- Cantrack:
  - V0 login echo
  - HTBT echo
  - parse V1/V2 (mínimo viable)
- Registro por puerto → protocolo (ruta rápida)

**Definition of Done**
- Con payloads reales, parse_ok y ack_sent correctos
- Reconexión constante corregida (ACK correcto/timing)
- Protocol strings aislados en módulos de protocolo (no en engine genérico)

---

### Fase 3 — Persistencia real (raw + posiciones + eventos + sesiones)
**Objetivo:** guardar evidencia y datos normalizados con performance.

**Entregables**
- Tablas mínimas:
  - Devices, DeviceSessions, RawPackets, Positions, DeviceEvents (si aplica)
- Insert raw ALWAYS (aunque parse falle) con ParseStatus/ParseError
- Correlación PacketId/SessionId en tablas
- Índices mínimos para consultas:
  - Devices(Imei unique)
  - Positions(DeviceId, GpsTimeUtc desc)
  - RawPackets(DeviceId, ReceivedAtUtc desc)
- Idempotencia básica (hash de posición o regla definida)

**Definition of Done**
- Persistencia estable bajo carga moderada
- Latencia p95 razonable (medida en logs/metrics)
- No se pierde trazabilidad (siempre existe RawPacket para auditar)

---

### Fase 4 — Operación enterprise (seguridad + observabilidad + hardening)
**Entregables**
- Rate limit por IP (frames/min, con invalid threshold)
- Ban temporal opcional (configurable)
- Health endpoints/health signals del worker
- OpenTelemetry (counters/histograms básicos)
- Runbook mínimo (qué mirar cuando hay reconexión/backlog/parse_fail)

**Definition of Done**
- Alertas accionables (aunque sea en logs) para:
  - backlog alto
  - parse_fail ratio alto
  - reconexiones frecuentes por IP
  - persist latency p95 alta

---

## 5) Checklist por PR (obligatorio)
- [ ] Cambios alineados con AGENTS.md (TCP guardrails)
- [ ] Build proof: `dotnet build ImpiTrack/ImpiTrack.sln -c Debug`
- [ ] No hardcode de límites/timeouts/puertos (todo en options)
- [ ] Logs estructurados con SessionId y PacketId (y Imei si aplica)
- [ ] Framing no rompe concatenados/incompletos (si se tocó)
- [ ] ACK correcto y rápido (si se tocó protocolo)
- [ ] Manejo de cola llena definido (si se tocó pipeline)
- [ ] Tests añadidos/actualizados cuando aplique (xUnit si existe)
- [ ] PR incluye payload/log snippet si se tocó protocolo

---

## 6) Señales rápidas de problemas (para debug)
- **Reconexión constante:** ACK incorrecto/tardío o framing roto.
- **parse_fail alto:** delimitador/encoding/max size/concatenados.
- **backlog sube:** DB lenta o workers insuficientes; revisar p95 persist latency e índices.
- **memoria sube:** buffer acumulativo sin MaxFrameBytes o sin recorte.

---

## 7) Próximo paso recomendado
Primero cerrar Fase 1: TCP engine + framing + channel bounded + correlación + logging.
Luego recién protocolos (Fase 2).