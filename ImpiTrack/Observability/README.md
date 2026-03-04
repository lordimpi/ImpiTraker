# IMPITrack Observability Stack (Local)

Este directorio contiene un stack local de observabilidad para metricas:
- OpenTelemetry Collector
- Prometheus
- Grafana

## Requisitos
- Docker Desktop activo.

## Arranque
Desde la raiz del repo:

```powershell
docker compose -f .\ImpiTrack\Observability\docker-compose.observability.yml up -d
```

Servicios:
- Collector OTLP: `http://localhost:4317` (gRPC), `http://localhost:4318` (HTTP)
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000`

Credenciales Grafana:
- user: `admin`
- password: `admin`

## Configuracion en API/Worker
Habilita en `appsettings.Development.json`:

```json
"OpenTelemetry": {
  "Enabled": true,
  "OtlpEndpoint": "http://127.0.0.1:4317"
}
```

## Dashboard provisionado
Grafana carga automaticamente:
- `IMPITrack TCP Overview`

Paneles incluidos:
- conexiones activas
- parse fail ratio por protocolo/puerto
- ack latency p95 por protocolo/puerto
- backlog inbound/raw por protocolo/puerto
- persist latency p95 por protocolo/puerto
- publish fail/retry/dlq por protocolo/puerto
- raw queue drops (5m)
- event publish latency p95

## Alertas SLO
Prometheus carga reglas desde:
- `ImpiTrack/Observability/prometheus-rules.yml`

Alertas activas:
- `ImpiTrackParseFailRatioHigh`
- `ImpiTrackAckP95High`
- `ImpiTrackPersistP95High`
- `ImpiTrackInboundBacklogHigh`
- `ImpiTrackRawQueueDropsDetected`

## Apagado
```powershell
docker compose -f .\ImpiTrack\Observability\docker-compose.observability.yml down
```
