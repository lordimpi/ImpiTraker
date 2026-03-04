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
- parse fail ratio
- ack latency p95
- backlog inbound/raw
- persist latency p95
- publish fail/retry/dlq

## Apagado
```powershell
docker compose -f .\ImpiTrack\Observability\docker-compose.observability.yml down
```
