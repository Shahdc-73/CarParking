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
- Feature services (lots, spots, vehicles, audit...) live in Application and use `IAppDbContext` (EF Core `DbSet`s, implemented by `AppDbContext`). Application knows EF Core abstractions, not SQL Server. Identity-bound services (auth, users) live in Infrastructure.
- Audit: call `IAuditLogger.Log(...)` BEFORE the single `SaveChangesAsync`; it only adds the row, so the change and its audit entry commit in one transaction. Audit every admin mutation.
- Deleting data that has parking history is blocked (409); deactivate / take out of service instead. Spot type cannot change while in use or with pending reservations.
- Someone else's resource returns 404, not 403 (vehicles), so ids can't be probed. Ownership rules live in one query helper per service.
- Fees: `FeeCalculator` (Domain) is pure. The first `FreeMinutes` are DEDUCTED (not a grace period), the rest is billed per started hour, and each 24h block from check-in is capped at `DailyCap` (midnight is irrelevant). Lost ticket = flat fee that replaces the normal fee. Pricing is versioned (append a row, never edit); the rule in force at check-out time applies, and receipts store a frozen breakdown.
- Races (check-in/check-out): the database arbitrates (unique indexes + spot rowversion); services catch `DbUpdateException`, `ChangeTracker.Clear()`, and retry (check-in) or return the winner's receipt (check-out, idempotent). Never use explicit transactions here (EnableRetryOnFailure); one `SaveChangesAsync` per unit of work.
- Integration tests control time with `factory.Clock.TravelBy(...)`: log in BEFORE travelling (JWT validation uses the real clock). Pricing tests only change Motorcycle pricing so other fee tests stay stable.
- Reservations: booking picks a specific spot; overlap is prevented by touching the spot row AND the vehicle row (both have a rowversion) so the loser of a race retries. A spot is `Reserved` only during the hold window [start, start+15min); the vehicle may check in from 15 min before start. Min booking 30 min, max 24 h, max 30 days ahead. The background job (`ReservationExpiryWorker` -> `IReservationMaintenance`) is state-based and idempotent: expire missed holds, start holds, reassign/cancel when the spot is unavailable. Hosted services are singletons: use `IServiceScopeFactory` to get scoped services per run. The job is disabled in tests (`Reservations:ExpiryJob:Enabled=false`); tests call `Arrange.RunMaintenanceAsync()` after `Clock.TravelBy(...)`.
- Hardening (`Api/Infrastructure/HardeningExtensions.cs`): login/register/refresh are rate limited per client IP (`RateLimiting:Auth:PermitLimit`/`WindowSeconds`, default 10/60 s, 429 + `Retry-After` + ProblemDetails). The real IP comes from `X-Forwarded-For` ONLY when the TCP peer is in `ForwardedHeaders:KnownNetworks` (docker-compose sets the Docker range); never trust it from anyone else. `UseForwardedHeaders` must stay first in the pipeline. The API sends its own `nosniff`/`no-referrer`, and `no-store` on `/api/v1/auth`; nginx adds no headers on `/api` and `/hubs`. Tests raise the limit to 100000 in `ApiFactory`; `HardeningTests` use a low-limit host with a fake peer address. CI fails on vulnerable NuGet packages and `npm audit --audit-level=high`.
- Logging: Serilog sinks are configured in `appsettings.json` (`Serilog:WriteTo`). Without a sink the app logs NOTHING, so never remove it. Never log tokens, passwords or request bodies.
- Entity ids are assigned in code (Guid v7) and configured `ValueGeneratedNever`; without that EF treats new children of tracked parents as existing rows.
- Identity (`ApplicationUser`) lives in Infrastructure; Domain refers to users by `Guid UserId`.
- Use `TimeProvider`, never `DateTime.UtcNow` directly.
- Money is `decimal`. All timestamps UTC; the frontend converts to local time.
- Errors are RFC 7807 `ProblemDetails` via the global handler.
- Concurrency: filtered unique indexes (one active session per spot / per vehicle) + `rowversion` on Spot for reservation overlap.
- No secrets in git. Config via appsettings + environment variables; `.env` is git-ignored. Local `dotnet run` reads `Jwt:Key` and `Seed:Password` from user-secrets (`dotnet user-secrets set ... --project src/ParkWise.Api`); Docker reads `JWT_KEY` / `SEED_PASSWORD` from `.env`.
- Auth: access JWT (15 min, in memory on the client) + opaque refresh token (7 days) in an HttpOnly, SameSite=Strict cookie scoped to `/api/v1/auth`. Refresh tokens are stored as SHA-256 hashes, rotated on every use; replaying a used token revokes its whole family. Self-registration only creates Drivers.
- Controllers inherit `ApiControllerBase` (`FromError`, `ToActionResult`) and never validate by hand: `ValidationFilter` runs FluentValidation validators. Unexpected exceptions go through `GlobalExceptionHandler`.

