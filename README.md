# AccessRequestApp

A small ASP.NET Core Razor Pages application for requesting and reviewing access to internal
systems. Built as a time-boxed (45–90 minute) take-home exercise.

## How to Run It

Requirements: **.NET 10 SDK**.

```bash
dotnet restore
dotnet ef database update --project AccessRequestApp
dotnet run --project AccessRequestApp
```

The app listens at `http://localhost:5199` (see `AccessRequestApp/Properties/launchSettings.json`).
On first run in the `Development` environment it seeds the `Employee`/`Administrator` roles and
two demo users.

If `dotnet ef` isn't installed:

```bash
dotnet tool install --global dotnet-ef
```

### Demo users (development only — not real credentials)

| Role          | Username               | Password         |
|---------------|-------------------------|------------------|
| Employee      | employee@example.test  | Employee1!       |
| Administrator | admin@example.test     | Administrator1!  |

These accounts are seeded only when `ASPNETCORE_ENVIRONMENT=Development` and must never be used
in a real deployment.

**Reproducibility:** the three commands above are everything required — no database file,
secrets, or client-side libraries need to be supplied separately. Verified by cloning the repo
into a clean directory and confirming the commands alone recreate the SQLite database (all
migrations applying cleanly), seed both roles and demo users correctly, and allow immediate login.

### Run the tests

```bash
dotnet test
```

## Your Approach

- **Razor Pages** over Blazor/an API/SPA: the whole app is forms, tables, and two role-gated
  workflows. Razor Pages keeps the HTTP request/response and authorization boundary explicit and
  server-rendered, with no persistent client connection to reason about.
- **ASP.NET Core Identity** supplies authentication, password hashing, roles, and cookies out of
  the box — exactly what's needed to distinguish an Employee from an Administrator and attribute
  actions to a real authenticated user, without hand-rolling auth.
- **SQLite** minimizes local setup (no server to install) while EF Core migrations keep a clear
  path to a production relational database later.
- **Workflow logic lives in `AccessRequestService`** (`AccessRequestApp/Services`), not in page
  handlers. Razor Pages call it; it owns the Pending → Approved/Denied rules and audit writes, so
  the rules exist in exactly one place.
- **A structured `AccessRequestAuditEvent` table** preserves history (who did what, when) that
  the current-state `AccessRequest` row alone would lose. The two tables intentionally duplicate
  a little data (e.g. `DecidedByUserId` lives on both) because they serve different queries:
  "what's the current state" vs. "what happened over time."
- **One deployable unit.** There's a single server-rendered client, so a separate API/SPA project
  would add DTOs, routing, and auth surface without a current benefit.
- **No repository/unit-of-work/CQRS/MediatR abstractions.** EF Core's `DbContext` already is the
  unit of work; one small service interface is enough indirection for this size of app.
- **Display name.** `ApplicationUser` (`AccessRequestApp/Data/ApplicationUser.cs`) extends
  `IdentityUser` with optional `FirstName`/`LastName`. A user sets either or both on `/Profile`;
  `GetDisplayName()` shows the name if set, otherwise falls back to email — used in the nav and in
  the admin request list's "Requested By"/decision columns.

## Security & SOC 2 Considerations

Implemented:

- Authenticated users only; anonymous access is limited to login.
- `Employee` and `Administrator` Identity roles, seeded (not user-selectable — there is no role
  dropdown, since that would let anyone declare themselves an administrator).
- **Server-side authorization is the real control.** `/Requests/*` requires authentication via a
  Razor Pages folder convention; the `Approve`/`Deny` handlers additionally check the
  `CanManageAccessRequests` policy (`RequireRole(Administrator)`) with `IAuthorizationService`
  inside the handler and return `Forbid()` on failure — verified manually by scripting a raw POST
  from an authenticated employee session directly at the handler (see Manual Test Checklist); it
  was redirected to `AccessDenied` and the target request's state was untouched. Hiding the
  buttons in the UI is a usability nicety, not the security boundary.
- **Segregation of duties: an admin cannot decide their own request.** `AccessRequestService`
  folds `RequestedByUserId != administratorUserId` into the same conditional `UPDATE` used for the
  Pending check, so self-approval is closed atomically alongside the concurrency guard, not as a
  separate check that could race. The UI also hides the Approve/Deny controls on an admin's own
  requests, but — as above — that's the usability layer; verified server-side by scripting a raw
  POST past the hidden buttons and confirming the request stayed Pending (see Manual Test
  Checklist).
- **Trusted attribution.** `RequestedByUserId`/`DecidedByUserId` come from
  `UserManager.GetUserId(User)` on the server — never from a posted field. The create/approve/deny
  input models don't expose `Status`, timestamps, or user IDs as bindable properties at all
  (over-posting protection).
- **Structured audit events** (`AccessRequestAuditEvent`) record who did what and when for every
  successful Create/Approve/Deny. A rejected transition (e.g., deciding an already-decided
  request) never creates an audit row — proven by a test.
