# CONVENTIONS

## Platform conventions
- All projects target `.NET 10` (`net10.0`)
- Nullable enabled
- Implicit usings enabled

## Code style and naming
- C# standard style (4 spaces, braces on new lines)
- `PascalCase` for types/methods
- `camelCase` for locals/parameters
- `_camelCase` for private fields

## API and error shape
- API responses use shared envelope/problem pattern
- Validation errors transformed in `ApiBehaviorOptions.InvalidModelStateResponseFactory`
- Global exception and status code handling through middleware/status page mapping

## Security and auth conventions
- JWT bearer auth with policies
- Admin-only operations guarded with `[Authorize(Policy = "Admin")]`
- Identity options enforce confirmed email + password constraints

## TCP ingestion conventions
- Async I/O and background workers
- Correlation-first logs (`sessionId`, `packetId`, `imei`, protocol/port)
- Bounded queues/channels with configurable capacity/full-mode
- Fast ACK strategy per protocol before downstream persistence where possible

## Documentation conventions
- XML comments are widely used in public classes/methods
- Operational docs are maintained in `ImpiTrack/Docs`

## Git and release conventions
- Conventional commit messages are used (`feat`, `fix`, `docs`, `chore`, etc.)
- Small scoped commits are preferred over mixed changes
