Role: history  
Status: historical  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15

# PRD Backend: Recorridos Vehiculares V1

Historical note: this PRD is preserved for product context. It is not the current backend/runtime truth.

## Resumen

La telemetria actual ya permite:

- ver resumen del dispositivo,
- consultar ultima posicion,
- listar posiciones por ventana temporal,
- listar eventos recientes.

Eso alcanza para puntos e historico, pero no modela un recorrido como entidad funcional.

La V1 agregara recorridos calculados en backend para que frontend consuma viajes ya construidos, sin inferirlos por su cuenta.

## Objetivo

Exponer recorridos vehiculares legibles para usuario y admin, construidos desde `positions` validas ya persistidas.

## Decisiones fijadas para V1

- No se crea tabla `trips`.
- Los recorridos se calculan **on-read**.
- La deteccion de inicio/fin usa la regla `movement_gap_v1`.
- Frontend no calcula recorridos.
- Se mantiene `ApiResponse<T>` como envelope.

## Endpoints requeridos

### Usuario autenticado

- `GET /api/me/telemetry/devices/{imei}/trips?from&to&limit`
- `GET /api/me/telemetry/devices/{imei}/trips/{tripId}`

### Admin

- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/trips?from&to&limit`
- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/trips/{tripId}`

## Contratos minimos

### TripSummaryDto

- `tripId`
- `imei`
- `startedAtUtc`
- `endedAtUtc`
- `pointCount`
- `maxSpeedKmh`
- `avgSpeedKmh`
- `startPosition`
- `endPosition`

### TripDetailDto

- `tripId`
- `imei`
- `startedAtUtc`
- `endedAtUtc`
- `pointCount`
- `maxSpeedKmh`
- `avgSpeedKmh`
- `pathPoints`
- `startPosition`
- `endPosition`
- `sourceRule`

### PositionPointDto

Puede reutilizar el shape actual de `DevicePositionPointDto`:

- `occurredAtUtc`
- `receivedAtUtc`
- `gpsTimeUtc`
- `latitude`
- `longitude`
- `speedKmh`
- `headingDeg`
- `packetId`
- `sessionId`

## Regla de construccion: `movement_gap_v1`

La segmentacion de recorridos se define asi:

### 1. Puntos candidatos

Se usan posiciones validas del IMEI dentro de la ventana solicitada:

- ordenadas por `occurredAtUtc asc`,
- con latitud y longitud validas,
- provenientes de `positions`.

### 2. Punto en movimiento

Un punto se considera parte de movimiento si cumple al menos una:

- `speedKmh >= 5`
- o desplazamiento >= `100 m` respecto al punto valido anterior

### 3. Inicio del recorrido

El recorrido inicia en el primer punto que cumpla la regla de movimiento.

### 4. Continuidad del recorrido

Los puntos consecutivos pertenecen al mismo recorrido mientras el gap entre ellos sea:

- `<= 10 minutos`

### 5. Fin del recorrido

El recorrido termina en el ultimo punto antes de un gap:

- `> 10 minutos`

### 6. Recorrido en curso

Si el ultimo punto del recorrido esta dentro de esos `10 minutos` respecto a `DateTimeOffset.UtcNow`, el viaje sigue abierto:

- `endedAtUtc = null`

### 7. Filtro de ruido

No se devuelven recorridos de un solo punto:

- `pointCount >= 2`

### 8. Regla reportada al cliente

`sourceRule = "movement_gap_v1"`

## Defaults y limites

- ventana por defecto: ultimas `24h`
- `limit` por defecto: `50`
- `limit` maximo: `200`
- backend puede usar hasta `5000` puntos candidatos por consulta para construir recorridos

Si no hay recorridos en la ventana:

- responder `200` con lista vacia

## Reglas de seguridad y errores

- si el IMEI no pertenece al usuario autenticado:
  - `404 device_binding_not_found`
- si admin consulta un usuario inexistente:
  - `404 user_not_found`
- si el `tripId` no existe para ese IMEI/contexto:
  - `404 trip_not_found`

## Criterios de aceptacion

### Caso 1. Dispositivo con varios recorridos

- la lista devuelve multiples items
- cada item tiene inicio, fin y metricas basicas

### Caso 2. Recorrido en curso

- `endedAtUtc = null`
- el detalle devuelve `pathPoints` acumulados

### Caso 3. Ventana sin recorridos

- lista vacia
- sin error

### Caso 4. Seguridad

- usuario no consulta IMEIs ajenos
- admin si puede dentro del contexto del usuario correcto

## Casos de prueba recomendados

- varios bloques de puntos separados por gap > `10 min`
- recorrido en curso dentro de la ventana
- puntos sin velocidad pero con desplazamiento >= `100 m`
- puntos con velocidad < `5` y sin desplazamiento suficiente
- ventana vacia
- `tripId` inexistente
- ownership usuario
- contexto admin correcto/incorrecto

## No objetivos

- no tiempo real
- no clustering
- no dashboard analitico
- no persistencia fisica de recorridos en tabla
- no regla hibrida con ignicion en esta fase

## Relacion con backlog

Las mejoras futuras de recorridos y telemetria posterior a esta V1 deben documentarse en:

- `ImpiTrack/Docs/TELEMETRY_BACKLOG_PRD.md`