- **UTC timestamps** (`DateTimeOffset`) generated server-side for every request/decision.
- **Workflow integrity under concurrency.** Approve/Deny use a single conditional
  `UPDATE ... WHERE Status = Pending` (`ExecuteUpdateAsync`) wrapped in a transaction with the
  audit insert, so if two admins race to decide the same request, only the first `UPDATE` matches
  a row; the second sees 0 rows affected and the service throws instead of silently overwriting
  the first decision.
- **Input validation**: `[Required]`/`[StringLength]` on `CreateAccessRequestInput`; a denial
  reason is required and enforced again in the service layer (not just the UI) since the service
  is the actual authority, not the page.
- **Sessions don't survive an app restart.** By default, ASP.NET Core persists its Data Protection
  keys to disk, so a sign-in cookie issued before the app was closed would still validate after
  relaunching it — an old browser tab would just look signed in again with no re-authentication.
  `Program.cs` uses `UseEphemeralDataProtectionProvider()` so keys live only in memory: every fresh
  start invalidates every previously-issued cookie. Verified by logging in, killing the running
  app, relaunching it, and confirming the same browser tab was bounced back to Login. This is the
  right call for a locally-run tool restarted on demand; a real multi-instance/load-balanced
  deployment would instead persist keys to shared storage so restarts and scale-out don't log
  everyone out at once.
- Razor Pages' default antiforgery protection is untouched (all state changes are POST forms).
- All EF Core queries are LINQ (parameterized) — no raw SQL string concatenation.
- Identity's built-in password hashing (PBKDF2) — no custom password code.
- Async I/O throughout the request → page handler → service → EF Core → SQLite path, with
  `CancellationToken` propagated from the HTTP request into every `SaveChangesAsync`/
  `ToListAsync`/`ExecuteUpdateAsync` call.
- Basic ASP.NET Core rate limiting (see below).
- No secrets or `*.db*` files are committed (see `.gitignore`).

**Explicitly not claimed:**

- This application is **not production-ready and not SOC 2 compliant.** SOC 2 concerns
  organizational controls, evidence, and process (access reviews, change management, incident
  response, retention, vendor management) far beyond application code.
- The SQLite audit table is an **application-level** audit trail, not tamper-proof evidence — a
  privileged database user could still edit it directly.
- Rate limiting here is defense-in-depth, **not** DDoS protection. Real DDoS protection belongs at
  the edge/network layer (managed DDoS protection, WAF, reverse proxy/load balancer) in front of
  the app.
- Production authentication should use the organization's approved enterprise identity provider,
  not local ASP.NET Core Identity accounts.

### Rate limiting

Registered via the built-in `Microsoft.AspNetCore.RateLimiting` middleware (`Program.cs`):

- **Global limiter**: fixed window, partitioned by authenticated user ID when present, otherwise
  by remote IP — 100 req/min authenticated, 30 req/min anonymous.
- **`submission` policy** (`Requests/Create`): 5/min per user.
- **`decisions` policy** (`Requests/Index`): 20/min per user. Note: because Razor Pages rate-limit
  attributes apply per page rather than per handler, this also covers that page's `GET` (viewing
  the list), not just the `Approve`/`Deny` posts — an accepted simplification given the time box,
  documented here rather than split into extra pages to get finer granularity.
- All policies use `QueueLimit = 0` (fail fast, no backlog) and return **HTTP 429** on rejection,
  with a structured warning log entry (`Program.cs`, `OnRejected`).
- If deployed behind a reverse proxy, IP-based partitioning would need `ForwardedHeadersOptions`
  configured against **trusted** proxies — client-supplied `X-Forwarded-For` headers must never be
  trusted blindly. Not configured here since there is no reverse proxy in this local exercise.

### CAPTCHA / bot protection

Not implemented. The workflows here require an authenticated internal user; CAPTCHA on every
access request would add friction without a real anti-automation benefit for this audience. It
would be considered for repeated failed logins, password recovery, or public registration in a
production system — none of which are in scope here.

### Caching / SignalR