- Real-time: SignalR hub `/hubs/availability` sends ONLY `LotChanged(lotId)` (never data: board data is role-redacted); clients re-fetch through the normal API. Groups `lot:{id:N}` and `lots`. JWT reaches the hub via `access_token` query string, accepted ONLY for `/hubs` paths. `IAvailabilityNotifier` is called AFTER the successful save, must never throw, and defaults to a null implementation (tests/other hosts).
- Reports (admin only): computed in memory from sessions/payments with the viewer's fixed `utcOffsetMinutes` (no DST modelling), max 92 days, zero-filled days, 24 peak-hour buckets; occupancy rate excludes out-of-service spots.

## Frontend (web/)
- Angular 22, standalone + signals + zoneless, `OnPush`, new control flow (`@if/@for`), typed reactive forms (`NonNullableFormBuilder`), Angular Material (M3 theme in `styles.scss`), Vitest via `ng test`.
- Folders: `core/` (api, auth), `layout/`, `shared/`, `features/<area>/` (auth, parking, gate, reservations, admin, errors). Routes are lazy (`loadComponent`).
- Auth: access token ONLY in memory (never web storage); refresh cookie is HttpOnly. `authInterceptor` adds the bearer to `/api/v1` calls only, and on 401 refreshes once (single-flight, `AuthService.refresh()` shares one request) and replays. `provideAppInitializer` restores the session on reload. Guards are UX only; the API enforces roles.
- `returnUrl` must go through `safeReturnUrl` (open-redirect protection). API errors go through `toApiError` (never show raw server text for 5xx).
- The browser talks to ONE origin: `ng serve` proxies `/api` to localhost:5080 (`proxy.conf.json`), nginx does the same in Docker. No CORS, cookie stays same-site.
- Production HTML must stay CSP-clean (no inline scripts/handlers): `npm run build:prod` runs `scripts/verify-csp.mjs`; keep `styles.inlineCritical=false`.
- Spot colours are CSS tokens in `styles.scss` (`--pw-free-*` green, `--pw-occupied-*` red, `--pw-reserved-*` amber, via `light-dark()`); always shown with an icon AND a word. The lot board (`/lots/:id`, `GET /lots/{id}/board`) ticks timers from ONE signal, corrects for browser clock skew with the server's `generatedAtUtc`, refreshes every 30 s (not while the tab is hidden) and clears its timers on destroy. The server redacts occupancy (plate, times, fee) for anyone except staff and the vehicle's owner.
- Live updates: `core/realtime` (`LiveUpdates` root service: one connection, ref-counted group joins, re-join + resync after reconnect, backoff, 401 -> `auth.refresh()`; tests inject a fake via `REALTIME_CONNECTION_FACTORY`). `BoardFeed` reloads on `LotChanged` (300 ms debounce) and polls only as a fallback/safety net; `LiveBadge` says Live / Reconnecting / Refreshes every 30 seconds. nginx and `proxy.conf.json` must forward `/hubs` with WebSocket upgrade.
- Feature areas: `gate` (check-in, parked list, settlement dialog), `reservations` + `history` (driver portal, tabbed routes under `/my`), `admin` (tabbed routes under `/admin`: dashboard, lots, lot detail, pricing, users, audit). API clients live in `core/api/*.api.ts`; models in `shared/models/`.
- Payments: one `newIdempotencyKey()` per payment ATTEMPT (per dialog), re-sent on retry, so a retry can never charge twice. Dialogs close with `true` only when something changed, and the page reloads then.
- Destructive actions go through `Confirm` (never `window.confirm`); outcomes through `Notify`; server refusals are shown as the server's own message (`toApiError`). Business rules are enforced by the API; the UI only guides (e.g. disabling "deactivate" on your own account, which the API also refuses).
- Forms: show errors on the control that owns them (cross-field rules belong on a control, or Material hides them); use `DirtyOrSubmittedErrorMatcher` for fields that are deliberately empty between tasks; use `FormGroupDirective.resetForm()` (not `form.reset()`) after a successful submit.
- Times: only `core/time.ts` converts local <-> UTC. Date filters send the start of the local day and the start of the NEXT local day (exclusive end).
- a11y: every input has a label, errors use `role=alert`/`mat-error`, skip link, focus moves to `<main>` on navigation, tables have captions.
- Commands (Node lives in `~/.local/node/bin` if not on PATH): `cd web && npm ci && npm start` (needs the API on :5080), `npm test -- --watch=false`, `npm run build:prod`.

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
