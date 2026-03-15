# PRD de Pendientes: Telemetria y Recorridos

## Resumen

Este documento centraliza los pendientes y mejoras futuras del backend de telemetria de IMPITrack.

Su objetivo es separar con claridad:

- lo que ya entra en la V1,
- lo que queda deliberadamente fuera,
- y las condiciones para retomar cada mejora en iteraciones futuras.

## Estado actual fijado

- Ya existen endpoints de telemetria para resumen, ultima posicion, historial de posiciones y eventos.
- La V1 de recorridos se implementara como calculo **on-read** desde `positions`.
- No se crea tabla `trips` en esta fase.
- La regla base de segmentacion sera `movement_gap_v1`.
- Frontend no debe inferir recorridos por su cuenta.

## Pendientes priorizados

### 1. Persistencia de recorridos en BD

**Problema**
Calcular recorridos en cada lectura puede encarecer consultas cuando crezca el volumen de puntos.

**Fuera de V1 porque**
La regla de negocio aun puede cambiar y no conviene materializar una entidad inestable.

**Condicion para retomarlo**
- alto volumen de `positions`,
- necesidad de reportes por recorrido,
- o requerimiento de tiempos de respuesta mas bajos.

### 2. Regla hibrida con ignicion/eventos

**Problema**
Movimiento + inactividad es suficiente para V1, pero no siempre representa con precision viajes reales.

**Fuera de V1 porque**
Hoy no existe una senal de ignicion confiable y uniforme para todas las marcas.

**Condicion para retomarlo**
- eventos de ignicion consistentes por protocolo,
- o normalizacion confiable en `device_events`.

### 3. Distancia recorrida y metricas enriquecidas

**Problema**
La V1 cubre velocidad maxima/promedio, pero no distancia, tiempo detenido ni analitica mas fina.

**Fuera de V1 porque**
Primero hay que validar la segmentacion de recorridos.

**Condicion para retomarlo**
- recorridos V1 validados por negocio,
- necesidad de reportes operativos o ejecutivos.

### 4. Recorridos en curso mas robustos

**Problema**
El criterio de viaje abierto puede requerir refinamiento si hay gaps irregulares o telemetria intermitente.

**Fuera de V1 porque**
Primero se necesita uso real para calibrar timeout y ruido.

**Condicion para retomarlo**
- falsos positivos/falsos cierres detectados en QA o produccion.

### 5. Replay y analitica por recorrido

**Problema**
No existe todavia experiencia de replay, comparacion de recorridos ni agregaciones historicas.

**Fuera de V1 porque**
No es necesario para habilitar el primer mapa funcional.

**Condicion para retomarlo**
- requerimiento explicito de UX o reporting.

### 6. Optimizacion de consultas y volumen

**Problema**
Ventanas grandes o dispositivos con muchos puntos pueden requerir optimizaciones adicionales.

**Fuera de V1 porque**
El costo actual todavia no justifica cache, preagregados o materializacion.

**Condicion para retomarlo**
- degradacion visible de performance,
- crecimiento sostenido del uso de telemetria.

## Disparadores para pasar de V1 a V2

Se recomienda pasar a una V2 persistida cuando ocurra al menos uno de estos escenarios:

- el calculo on-read de recorridos se vuelva costoso,
- la regla de segmentacion quede estable,
- se necesiten dashboards o exportaciones por recorrido,
- se requiera reprocesar historico por cambios de criterio,
- o negocio pida trazabilidad fuerte de viajes como entidad propia.

## Decisiones fijadas por ahora

- Backend calcula recorridos.
- Frontend consume recorridos ya construidos.
- No se implementa tiempo real en esta fase.
- No se implementa clustering ni dashboard analitico.
- No se crea tabla `trips` en V1.

## Riesgos conocidos

- posiciones tardias pueden cambiar la percepcion de inicio/fin,
- gaps de telemetria pueden partir recorridos reales,
- velocidades faltantes o ruidosas afectan metricas,
- distintas marcas pueden requerir reglas especializadas mas adelante.

## Uso de este documento

Este PRD debe actualizarse cada vez que una mejora futura:

- quede aprobada para ejecucion,
- cambie de prioridad,
- o deje de ser pendiente porque paso a una fase activa.
