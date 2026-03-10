# Plan de Ejecucion Post-MVP Backend: Mapa y Telemetria

## 1. Objetivo
Definir la ejecucion post-MVP del backend para habilitar monitoreo funcional read-only sobre dispositivos GPS ya vinculados. Esta fase expone APIs publicas y seguras para resumen de telemetria, ultima posicion, historial de posiciones y eventos recientes.

Base real del sistema actual:
- ya existen `positions` y `device_events`,
- ya existe ownership por usuario y admin,
- ya existen `ApiResponse<T>`, controladores `me/admin`, SQL Server e InMemory,
- no existen aun endpoints publicos de telemetria funcional.

Fuera de alcance en este corte:
- tiempo real,
- alertas avanzadas,
- mapa global admin,
- dashboards agregados,
- optimizacion avanzada con cache/preagregados.

## 2. Criterios fijos
- Backend only.
- Read-only.
- Polling como modelo de consumo.
- Usuario solo consulta IMEIs vinculados a su cuenta.
- Admin consulta IMEIs dentro del contexto de `userId`.
- Si no existe vinculo activo: `404 device_binding_not_found`.
- Si no hay posiciones o eventos: `200` con lista vacia.
- Defaults:
  - ventana: ultimas 24h,
  - `limit` posiciones: 500,
  - `limit` eventos: 100.
- Orden por defecto:
  - posiciones: `occurredAtUtc desc`,
  - eventos: `receivedAtUtc desc`.

## 3. Contratos objetivo
- `TelemetryDeviceSummaryDto`
  - `imei`
  - `boundAtUtc`
  - `lastSeenAtUtc`
  - `activeSessionId`
  - `protocol`
  - `lastMessageType`
  - `lastPosition`
- `LastKnownPositionDto`
  - `occurredAtUtc`
  - `receivedAtUtc`
  - `gpsTimeUtc`
  - `latitude`
  - `longitude`
  - `speedKmh`
  - `headingDeg`
  - `packetId`
  - `sessionId`
- `DevicePositionPointDto`
- `DeviceEventDto`
  - `eventId`
  - `occurredAtUtc`
  - `receivedAtUtc`
  - `eventCode`
  - `payloadText`
  - `protocol`
  - `messageType`
  - `packetId`
  - `sessionId`

## 4. Fases de ejecucion

### Fase 0. Aterrizaje tecnico
Objetivo:
- Separar telemetria funcional del toolbox `Ops`.

Entregables:
- Documento de contratos y defaults.
- Nueva abstraccion de lectura, por ejemplo `ITelemetryQueryRepository`.
- Decision de no reutilizar `IOpsRepository` para UI funcional.

Definition of Done:
- El contrato queda estable para frontend.
- Las reglas de ownership y defaults quedan fijadas.

### Fase 1. Data access de telemetria
Objetivo:
- Leer telemetria real desde `positions`, `device_events`, `user_devices`, `devices` y `device_sessions`.

Entregables:
- Consultas para:
  - resumen por dispositivo,
  - ultima posicion por IMEI,
  - historial de posiciones,
  - eventos recientes,
  - validacion de vinculo activo `userId + imei`.
- Soporte SQL Server e InMemory.
- Resolucion de `lastSeenAtUtc`, `activeSessionId`, `protocol` y `lastMessageType` usando fuentes reales existentes.

Definition of Done:
- Las consultas devuelven datos reales.
- Un IMEI sin data devuelve estado vacio, no error.
- La validacion de ownership queda reutilizable.

### Fase 2. Application y seguridad
Objetivo:
- Encapsular reglas de acceso para usuario final y admin.

Entregables:
- Servicio de telemetria para `/api/me/telemetry/*`.
- Servicio de telemetria para `/api/admin/users/{userId}/telemetry/*`.
- Resolucion centralizada de:
  - usuario autenticado,
  - ownership del IMEI,
  - contexto admin por `userId`.

Definition of Done:
- No hay fuga de datos entre usuarios.
- Admin solo consulta en contexto del usuario solicitado.
- `device_binding_not_found` sale desde una ruta consistente.

### Fase 3. Endpoints HTTP
Objetivo:
- Publicar la superficie HTTP consumible por frontend.

Entregables:
- `GET /api/me/telemetry/devices`
- `GET /api/me/telemetry/devices/{imei}/positions?from&to&limit`
- `GET /api/me/telemetry/devices/{imei}/events?from&to&limit`
- `GET /api/admin/users/{userId}/telemetry/devices`
- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/positions?from&to&limit`
- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/events?from&to&limit`
- XML docs en espanol.
- `ProducesResponseType` y `ApiResponse<T>` alineados con el resto de la API.

Definition of Done:
- Los 6 endpoints aparecen en Scalar.
- Responden con contratos estables y datos reales.
- `from/to/limit` usan defaults y clamps definidos.

### Fase 4. Pruebas, indices y hardening
Objetivo:
- Cerrar calidad, eficiencia y no regresion.

Entregables:
- Pruebas de:
  - ownership usuario,
  - acceso admin,
  - `404 device_binding_not_found`,
  - listas vacias,
  - filtros por ventana,
  - limites,
  - ordenamiento por defecto.
- Revision de indices para polling moderado:
  - `positions(imei, gps_time_utc)` o equivalente efectivo para ultima posicion/historial,
  - `device_events(imei, received_at_utc)`,
  - indices adicionales si el query real lo requiere.
- Notas de performance y limites operativos.

Definition of Done:
- `dotnet build ImpiTrack\\ImpiTrack.sln -c Debug`
- `dotnet test ImpiTrack\\ImpiTrack.sln -c Debug --no-build`
- Consultas listas para polling moderado sin degradar operaciones base.

## 5. Endpoints esperados al cerrar este corte

### Usuario autenticado
- `GET /api/me/telemetry/devices`
- `GET /api/me/telemetry/devices/{imei}/positions`
- `GET /api/me/telemetry/devices/{imei}/events`

### Admin
- `GET /api/admin/users/{userId}/telemetry/devices`
- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/positions`
- `GET /api/admin/users/{userId}/telemetry/devices/{imei}/events`

## 6. Casos de prueba obligatorios
- Usuario con 2 IMEIs vinculados obtiene ambos en su resumen.
- Usuario no puede consultar un IMEI ajeno.
- Admin puede consultar un IMEI solo dentro del `userId` correcto.
- IMEI vinculado sin posiciones retorna `lastPosition = null`.
- Historial de posiciones respeta `from/to/limit`.
- Eventos respetan `from/to/limit`.
- Posiciones ordenadas por `occurredAtUtc desc`.
- Eventos ordenados por `receivedAtUtc desc`.
- IMEI sin vinculo activo retorna `404 device_binding_not_found`.

## 7. Backlog formal despues de este corte
- Alertas y timeline enriquecido.
- Tiempo real con SignalR o WebSockets.
- Dashboard operativo y ejecutivo.
- Mapa global administrativo.
- Optimizacion avanzada de consultas, cache y preagregados.

## 8. Ruta de uso como base de conocimiento
- Archivo: `ImpiTrack/Docs/POST_MVP_BACKEND_EXECUTION_PLAN.md`
- Este documento debe mantenerse actualizado cada vez que una fase post-MVP se cierre o cambie de alcance.