Neither is used, deliberately. Every page here shows user-specific, authorization-sensitive data
(a caller's own requests, or all requests for an admin); caching it would risk serving one user's
data to another without a demonstrated performance need on a dataset this small. There's no
real-time requirement — both roles can refresh the page — so SignalR would only add persistent
connections and complexity without solving caching or DDoS concerns.

## Scope Decisions

Intentionally not implemented, given the time box:

- Production identity-provider integration (e.g., Entra ID/Okta/SAML)
- Email/Teams notifications on decisions
- Multi-stage or second-person (maker/checker) approval
- Request cancellation by the requester
- Administrative user/role management UI
- Advanced filtering, sorting, and pagination on the request list
- A `/Requests/Details/{id}` page (list + inline decision covers the requirement)
- Distributed caching, SignalR, CAPTCHA
- Distributed rate limiting / production DDoS infrastructure
- Production audit export to external, access-controlled storage
- Database encryption-at-rest configuration
- Exhaustive automated test coverage (only the highest-value workflow/security tests are included)
- A separate API or SPA project

None of these were necessary to demonstrate the core workflow, the authorization boundary,
auditability, or the engineering trade-offs the exercise is evaluating.

## Production Readiness

In priority order, before this would go anywhere near production:

1. Integrate the organization's approved enterprise identity provider.
2. Confirm role assignment, approval authority, and segregation-of-duties requirements with the
   business.
3. Put the app behind managed DDoS protection, a WAF, and an approved reverse proxy/load balancer.
4. Configure and load-test edge and application rate limits together.
5. Move persistence from SQLite to the organization's approved production database.
6. Export audit events to centralized, access-controlled, tamper-resistant storage with retention
   and monitoring.
7. Configure TLS, encryption at rest, secrets management, and key rotation.
8. Add request-size limits, timeouts, and revisit concurrency controls under real load (the
   conditional-update approach here is a reasonable start, but should be load-tested).
9. Add risk-based bot challenges/CAPTCHA to anonymous authentication and account-recovery flows.
10. Add structured logging, metrics, tracing, health checks, alerting, and capacity monitoring.
11. Add integration, authorization, concurrency, load, and security tests.
12. Complete threat modeling, security review, backup testing, retention planning, and
    incident-response preparation.

SignalR or distributed caching would only be added if a real-time or measured performance
requirement actually justified them.

## AI Usage

I used AI (Claude Code) to scaffold the initial Razor Pages/Identity/EF Core project structure,
generate the domain entities, service, pages, and rate-limiting configuration from a detailed
spec, and to draft this README.

I treated the generated code as untrusted input: I reviewed every file, compiled after each stage,
ran `dotnet ef` migrations and inspected the generated SQLite schema, and drove both the Employee
and Administrator flows through a real browser session (login, create, view, approve, deny). While
testing manually I found and fixed a real bug the AI introduced — `OrderByDescending` on a
`DateTimeOffset` column throws at runtime on SQLite (`NotSupportedException`), fixed by ordering by
`Id` instead, which is equivalent here since IDs increase monotonically with insertion order. I
also verified server-side authorization by scripting a raw `fetch()` POST from an authenticated
employee session directly against the admin `Approve` handler and confirming it was redirected to
`AccessDenied` with no state change, rather than trusting that hiding the button in the UI was
sufficient. I wrote and ran the automated test suite (`dotnet test`, 7/7 passing) and fixed a
test-fixture bug of my own along the way (reusing one `DbContext` across create/approve calls
masked `ExecuteUpdateAsync`'s effect behind EF's change-tracker identity map — not a production
bug, since each HTTP request gets its own scoped `DbContext`, but worth noting since it's a subtle
EF Core gotcha).

## Known Limitations

- SQLite has materially more limited concurrency than a client/server RDBMS; the conditional
  `UPDATE ... WHERE Status = Pending` prevents two admins from double-deciding the same request,
  but this hasn't been load-tested.
- The `decisions` rate-limit policy applies to the whole `/Requests` admin/employee list page
  (`GET` included), not just the decision POSTs — see the Rate Limiting section above.
- No structured operational logging/metrics/tracing beyond ASP.NET Core's defaults and the
  rate-limit rejection log line.

## Manual Test Checklist

- [ ] `dotnet ef database update --project AccessRequestApp` creates `access-requests.db`.
- [ ] Log in as `employee@example.test` / `Employee1!`.
- [ ] Create a request via `/Requests/Create`; it appears on `/Requests` as **Pending**, with no
      Approve/Deny controls visible.
- [ ] On `/Profile`, set a first name and save — the nav's "Hello ..." updates to the name
      immediately; leave both fields blank and save — it reverts to showing the email.
- [ ] Log out, log in as `admin@example.test` / `Administrator1!`.
- [ ] `/Requests` shows every request (not just the admin's own) with a **Requested By** column,
      showing the employee's display name (or email, if no name was set).
- [ ] As admin, create a request for yourself — it shows "Your own request — cannot self-decide."
      instead of Approve/Deny; a raw POST to `/Requests?handler=Approve` for that request's `id`
      is rejected server-side too and the request stays Pending.
- [ ] Approve a pending request — status flips to **Approved**, decided-by/at populate.
- [ ] Deny a pending request without typing a reason — browser blocks submit (`required` field);
      submitting via a raw request without a reason is rejected server-side too.
- [ ] Deny a pending request with a reason — status flips to **Denied**, reason is shown.
- [ ] Re-decide an already-decided request — rejected, original decision unchanged.
- [ ] While authenticated as the employee, POST directly to
      `/Requests?handler=Approve` — redirected to `/Identity/Account/AccessDenied`, target
      request unchanged.
- [ ] `dotnet test` — all tests pass.
