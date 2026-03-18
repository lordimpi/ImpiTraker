Role: adr  
Status: active  
Owner: backend-maintainers  
Last Reviewed: 2026-03-15

# ADR-001: Habilitacion de Identity sobre PostgreSQL

Canonical scope note: this ADR records a decision boundary. For the live backend/runtime snapshot, also consult [`CURRENT_STATE.md`](CURRENT_STATE.md).

## Estado
Aceptado (planificado, no habilitado de forma general).

## Contexto
- El backend soporta negocio SQL dual (`SqlServer` y `Postgres`) en `ImpiTrack.DataAccess`.
- `ImpiTrack.Api` usa ASP.NET Identity y hoy opera estable en:
  - `IdentityStorage:Provider=SqlServer`
  - `IdentityStorage:Provider=InMemory`
- Para `IdentityStorage:Provider=Postgres` se permite bootstrap inicial en `Development` con `EnsureCreated`, pero aun no existe un flujo maduro de migraciones de Identity por proveedor para ambientes no-dev.

## Decision
1. Mantener Identity Postgres en estado controlado:
   - permitido para bootstrap en `Development`,
   - no considerado listo para rollout general hasta cumplir criterios de salida.
2. No mezclar scripts SQL de negocio con migraciones EF de Identity.
3. Establecer ruta de habilitacion por etapas con validacion automatizable.

## Criterios de salida para habilitacion general
1. Estrategia de migraciones Identity definida y versionada para Postgres (equivalente operativa a SQL Server).
2. Suite minima verde sobre Postgres para flujos:
   - register
   - verify-email
   - login
   - refresh token
   - roles/admin bootstrap
3. Smoke de proveedor (`Run-ProviderSmoke.ps1`) estable en Postgres en CI.
4. Runbook de rollback documentado (volver a `SqlServer` o `InMemory` sin perdida de acceso operativo).

## Plan de ejecucion recomendado
1. Definir y acordar el modelo de versionado de esquema Identity para Postgres.
2. Implementar pipeline de migracion reproducible en entorno efimero.
3. Ejecutar pruebas de regresion de Auth contra Postgres.
4. Habilitar por feature flag en entorno controlado.
5. Promover a rollout general una vez completados los criterios.

## Consecuencias
- Se evita un rollout parcial de Identity Postgres que pueda romper autenticacion.
- El equipo conserva compatibilidad actual y una ruta clara de habilitacion.
- Se explicita la deuda tecnica como decision gestionada y no como bloqueo indefinido.
