# ParkWise: conventions for Claude

Portfolio project: car parking management. The owner is a junior dev who must explain every part in interviews.

## Working agreement
- One phase at a time. After each phase: what was built, why, how to run/test, a suggested commit message. Then stop and wait.
- Explain non-obvious things in chat (DI lifetimes, EF config, concurrency, auth, RxJS), not in long code comments.
- Ask with 2-3 options and a recommendation when a real trade-off exists.
- Every phase must build, pass tests, and run. Never leave broken code.

## Stack
.NET 10, ASP.NET Core Web API, EF Core, SQL Server, Identity + JWT (access + rotating refresh), SignalR, Serilog, FluentValidation.
Angular (standalone, signals, typed reactive forms, Material) in `web/`. xUnit + NSubstitute + Testcontainers.

## Architecture rules
- Dependencies point inward: Api -> Application/Infrastructure; Infrastructure -> Application; Application -> Domain; Domain -> nothing.
- Business rules live in Domain/Application, never in controllers. Controllers bind, call one service, map to HTTP.
- No MediatR. Plain application services. `Result<T>` for expected business failures; exceptions only for the unexpected.
- DTOs for all API input/output; never expose entities.
- Identity (`ApplicationUser`) lives in Infrastructure; Domain refers to users by `Guid UserId`.
- Use `TimeProvider`, never `DateTime.UtcNow` directly.
- Money is `decimal`. All timestamps UTC; the frontend converts to local time.
- Errors are RFC 7807 `ProblemDetails` via the global handler.
- Concurrency: filtered unique indexes (one active session per spot / per vehicle) + `rowversion` on Spot for reservation overlap.
- No secrets in git. Config via appsettings + environment variables; `.env` is git-ignored. Local `dotnet run` reads `Jwt:Key` and `Seed:Password` from user-secrets (`dotnet user-secrets set ... --project src/ParkWise.Api`); Docker reads `JWT_KEY` / `SEED_PASSWORD` from `.env`.
- Auth: access JWT (15 min, in memory on the client) + opaque refresh token (7 days) in an HttpOnly, SameSite=Strict cookie scoped to `/api/v1/auth`. Refresh tokens are stored as SHA-256 hashes, rotated on every use; replaying a used token revokes its whole family. Self-registration only creates Drivers.
- Controllers inherit `ApiControllerBase` (`FromError`, `ToActionResult`) and never validate by hand: `ValidationFilter` runs FluentValidation validators. Unexpected exceptions go through `GlobalExceptionHandler`.

## Conventions
- File-scoped namespaces, nullable enabled, warnings are errors.
- Package versions live in `Directory.Packages.props` (central package management); no `Version=` in csproj.
- Tests: `*.Domain.Tests` (pure logic), `*.Application.Tests` (NSubstitute), `*.Infrastructure.Tests` (EF model rules, no DB), `*.Api.IntegrationTests` (WebApplicationFactory + Testcontainers SQL Server; needs Docker; one shared container via `ApiFactory`, tests isolate with unique data). On macOS Docker Desktop run with `DOCKER_HOST=unix://$HOME/.docker/run/docker.sock`.
- Entities have private setters and enforce their own invariants (throw `DomainException`); EF config lives in `Infrastructure/Persistence/Configurations`.
- After changing the model add a migration (a test fails if you forget):
  `dotnet tool run dotnet-ef migrations add <Name> -p src/ParkWise.Infrastructure -s src/ParkWise.Api -o Persistence/Migrations`
- With `Database:MigrateOnStartup=true` the API migrates + seeds on start (set in docker-compose).
- API routes under `/api/v1`, plural nouns. List endpoints support page/pageSize/sort/filters.
- Commits: imperative, one phase = one logical commit (or a few small ones).

## Commands
```
dotnet build
dotnet test
docker compose up --build      # needs .env (copy .env.example)
```
Local SDK note: if `dotnet` is not on PATH, it may be in `~/.dotnet`.
