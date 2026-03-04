# Postman E2E (ImpiTrack API)

## Archivos
- `ImpiTrack-E2E.postman_collection.json`
- `ImpiTrack-Local.postman_environment.json`

## Uso rapido
1. Inicia la API (`ImpiTrack.Api`) en local.
2. Importa la coleccion y el environment en Postman.
3. Selecciona el environment `ImpiTrack Local`.
4. Ejecuta la coleccion en orden (Runner).

## Notas
- El flujo crea un usuario de prueba y usa su token de verificacion retornado por `/api/auth/register`.
- El login de admin requiere que exista usuario admin en tu entorno.
- Si cambias puerto HTTPS, actualiza `baseUrl`.

## Equivalente en Scalar
- Puedes ejecutar las mismas rutas manualmente en `/scalar/v1` reutilizando los payloads de la coleccion.
