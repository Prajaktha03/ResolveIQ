## LIVE DEMO : https://resolveiq-client.onrender.com/

# ResolveIQ Backend API

ResolveIQ is an enterprise IT service management and ticket tracking Web API built on .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL, and JWT Bearer authentication.

---

## Architecture & Technology Stack

- **Framework**: .NET 10 (C# 13)
- **Database**: PostgreSQL with Entity Framework Core 10 (Npgsql.EntityFrameworkCore.PostgreSQL)
- **Authentication**: JWT Bearer Tokens with HMAC-SHA256 signing
- **Password Security**: `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (PBKDF2 with HMAC-SHA512)
- **API Documentation**: OpenAPI / Swagger UI
- **Testing**: xUnit with EF Core In-Memory database provider

---

## Role-Based Access Control (RBAC)

The system enforces three primary roles:
1. **Employee**: Can report tickets, comment, view their own tickets, view ticket histories for their tickets, and manage their own profile and password.
2. **Agent**: Can view and manage tickets assigned to them, comment, update status, view dashboard analytics, and manage profile and password.
3. **Admin**: Full system access, ticket assignment, user role management, system-wide search/filtering, and comprehensive SLA analytics.

---

## Batch 2 Feature Implementations

### 1. Ticket Search, Filtering, Sorting & Pagination
- **Search**: Case-insensitive substring matching against `Title` and `Description`, and exact numeric matching against `Id`.
- **Filtering**:
  - `status`: Open, InProgress, Resolved
  - `priority`: Low, Medium, High, Critical
  - `assigneeId`: Filter by assigned agent user ID
  - `creatorId`: Filter by creator user ID
  - `createdAtFrom` & `createdAtTo`: Filter by ticket creation timestamp range
- **Sorting**:
  - `sortBy`: `title`, `priority`, `status`, `id`, `createdAt` (default)
  - `sortDirection`: `asc` or `desc`
  - Deterministic tie-breaker: Secondary ordering by `.ThenBy(t => t.Id)` or `.ThenByDescending(t => t.Id)` ensures deterministic result sets across pages.
- **Pagination**:
  - `page`: Clamped to `Math.Max(1, page)`
  - `pageSize`: Clamped between 1 and 100 (default 10)
  - Returns `PagedResult<T>` containing `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`, `HasPreviousPage`, and `HasNextPage`.

### 2. User Profile Retrieval & Safe Updates
- `GET /api/Users/me`: Retrieves current authenticated user's profile (`Id`, `FullName`, `Email`, `Role`, `CreatedAt`) without exposing password hashes.
- `PUT /api/Users/me` / `PATCH /api/Users/me`: Allows safe update of `FullName` and `Email`.
  - Enforces email uniqueness across users (HTTP 409 Conflict if taken).
  - Role cannot be modified through the profile endpoint (role immutability).
- `GET /api/Users/{id}`: Self and Admins can view profile; other users receive HTTP 403 Forbidden.
- `GET /api/Users`: Restricted strictly to Admins (HTTP 403 Forbidden for others).

### 3. Authenticated Password Change
- `POST /api/Auth/change-password`:
  - Requires JWT authentication.
  - Verifies `CurrentPassword` against stored password hash.
  - Validates that `NewPassword` matches `ConfirmNewPassword`.
  - Re-hashes and saves updated password.

### 4. Secure Password Reset Workflow
- `POST /api/Auth/forgot-password`:
  - Prevents email enumeration by returning a generic success message whether the user exists or not.
  - Generates a cryptographically random, 32-byte (64 hex character) token.
  - Stores a **SHA-256 hash** of the token in `PasswordResetTokens` table with a 15-minute expiration timestamp (`ExpiresAt`).
  - Dispatches the raw token via `IEmailService.SendPasswordResetEmailAsync`. The raw token is **never** returned in API responses or written to persistent logs.
- `POST /api/Auth/reset-password`:
  - Looks up user and verifies token hash.
  - Rejects expired tokens (`DateTime.UtcNow > token.ExpiresAt`).
  - Rejects already-used tokens (`token.IsUsed == true`).
  - Validates password confirmation match.
  - Marks token as `IsUsed = true` immediately upon successful reset (single-use enforcement).
  - Updates the user's password hash.

### 5. Performance Indexes & Migrations
- Migration `20261016145548_AddPasswordResetTokensAndTicketIndexes`:
  - Created `PasswordResetTokens` table with index on `TokenHash` and foreign key cascade delete to `Users`.
  - Added PostgreSQL B-tree indexes on `Tickets`:
    - `IX_Tickets_Status`
    - `IX_Tickets_Priority`
    - `IX_Tickets_CreatedAt`

---

## Batch 3 Feature Implementations

### 1. Persistent In-App Notifications
- **Entity**: `Notification` (`Id`, `UserId`, `Type`, `Title`, `Message`, `TicketId`, `CreatedAt`, `IsRead`, `ReadAt`, `DeduplicationKey`).
- **Database Indexes**: Indexes on `UserId`, `(UserId, IsRead)`, `(UserId, CreatedAt)`, and a unique index on `DeduplicationKey`.
- **Endpoints**:
  - `GET /api/Notifications`: Lists caller's notifications, filterable by `isRead`, paginated with metadata.
  - `GET /api/Notifications/unread-count`: Returns unread count for caller.
  - `PATCH /api/Notifications/{id}/read`: Marks single notification read; verifies caller owns it (HTTP 403 Forbidden if not owned).
  - `PATCH /api/Notifications/read-all`: Marks all unread notifications for caller as read.

### 2. Ticket Event Notifications
- Lifecycle events trigger notifications with actor-exclusion:
  - **Ticket Created**: Admins notified (`Type = TicketCreated`). Creator excluded.
  - **Ticket Assigned**: Newly assigned agent and ticket creator notified (`Type = TicketAssigned`). Admin excluded.
  - **Ticket Reassigned**: Old agent and new agent notified of assignment changes.
  - **New Comment**:
    - Agent/Admin comment notifies ticket creator.
    - Creator comment notifies assigned agent (or Admins if unassigned).
    - Comment author is never notified of their own comment.
  - **Status & Priority Changed**: Ticket creator and assigned agent notified. Actor excluded.

### 3. SLA At-Risk Warnings and Breach Alerts
- Integrated with `SlaMonitoringService` and `SlaMonitoringBackgroundService`.
- **Warning Window**: Configurable via `SlaMonitoring:WarningWindowMinutes` (default 15 minutes). Dispatches `SlaAtRisk` notification when deadline is approaching.
- **Breach Alerts**: Dispatches `SlaBreached` notification when deadline is passed and logs `FirstResponseBreached` or `ResolutionBreached` in `TicketHistory`.
- **Idempotency**: Unique deduplication keys (`sla_warning_first_response_{id}`, `sla_breached_first_response_{id}`, etc.) ensure subsequent polling loops do not generate repeat alerts.
- Invariants: Resolved tickets are never flagged for resolution risk; tickets with recorded `FirstRespondedAt` never trigger first response alerts.

### 4. Configurable Escalation Workflows
- Escalates `Critical` and `High` priority tickets remaining in breach past `SlaMonitoring:EscalationDelayMinutes` (default 30 minutes).
- Resolves actual Admins (`Role == "Admin"`).
- Logs `TicketHistory` with `Action = "Escalated"`, `UserId = null` (system actor convention compatible with database constraints).
- Dispatches `Escalated` notification to all Admins with deduplication key (`escalation_{ticketId}_{milestone}`).
- Endpoint `GET /api/Tickets/escalations`: Restricted to `Admin` role to review escalation events.

### 5. Real-Time Notifications with SignalR
- **Hub Endpoint**: `/hubs/notifications` (`NotificationHub`).
- **Authentication**: JWT token passed in query parameter `?access_token=` for WebSocket connections, verified securely in `OnMessageReceived`.
- **Targeting**: Dispatches real-time event `"ReceiveNotification"` with `NotificationResponse` payload to `Clients.User(recipientUserId)`.
- **Resilience**: Database persistence commits first; transient SignalR transport failures are logged and do not corrupt or fail the transaction.
- **Frontend Connection Guide**:
  ```javascript
  import * as signalR from "@microsoft/signalr";

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:5022/hubs/notifications", {
      accessTokenFactory: () => jwtToken
    })
    .withAutomaticReconnect()
    .build();

  connection.on("ReceiveNotification", (notification) => {
    console.log("New notification received:", notification);
  });

  await connection.start();
  ```

---

## API Endpoints Reference

| Method | Route | Access | Description |
|---|---|---|---|
| `POST` | `/api/Auth/register` | Public | Register new employee |
| `POST` | `/api/Auth/login` | Public | Authenticate user, receive JWT |
| `POST` | `/api/Auth/change-password` | Authenticated | Change password verifying current password |
| `POST` | `/api/Auth/forgot-password` | Public | Request short-lived password reset token |
| `POST` | `/api/Auth/reset-password` | Public | Reset password using valid, single-use token |
| `GET` | `/api/Users/me` | Authenticated | Get current user's profile |
| `PUT` | `/api/Users/me` | Authenticated | Update current user's name and email |
| `GET` | `/api/Users/{id}` | Self / Admin | Get user profile by ID |
| `GET` | `/api/Users` | Admin | List all users |
| `PATCH` | `/api/Users/{id}/role` | Admin | Update user role (promote/demote) |
| `GET` | `/api/Tickets` | Authenticated | Search, filter, sort, paginate tickets |
| `GET` | `/api/Tickets/assigned` | Agent / Admin | Get tickets assigned to current agent |
| `GET` | `/api/Tickets/{id}` | Owner / Agent / Admin | Get ticket details |
| `GET` | `/api/Tickets/{id}/sla` | Owner / Agent / Admin | Get SLA deadline tracking status |
| `GET` | `/api/Tickets/{id}/history`| Owner / Agent / Admin | Get audit trail of ticket changes |
| `POST` | `/api/Tickets` | Authenticated | Create a new ticket |
| `PATCH` | `/api/Tickets/{id}/status` | Agent / Admin | Transition ticket status |
| `PATCH` | `/api/Tickets/{id}/assign` | Admin | Assign ticket to an agent |
| `POST` | `/api/Tickets/{id}/comments` | Authenticated | Add a comment to ticket |
| `GET` | `/api/Tickets/{id}/comments` | Authenticated | Retrieve ticket comments |
| `GET` | `/api/Tickets/dashboard` | Agent / Admin | Dashboard metrics and SLA breach analytics |
| `GET` | `/api/Tickets/escalations`| Admin | View escalated tickets and breach reasons |
| `GET` | `/api/Notifications` | Authenticated | Get paginated caller notifications |
| `GET` | `/api/Notifications/unread-count` | Authenticated | Get unread notification count |
| `PATCH` | `/api/Notifications/{id}/read` | Authenticated | Mark caller notification as read |
| `PATCH` | `/api/Notifications/read-all` | Authenticated | Mark all caller notifications as read |
| `WS` | `/hubs/notifications` | Authenticated | SignalR real-time notification socket |

---

## Batch 4 Feature Implementations

### 1. Security Hardening & Headers
- **Security Headers Middleware**: Enforces defense-in-depth HTTP response headers:
  - `X-Content-Type-Options: nosniff` (MIME-sniffing prevention)
  - `X-Frame-Options: DENY` (Clickjacking defense)
  - `Referrer-Policy: strict-origin-when-cross-origin` (Information leak mitigation)
  - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
  - `Permissions-Policy: camera=(), microphone=(), geolocation=()`
  - `X-Permitted-Cross-Domain-Policies: none`
- **Configurable Explicit CORS**: Restricts allowed origins via `Cors:AllowedOrigins` configuration array. In development, defaults to standard local development clients; in production, rejects unconfigured origins.
- **Strict JWT Configuration Validation**:
  - Enforces minimum 256-bit (32 bytes) symmetric signing key.
  - Fails safely on startup if `Jwt:Key`, `Jwt:Issuer`, or `Jwt:Audience` is missing, short, or invalid.
- **Production Swagger Protection**: Swagger UI is gated via `Swagger:Enabled` configuration, disabled by default in production environments to avoid leaking API schema diagnostics.

### 2. Centralized RFC 7807 Error Handling
- **ProblemDetails Exception Middleware**:
  - Catches unhandled exceptions and writes compliant `application/problem+json` (RFC 7807) responses.
  - In Production: sanitizes internal database exception messages, connection strings, and stack traces into generic safe text.
  - In Development: retains developer diagnostic details.
  - Embeds request `traceId` / correlation ID in ProblemDetails extension property for immediate log cross-referencing.
  - Gracefully ignores client cancellation (`OperationCanceledException`) on aborted requests without polluting server logs with false alarms.

### 3. Rate Limiting & Abuse Protection
- **ASP.NET Core Partitioned Fixed-Window Rate Limiting**:
  - **Auth Policy (`"auth"`)**: 10 permits/minute partitioned by client IP (`RemoteIpAddress`). Protects sensitive `/api/Auth/login`, `/api/Auth/register`, `/api/Auth/forgot-password`, `/api/Auth/reset-password`, and `/api/Auth/change-password` endpoints against brute force and credential stuffing.
  - **General API Policy (`"general"`)**: 100 permits/minute partitioned by authenticated user ID (`ClaimTypes.NameIdentifier`) or client IP for anonymous callers. Applied across `TicketsController`, `TicketCommentsController`, `UsersController`, and `NotificationsController`.
  - **Rejection Behavior**: Returns HTTP 429 Too Many Requests with RFC 7807 `application/problem+json` payload, `traceId`, and standard `Retry-After` header.

### 4. Correlation IDs, Structured Logging & Health Checks
- **Correlation ID Middleware**:
  - Reads `X-Correlation-ID` from incoming requests or generates a clean GUID (`N` format).
  - Synchronizes `context.TraceIdentifier` with the correlation ID.
  - Enriches all downstream server logs with correlation context via structured logger scope.
  - Attaches `X-Correlation-ID` to outgoing response headers.
- **Liveness Probe (`/health/live`)**:
  - Verifies process responsiveness without external dependencies.
  - Returns HTTP 200 `{ "status": "Healthy" }`.
- **Readiness Probe (`/health/ready`)**:
  - Verifies PostgreSQL database connectivity using EF Core `AppDbContext.Database.CanConnectAsync()`.
  - Returns HTTP 200 `{ "status": "Healthy", "database": "Operational" }` or HTTP 503 `{ "status": "Unhealthy", "database": "Unreachable" }`.
  - Does NOT leak database hostnames, credentials, or internal query diagnostics to callers.

---

## API Endpoints Reference

| Method | Route | Access | Rate Limit | Description |
|---|---|---|---|---|
| `GET` | `/health/live` | Public | None | Liveness health check probe |
| `GET` | `/health/ready` | Public | None | Readiness health check probe (PostgreSQL) |
| `POST` | `/api/Auth/register` | Public | Auth (10/min) | Register new employee |
| `POST` | `/api/Auth/login` | Public | Auth (10/min) | Authenticate user, receive JWT |
| `POST` | `/api/Auth/change-password` | Authenticated | Auth (10/min) | Change password verifying current password |
| `POST` | `/api/Auth/forgot-password` | Public | Auth (10/min) | Request short-lived password reset token |
| `POST` | `/api/Auth/reset-password` | Public | Auth (10/min) | Reset password using valid, single-use token |
| `GET` | `/api/Users/me` | Authenticated | General (100/min) | Get current user's profile |
| `PUT` | `/api/Users/me` | Authenticated | General (100/min) | Update current user's name and email |
| `GET` | `/api/Users/{id}` | Self / Admin | General (100/min) | Get user profile by ID |
| `GET` | `/api/Users` | Admin | General (100/min) | List all users |
| `PATCH` | `/api/Users/{id}/role` | Admin | General (100/min) | Update user role (promote/demote) |
| `GET` | `/api/Tickets` | Authenticated | General (100/min) | Search, filter, sort, paginate tickets |
| `GET` | `/api/Tickets/assigned` | Agent / Admin | General (100/min) | Get tickets assigned to current agent |
| `GET` | `/api/Tickets/{id}` | Owner / Agent / Admin | General (100/min) | Get ticket details |
| `GET` | `/api/Tickets/{id}/sla` | Owner / Agent / Admin | General (100/min) | Get SLA deadline tracking status |
| `GET` | `/api/Tickets/{id}/history`| Owner / Agent / Admin | General (100/min) | Get audit trail of ticket changes |
| `POST` | `/api/Tickets` | Authenticated | General (100/min) | Create a new ticket |
| `PATCH` | `/api/Tickets/{id}/status` | Agent / Admin | General (100/min) | Transition ticket status |
| `PATCH` | `/api/Tickets/{id}/assign` | Admin | General (100/min) | Assign ticket to an agent |
| `POST` | `/api/Tickets/{id}/comments` | Authenticated | General (100/min) | Add a comment to ticket |
| `GET` | `/api/Tickets/{id}/comments` | Authenticated | General (100/min) | Retrieve ticket comments |
| `GET` | `/api/Tickets/dashboard` | Agent / Admin | General (100/min) | Dashboard metrics and SLA breach analytics |
| `GET` | `/api/Tickets/escalations`| Admin | General (100/min) | View escalated tickets and breach reasons |
| `GET` | `/api/Notifications` | Authenticated | General (100/min) | Get paginated caller notifications |
| `GET` | `/api/Notifications/unread-count` | Authenticated | General (100/min) | Get unread notification count |
| `PATCH` | `/api/Notifications/{id}/read` | Authenticated | General (100/min) | Mark caller notification as read |
| `PATCH` | `/api/Notifications/read-all` | Authenticated | General (100/min) | Mark all caller notifications as read |
| `WS` | `/hubs/notifications` | Authenticated | General | SignalR real-time notification socket |
| `POST` | `/api/AI/classify-ticket` | Authenticated | AI (20/min) | Advisory ticket category, subcategory & priority classification |
| `POST` | `/api/AI/recommend-priority` | Authenticated | AI (20/min) | Evidence-based priority recommendation & SLA impact analysis |
| `POST` | `/api/AI/troubleshoot/{id}` | Authenticated | AI (20/min) | Grounded diagnostic summary, hypotheses, steps & clarifying Qs |
| `GET` | `/api/KnowledgeBase` | Authenticated | General (100/min) | Search and retrieve published knowledge articles |
| `GET` | `/api/KnowledgeBase/{id}` | Authenticated | General (100/min) | Get specific knowledge article by ID |
| `POST` | `/api/KnowledgeBase` | Agent / Admin | General (100/min) | Author new knowledge article |
| `PUT` | `/api/KnowledgeBase/{id}` | Agent / Admin | General (100/min) | Update existing knowledge article |
| `DELETE` | `/api/KnowledgeBase/{id}` | Admin | General (100/min) | Delete knowledge article |

---

## Batch 5 Feature Implementations

### 1. Docker Containerization (.NET 10)
- **Multi-Stage Build Pipeline**:
  - `build` stage: `mcr.microsoft.com/dotnet/sdk:10.0` restores dependencies using solution and project caching layers before copying full source.
  - `publish` stage: Compiles the API in `Release` mode with `/p:UseAppHost=false`.
  - `final` stage: `mcr.microsoft.com/dotnet/aspnet:10.0` runtime image.
- **Security & Non-Root Execution**:
  - Explicitly runs under the unprivileged built-in `app` user (`USER app`, UID 1654).
  - Published artifacts are copied with non-root ownership (`--chown=app:app`).
  - Container binds to port `8080` (`ASPNETCORE_HTTP_PORTS=8080`).
- **Build Context Sanitization (`.dockerignore`)**:
  - Excludes source control metadata (`.git`), IDE artifacts (`.vs`, `.vscode`), compiled outputs (`bin`, `obj`), local secret files (`.env`, `appsettings.Development.json`), test results, and log files.

### 2. Local Docker Compose Setup
- **`docker-compose.yml`**:
  - **Database Service (`db`)**: `postgres:17-alpine` container with a named persistent volume (`resolveiq_postgres_data`), isolated bridge network (`resolveiq_network`), and health check probe (`pg_isready`).
  - **API Service (`api`)**: Builds from the local `Dockerfile`, connects to the database via internal container DNS (`Host=db`), and waits for database health confirmation (`condition: service_healthy`) before booting.
  - Host port `5022` forwards directly to container port `8080`.
- **Environment Template (`.env.example`)**:
  - Contains strictly non-sensitive placeholders for local configuration (`POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `JWT_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE`).
  - Real `.env` files are ignored by version control in `.gitignore`.

### 3. Production Database & Safe Migration Strategy
- **Idempotent Migration Bundle (`migration_bundle.sql`)**:
  - Pre-generated idempotent SQL script covering all 9 migrations from `InitialCreate` through `AddNotificationsTableAndIndexes`.
  - Every DDL operation is wrapped in transactions and protected by `IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = ...)` checks.
- **Zero Race Conditions on Startup**:
  - Schema migrations are decoupled from application startup (`Database.Migrate()` is intentionally omitted from `Program.cs`).
  - Multiple horizontal API replicas can start concurrently without database lock contention, schema deadlocks, or migration table race conditions.
  - Schema migrations should be executed once per release pipeline using the idempotent SQL script or `dotnet ef database update`.

### 4. Production Configuration & Secrets Separation
- **`appsettings.Production.json`**:
  - Disables Swagger UI by default (`Swagger:Enabled: false`).
  - Clears default CORS origins to prevent accidental development origin exposure.
  - Configures `ForwardedHeaders:ForwardAllProxies: true` for cloud reverse proxies.
  - Suppresses database command query logging in production logs.
- **Fail-Fast Secret Validation**:
  - On application startup, `Program.cs` validates that `Jwt:Key` is provided and is a valid base64 symmetric key of at least 256 bits (32 bytes).
  - Missing database connection strings or invalid JWT configurations terminate process startup immediately with descriptive error logging.
- **Reverse Proxy Forwarded Headers**:
  - Configured `ForwardedHeadersOptions` (`X-Forwarded-For` and `X-Forwarded-Proto`).
  - Allows rate limiting, logging, and security middleware to accurately identify client IP addresses and enforce HTTPS schemes behind AWS ALB, Cloudflare, Nginx, or Kubernetes Ingress.

### 5. GitHub Actions CI/CD Automation
- **Workflow (`.github/workflows/ci.yml`)**:
  - Triggers on pull requests and pushes to `main` and `master`.
  - **Job 1: `build-and-test`**:
    - Checks out code with `actions/checkout@v4`.
    - Sets up .NET 10 SDK with `actions/setup-dotnet@v4`.
    - Caches NuGet dependencies with `actions/cache@v4`.
    - Restores, builds (`Release`), and executes the complete 115-test suite.
    - Validates EF Core migration bundle generation.
    - Sets up Docker Buildx and compiles the multi-stage `Dockerfile` (push disabled) to verify container build integrity on every PR.
  - **Job 2: `deploy`**:
    - Environment: `production`.
    - Gated by GitHub repository secrets (`DEPLOY_HOOK_URL` / `DOCKER_REGISTRY_PASSWORD`). Safely logs notice when secrets are not yet configured without failing the CI pipeline.

### 6. Cloud Hosting Plan & Infrastructure-as-Code
- **Render Blueprint (`render.yaml`)**:
  - Provides a single-click PaaS deployment configuration.
  - Provisions a managed PostgreSQL database with automatic backups and a web service container for the API.
  - Automatically injects database connection strings and auto-generated 256-bit JWT secret keys.
- **Hosting Tier Comparison**:
  - **Render Starter** (~$14/mo total: $7 web service + $7 managed PostgreSQL): Best entry-level managed production stack. Persistent storage, automated daily backups, SSL, continuous uptime.
  - **Railway** (~$10-15/mo): Pay-per-usage, private network PostgreSQL, automatic container builds.
  - **DigitalOcean App Platform** (~$20/mo): Dedicated container droplet + managed PostgreSQL.

---

## Production Deployment & Smoke-Test Runbook

### Step 1: Pre-Deployment Verification
Before pushing to production or initiating a deploy:
```powershell
# 1. Run local build and automated test suite
dotnet test

# 2. Verify migration SQL script generation
dotnet ef migrations script --idempotent --project ResolveIQ.Api --output migration_check.sql
```

### Step 2: Apply Database Migrations (One-Shot Step)
Execute the idempotent migration bundle against the target PostgreSQL instance before launching updated container replicas:
```bash
# Using psql with connection string
psql "$PRODUCTION_DATABASE_URL" -f ResolveIQ.Api/Migrations/migration_bundle.sql

# OR using dotnet-ef CLI with target connection string
dotnet ef database update --project ResolveIQ.Api --connection "$PRODUCTION_DATABASE_URL"
```

### Step 3: Run Local Containerized Stack
To test the containerized API and PostgreSQL database locally using Docker Compose:
```bash
# 1. Copy environment template and fill in local passwords
cp .env.example .env

# 2. Build and launch services in background
docker compose up -d --build

# 3. Verify container statuses
docker compose ps
```

### Step 4: Smoke-Test Checklist
After deployment to staging or production, execute the following smoke tests against the API URL:

1. **Liveness Probe**:
   ```bash
   curl -i https://your-api-domain.com/health/live
   # Expect: HTTP 200 OK, {"status":"Healthy"}
   ```
2. **Readiness Probe**:
   ```bash
   curl -i https://your-api-domain.com/health/ready
   # Expect: HTTP 200 OK, {"status":"Healthy","database":"Operational"}
   ```
3. **Security Headers Verification**:
   ```bash
   curl -I https://your-api-domain.com/health/live
   # Expect headers:
   # X-Content-Type-Options: nosniff
   # X-Frame-Options: DENY
   # Referrer-Policy: strict-origin-when-cross-origin
   # Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
   # X-Correlation-ID: <guid>
   ```
4. **Root Endpoint**:
   ```bash
   curl https://your-api-domain.com/
   # Expect: "ResolveIQ API is running!"
   ```
5. **Authentication & Rate Limiting**:
   ```bash
   # Test user login
   curl -X POST https://your-api-domain.com/api/Auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"admin@resolveiq.com","password":"YourPassword123!"}'
   # Expect: HTTP 200 with JWT token, or HTTP 401 if invalid credentials.
   # Exceed 10 requests within 60s to verify HTTP 429 Too Many Requests and Retry-After header.
   ```
6. **Swagger Exposure Verification**:
   ```bash
   curl -i https://your-api-domain.com/swagger
   # In Production: Expect HTTP 404 Not Found (Swagger disabled).
   ```

### Step 5: Rollback Procedure
If a production issue occurs after deployment:
1. **Container Revert**: Roll back the container image tag in your cloud dashboard (e.g. Render, ECS, Kubernetes) to the previous known stable SHA tag.
2. **Database Schema**: Because migrations are strictly additive (no destructive drops or renames), previous API versions remain fully compatible with the database schema.
3. **Verify Health**: Check `/health/ready` to ensure the restored container establishes database connectivity.

---

## Batch 6 Feature Implementations

### 1. AI Intelligence Engine Architecture
- **Provider-Independent Abstraction (`IAiIntelligenceService`)**:
  - Implemented [AiIntelligenceService.cs](file:///c:/Users/nauma/ResolveIQ/ResolveIQ.Api/Services/AiIntelligenceService.cs) supporting OpenAI, Azure OpenAI, Ollama, and local REST-compatible model endpoints.
  - Provider selection and credentials configured via `Ai` settings:
    - `Ai:Enabled`: Master toggle (default `false`).
    - `Ai:Provider`: Target provider (`OpenAI`, `AzureOpenAI`, `Ollama`, or `None`).
    - `Ai:ApiKey`: Secret key supplied via environment variables (`AI__ApiKey`) or user-secrets (never committed to git).
    - `Ai:Endpoint`: Base API URL (e.g. `https://api.openai.com/v1`).
    - `Ai:Model`: Model name (default `gpt-4o-mini`).
    - `Ai:TimeoutSeconds`: Maximum inference duration (default 15s).
- **Graceful Failure Tolerance & Zero Fabrication**:
  - When unconfigured or disabled, AI endpoints return clean HTTP 503 Service Unavailable with RFC 7807 ProblemDetails. No external network requests are made.
  - Normal ticket CRUD and IT service desk workflows operate 100% independently of AI availability.
  - Zero fabrication: Knowledge base articles, similar tickets, and citations are never invented.

### 2. Advisory Ticket Classification
- **Endpoint**: `POST /api/AI/classify-ticket`
- **Capabilities**:
  - Accepts either a saved `ticketId` or proposed `title` and `description`.
  - Maps tickets to a documented enterprise IT taxonomy: `Hardware`, `Software`, `Network`, `Access & Identity`, `Infrastructure`, and `General`.
  - Suggests priority (`Low`, `Medium`, `High`, `Critical`), confidence score (0.0 to 1.0), reasoning rationale, and searchable tags.
  - Strictly advisory: Does NOT mutate ticket properties in the database.

### 3. Evidence-Based Priority Recommendation
- **Endpoint**: `POST /api/AI/recommend-priority`
- **Capabilities**:
  - Analyzes ticket content alongside optional `impact` (e.g., `SingleUser`, `Department`, `OrganizationWide`) and `urgency`.
  - Returns current priority, recommended priority, justification rationale, and uncertainty warnings.
  - Automatically calculates SLA implications using the existing `SlaService` (Critical: 15m/4h, High: 1h/8h, Medium: 4h/24h, Low: 8h/72h).
  - Never mutates ticket priority or resets SLA deadlines.

### 4. Grounded Troubleshooting Assistant
- **Endpoint**: `POST /api/AI/troubleshoot/{ticketId}`
- **Capabilities**:
  - Generates a concise incident summary, potential root causes labeled as hypotheses, ordered diagnostic steps, and clarifying questions.
  - **Grounding Retrieval**: Automatically queries the repository's knowledge base and similar resolved tickets.
  - **Grounding Notice**: Explicitly informs the IT agent whether recommendations are grounded in repository articles or represent general model hypotheses.

### 5. Knowledge Base & Authorization-Aware Retrieval
- **Entity**: `KnowledgeArticle` with fields for `Title`, `Content`, `Category`, `Tags`, `IsPublished`, `CreatedAt`, and `AuthorUserId`.
- **Database Indexing**: B-tree indexes on `Category`, `IsPublished`, and `CreatedAt` applied via EF Core migration `20261016161824_AddKnowledgeArticles`.
- **Role-Based Retrieval Isolation**:
  - Knowledge articles are readable by all authenticated roles; authoring is restricted to `Agent` and `Admin`.
  - Similar resolved ticket search enforces strict caller isolation: Employees only see their own resolved tickets; Agents see tickets assigned to them; Admins see all tickets.

### 6. Security, Rate Limiting & Prompt Injection Hardening
- **Prompt Injection Defense**:
  - Untrusted customer ticket titles and descriptions are wrapped inside structural `<ticket_title>` and `<ticket_description>` delimiter tags.
  - System prompts command the model to treat user content strictly as passive data and reject embedded execution instructions.
- **Dedicated Rate Limiting**:
  - Configured `RateLimiter` policy `"ai"` allowing up to 20 requests/minute per authenticated user (HTTP 429 with `Retry-After` on exceed).
- **Secret & PII Redaction**:
  - Model API keys are never included in error responses, log events, or client payloads.
  - Structured logging logs model identifiers and prompt token metadata without writing raw ticket descriptions to persistent logs.

---

## Batch 6.1 — Google Gemini API Integration

### 1. Integration Architecture & Official Endpoints
ResolveIQ's `AiIntelligenceService` directly integrates with Google's official Gemini API (`generativelanguage.googleapis.com`):
- **GenerateContent Endpoint**: `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- **Authentication**: Passed securely via the `x-goog-api-key` HTTP header. Never placed in URL query parameters, preventing secret leakage in access logs or proxies.
- **Structured JSON Mode**: Configured with `generationConfig: { responseMimeType: "application/json", temperature: 0.2 }` to ensure strictly valid JSON responses matching application DTO schemas.
- **System Instructions**: Behavior, persona, IT taxonomy, and prompt-injection safety rules are passed in Google's native `systemInstruction` field.
- **Active Verified Models**:
  - `gemini-1.5-flash` (Default): High-speed, cost-effective multimodal model optimized for low-latency classification and troubleshooting.
  - `gemini-1.5-pro`: Mid-size model suited for deep analytical reasoning across complex incident tickets.
  - `gemini-2.0-flash`: Next-generation Flash model with accelerated latency and throughput.

### 2. Configuration & Environment Variables
The application uses .NET 10 configuration with environment variable hierarchy:
| Setting | Environment Variable | Default | Description |
| :--- | :--- | :--- | :--- |
| `Ai:Enabled` | `AI__Enabled` | `false` | Master toggle for AI capabilities. |
| `Ai:Provider` | `AI__Provider` | `Gemini` | Selected AI provider (`Gemini`, `OpenAI`, `Ollama`). |
| `Ai:Gemini:ApiKey` | `AI__Gemini__ApiKey` | `""` | Google Gemini API key. Read from User Secrets or environment variable. |
| `Ai:Gemini:Model` | `AI__Gemini__Model` | `gemini-1.5-flash` | Configured Gemini model name. |
| `Ai:Gemini:Endpoint`| `AI__Gemini__Endpoint` | `https://generativelanguage.googleapis.com/v1beta` | Google API base endpoint. |
| `Ai:TimeoutSeconds` | `AI__TimeoutSeconds` | `15` | Request timeout before triggering 504 Gateway Timeout. |

### 3. Local Development Setup (.NET User Secrets)
To configure your Google Gemini API key securely during local development without committing secrets:

```powershell
# 1. Navigate to the API project directory
cd c:\Users\nauma\ResolveIQ\ResolveIQ.Api

# 2. Enable the AI engine and set the Gemini provider
dotnet user-secrets set "Ai:Enabled" "true"
dotnet user-secrets set "Ai:Provider" "Gemini"

# 3. Supply your Gemini API key (from Google AI Studio)
dotnet user-secrets set "Ai:Gemini:ApiKey" "your-gemini-api-key-here"

# 4. (Optional) Configure model name
dotnet user-secrets set "Ai:Gemini:Model" "gemini-1.5-flash"
```

Alternatively, set environment variables in your terminal or container environment:
```bash
export AI__Enabled=true
export AI__Provider=Gemini
export AI__Gemini__ApiKey="your-gemini-api-key-here"
export AI__Gemini__Model="gemini-1.5-flash"
```

### 4. Data Protection & Privacy Notice
> [!IMPORTANT]
> **Data Handling Notice**: When AI features are enabled and invoked, submitted ticket titles and descriptions are transmitted via TLS to Google's Generative Language API servers. Passwords, password hashes, JWTs, user email addresses, and database credentials are **never** transmitted. Review Google's API Terms of Service and data-use policies prior to enabling external provider calls in production environments.

### 5. Error Mapping, Resiliency & Bounded Retries
- **Unconfigured Engine**: Returns HTTP 503 Service Unavailable with RFC 7807 ProblemDetails. Zero external calls are made.
- **Authentication Failure (401 / 403)**: Returns HTTP 502 Bad Gateway with a sanitized message. Secret keys are never exposed in exception messages or response bodies.
- **Quota / Rate Limit (429)**: Returns HTTP 429 Too Many Requests with descriptive ProblemDetails.
- **Timeout**: Returns HTTP 504 Gateway Timeout when inferences exceed `Ai:TimeoutSeconds` (default 15s).
- **Transient Provider 503**: Implements a bounded single-retry policy with a 500ms delay. Never retries indefinitely.
- **Fail-Safe Ticket Operations**: Normal ticket creation, assignment, comment addition, and status transitions operate 100% independently of AI provider uptime.

---

## Running the Automated Test Suite

To run all automated unit, integration, and regression tests:

```powershell
# Run backend test suite (151 tests)
dotnet test --configuration Release

# Run frontend test suite (69 tests)
cd ResolveIQ.Client
npm test
```

**Backend Test Results**: All **151 tests pass** across 14 test suites with 0 failures and 0 skipped.
**Frontend Test Results**: All **69 tests pass** across 8 test suites with 0 failures and 0 skipped.

---

## Batch 7: React + TypeScript Frontend Foundation and Authentication

The ResolveIQ frontend application is located in `ResolveIQ.Client`. It is built with **React 18**, **TypeScript**, **Vite**, and **Tailwind CSS**.

### 1. Technology Stack
- **Framework**: React 18 with TypeScript 5
- **Build Tool**: Vite 6 (ESM dev server and optimized Rollup production bundler)
- **Styling**: Tailwind CSS with custom enterprise ITSM theme tokens and Lucide React icons
- **Routing**: React Router v6 with declarative route guards and role-based redirect matrix
- **Testing**: Vitest + React Testing Library + jsdom

### 2. Architecture & Design Patterns
- **Centralized Typed API Client (`src/services/api.ts`)**:
  - Dynamically configured via `VITE_API_BASE_URL` (defaults to `http://localhost:5022` / `/api`).
  - Automatically attaches `Authorization: Bearer <token>` to protected endpoints.
  - Parses RFC 7807 ProblemDetails and standard error payloads into typed `ApiClientError` instances with status code and error dictionaries.
  - Transparent support for AbortSignal cancellation, 401 unauthenticated redirect triggers, 403 forbidden states, 404 not found, 429 rate limit errors, and 5xx server faults.
  - Zero mock fallbacks: never fabricates fake responses when backend requests fail.
- **Authentication Context (`src/context/AuthContext.tsx`)**:
  - Manages reactive user session, JWT decoding, roles, and error states.
  - Supported roles: `Employee`, `Agent`, `Admin`.
  - Seamless login, registration, and logout flows.
- **Role-Based Protected Routes (`src/components/ProtectedRoute.tsx`)**:
  - Validates authentication status and enforces role authorization arrays.
  - Unauthorized role access redirects to a dedicated `ForbiddenPage` (`/forbidden`) or login.
  - Authenticated landing redirection directs users to their respective dashboards:
    - `Employee` -> `/employee/dashboard`
    - `Agent` -> `/agent/dashboard`
    - `Admin` -> `/admin/dashboard`
- **Shared Enterprise ITSM Layout (`src/components/AppLayout.tsx`)**:
  - Collapsible, mobile-responsive sidebar with role-aware navigation links.
  - Top header displaying page title, live backend connectivity status, current user profile, and quick logout.
  - Accessible dialog modals, status pills, and priority badges (`PriorityBadge`, `StatusBadge`, `RoleBadge`).
- **Initial Dashboards & Workspaces**:
  - **Employee Workspace**: Ticket metrics, submitted tickets view, priority filters, and interactive "Submit New Ticket" modal.
  - **Agent Workspace**: Assigned queue overview, SLA breach warnings, and quick ticket status transitions (`Open` -> `InProgress` -> `Resolved`).
  - **Admin Workspace**: System-wide operational analytics (`/api/Tickets/dashboard`), registered user governance (`/api/Users`), and live system health diagnostics (`/health/ready`).
  - **Knowledge Base Workspace**: Searchable FAQ and ITSM self-service reference articles (`/api/KnowledgeBase`).

### 3. Token Storage Architecture & Security Tradeoff
> [!NOTE]
> **Token Storage Decision**: The backend uses JWT Bearer tokens issued in the HTTP response body (`AuthResponse.token`).
> - **Tradeoff Analysis**: Storing tokens in `localStorage` makes credentials persistent across browser restarts, exposing them to cross-session retrieval on shared or public workstations. `HttpOnly` cookies provide optimal protection against cross-site scripting (XSS), but require cookie-based authentication and CSRF token mechanics in the ASP.NET Core backend.
> - **Implemented Safe Approach**: In compliance with the backend's Bearer token design, tokens are stored in `sessionStorage`. This isolates tokens strictly to the active browser tab/session, clearing them immediately upon tab closure while persisting through page refreshes.

### 4. Setup & Running Locally

#### Step A: Start the Backend Web API
Ensure PostgreSQL is running and the database is migrated:
```powershell
cd c:\Users\nauma\ResolveIQ\ResolveIQ.Api
dotnet run
```
The API listens on `http://localhost:5022` (and `https://localhost:7038`). Development CORS allows `http://localhost:5173`.

#### Step B: Configure and Start the Frontend Application
```powershell
cd c:\Users\nauma\ResolveIQ\ResolveIQ.Client

# Copy environment template if not present
copy .env.example .env

# Install dependencies
npm install

# Start development server
npm run dev
```
Open your browser at `http://localhost:5173`.

#### Step C: Build for Production & Typecheck
```powershell
cd c:\Users\nauma\ResolveIQ\ResolveIQ.Client
npm run build
```

#### Step D: Run Frontend Automated Tests
```powershell
cd c:\Users\nauma\ResolveIQ\ResolveIQ.Client
npm test
```

---

## Batch 8: Complete Employee Portal

Batch 8 implements the complete end-to-end Employee Portal within `ResolveIQ.Client`, strictly integrating with real backend routes, authorization boundaries, SLA tracking, and Gemini AI endpoints.

### 1. Workspaces & Pages Completed
- **Employee Dashboard (`/employee/dashboard`)**:
  - Live ticket metrics (Total, Open, In Progress, Resolved) powered by `GET /api/Tickets/dashboard`.
  - Recent support tickets table with SLA countdown targets, status pills, and direct "View Details" links.
  - Search and status filters, loading spinners, empty states, and error banner with retry mechanics.
  - Action buttons linking directly to `/employee/tickets/new` and quick modal form.
- **Ticket Submission (`/employee/tickets/new`)**:
  - Validated Title (1-150 chars, required) and Description (1-4000 chars, required).
  - Priority selector (`Low`, `Medium`, `High`, `Critical`) defaulting to `Medium` with detailed SLA targets reference.
  - Duplicate submission prevention (`isSubmitting` lock on submit buttons).
  - Preserves user input drafts upon backend validation or network error (never clears form on failure).
  - **AI Classification Assistant**:
    - Optional action invoking `POST /api/AI/classify-ticket`.
    - Returns suggested category, subcategory, recommended priority, heuristic confidence score, and rationale.
    - User can explicitly review and accept suggestions via "Apply Suggested Priority" — never silently overwrites form inputs.
    - Handles offline/disabled AI (503) and rate limits (429) without hindering ticket creation.
  - On successful submission, displays confirmation and redirects to the created ticket (`/employee/tickets/:id`).
- **My Tickets Page (`/employee/tickets`)**:
  - Scoped strictly to the authenticated employee's tickets via backend `GET /api/Tickets` authorization (`CreatedByUserId == currentUserId`).
  - Search by ticket title, description, or numeric `#ID`.
  - Filter by status (`Open`, `InProgress`, `Resolved`) and priority (`Low`, `Medium`, `High`, `Critical`).
  - Column sorting with deterministic tie-breaking.
  - Server-side pagination controls with item counts and total pages.
  - Direct links to `/employee/tickets/:id`.
- **Ticket Details, Conversation & AI Troubleshooting (`/employee/tickets/:id`)**:
  - **Overview**: Title, description, creator, assigned agent, status badge, priority badge, and creation timestamp.
  - **SLA Tracking**: First-response due date/status and resolution due date/status (`GET /api/Tickets/{id}/sla`).
  - **Audit History**: Chronological timeline of lifecycle actions, actors, and field changes (`GET /api/Tickets/{id}/history`).
  - **Conversation & Comments**:
    - Full comment thread with author identity, timestamp, and content (`GET /api/tickets/{id}/comments`).
    - Add Comment box (`POST /api/tickets/{id}/comments`).
    - Validates non-empty content and locks during submission.
    - Retains comment drafts in textarea on error.
    - Preserves backend first-response rule: employee comments do not count as support agent first response.
  - **AI Troubleshooting Assistant**:
    - Dedicated tab with "Generate AI Advice" invoking `POST /api/AI/troubleshoot/{id}`.
    - Displays problem summary, hypothesized causes (prominently labeled as hypotheses for verification), ordered troubleshooting steps, and clarifying questions.
    - Grounded against ResolveIQ Knowledge Base articles and permission-scoped similar resolved cases.
    - Handles 503 (offline engine), 429 (rate limit), and 504 (timeout) gracefully.
    - **Zero Silent Modification**: Inferences are strictly advisory and never change ticket status, assignment, or priority.
  - **Security Boundaries**: Handles 403 Forbidden with security restriction notice, and 404 Not Found.

### 2. Actual API Routes Integrated
| Endpoint | Method | Role Scoping / Auth | Purpose |
| :--- | :--- | :--- | :--- |
| `/api/Tickets/dashboard` | `GET` | Authenticated (Employee-scoped) | Retrieve caller ticket metric counts |
| `/api/Tickets` | `GET` | Authenticated (Employee-scoped) | Retrieve caller tickets with search, filtering, pagination |
| `/api/Tickets/{id}` | `GET` | Caller must own ticket | Retrieve specific ticket entity |
| `/api/Tickets` | `POST` | Authenticated | Create new ticket (`Title`, `Description`, `Priority`) |
| `/api/Tickets/{id}/sla` | `GET` | Caller must own ticket | Retrieve SLA milestone targets and statuses |
| `/api/Tickets/{id}/history` | `GET` | Caller must own ticket | Retrieve audit log history entries |
| `/api/tickets/{id}/comments` | `GET` | Caller must own ticket | Retrieve ticket conversation comments |
| `/api/tickets/{id}/comments` | `POST` | Caller must own ticket | Post a new comment to the ticket |
| `/api/AI/classify-ticket` | `POST` | Authenticated | Heuristic category and priority suggestion |
| `/api/AI/troubleshoot/{id}` | `POST` | Caller must own ticket | AI troubleshooting grounded on KB & similar cases |

---

## Batch 9: Complete Agent Workspace Implementation

Batch 9 implements the complete Agent Workspace in `ResolveIQ.Client`, built on real .NET 10 API endpoints, strict role-based access control, SLA milestone monitoring, official support responses, and advisory AI integration.

### 1. Workspaces & Pages Completed

- **Agent Dashboard (`/agent/dashboard`)**:
  - Live agent-scoped metrics (`Assigned Total`, `Open`, `In Progress`, `Urgent High/Critical`, `At Risk`, `Breached SLA`) fetched directly from `GET /api/Tickets/dashboard`.
  - Prominent SLA Warning Alert banner displayed when any ticket is at imminent risk of breaching or already breached.
  - Quick action status transitions ("Start Work" &rarr; `InProgress`, "Resolve" &rarr; `Resolved`) directly from the table, preserving ticket priority to satisfy backend `UpdateTicketRequest` requirements.
  - Search toolbar and status/priority filters for immediate triage.
  - Loading skeletons, empty states, and error handling with retry capability.
  - Fast navigation to `/agent/assigned` and `/agent/tickets/:id`.

- **Assigned Tickets Workspace (`/agent/assigned`)**:
  - Dedicated agent queue powered by `GET /api/Tickets/assigned` (scoped strictly to tickets where `AssignedToUserId == currentUserId`).
  - Search by ticket title, description, or exact ticket ID.
  - Filter by Status (`Open`, `InProgress`, `Resolved`) and Priority (`Low`, `Medium`, `High`, `Critical`).
  - Server-side sorting with deterministic tie-breaking across columns (`ID`, `Title`, `Priority`, `Status`, `Resolution Due`, `Created At`).
  - Server-side pagination controls (Previous/Next, page indicators, total counts).
  - SLA indicators displaying first-response and resolution deadlines with visual status badges (`Met`, `Breached`, `At Risk`, `On Track`).
  - Quick status actions to update incident lifecycle directly from the queue.

- **Agent Ticket Management & SLA Workflow (`/agent/tickets/:id` or `/tickets/:id`)**:
  - **Role-Aware Navigation**: Breadcrumb automatically adapts to user role, linking to `/agent/assigned` for Agents and Admins.
  - **Agent Operations & Ticket Controls**:
    - Status selectors (`Open`, `InProgress`, `Resolved`) and Priority selectors (`Low`, `Medium`, `High`, `Critical`).
    - Updates executed via `PATCH /api/Tickets/{id}` transmitting both `Status` and `Priority`.
    - Authorization boundary strictly enforced: Agents attempting to modify unassigned tickets receive a 403 Forbidden alert.
    - Successful updates immediately refresh ticket details, SLA milestones, and audit history.
    - Ticket reassignment is strictly prevented for agents (backend restricts `PATCH /api/Tickets/{id}/assign` to `Admin`).
  - **SLA Milestone Visualization**:
    - First Response Milestone: Target due date, official response timestamp, and status badge (`Met`, `Breached`, `On Track`, `At Risk`).
    - Resolution Milestone: Target resolution deadline, resolved timestamp, and status badge.
  - **Support Response Workflow**:
    - Support reply form explicitly labeled for customer-facing responses.
    - Posting a comment (`POST /api/tickets/{id}/comments`) as an Agent or Admin automatically triggers the backend's official `FirstRespondedAt` milestone if pending.
    - Comment drafts are strictly preserved in the textarea upon network or validation failures.
    - Refreshes SLA status and audit trail upon comment submission.

- **AI-Assisted Support & Intelligence Engine**:
  - **AI Priority Advisor**:
    - Calls `POST /api/AI/recommend-priority` with `{ ticketId }`.
    - Displays AI Recommended Priority, Confidence Score (with calibration notes), and detailed enterprise impact Rationale *separately* from current priority.
    - **Zero Silent Mutation**: Ticket priority remains unchanged until the agent explicitly reviews and clicks "Apply {priority} Priority".
  - **ITSM AI Troubleshooting Assistant**:
    - Calls `POST /api/AI/troubleshoot/{id}`.
    - Displays problem summary, hypothesized root causes (prominently flagged as hypotheses for verification), ordered troubleshooting steps, and clarifying questions.
    - Grounded against enterprise Knowledge Base articles and permission-scoped similar resolved cases.
    - Resilient error handling for 503 (offline/disabled AI provider), 429 (rate limit exceeded), and 504 (timeout) without impairing core ticketing workflows.

### 2. Actual Backend Routes Integrated

| Endpoint | Method | Role / Security Boundary | Purpose |
| :--- | :--- | :--- | :--- |
| `/api/Tickets/dashboard` | `GET` | Authenticated (Agent-scoped) | Retrieve caller-assigned metric counts |
| `/api/Tickets/assigned` | `GET` | Authenticated (`Agent`, `Admin`) | Retrieve caller-assigned ticket queue with search/filter/sort/pagination |
| `/api/Tickets/{id}` | `GET` | Caller owns or is assigned | Retrieve complete ticket entity |
| `/api/Tickets/{id}` | `PATCH` | Agent must be assigned to ticket | Update ticket status and priority (`UpdateTicketRequest`) |
| `/api/Tickets/{id}/sla` | `GET` | Caller owns or is assigned | Retrieve first-response and resolution SLA milestones |
| `/api/Tickets/{id}/history` | `GET` | Caller owns or is assigned | Retrieve chronological lifecycle audit log |
| `/api/tickets/{id}/comments` | `GET` | Caller owns or is assigned | Retrieve ticket conversation messages |
| `/api/tickets/{id}/comments` | `POST` | Caller owns or is assigned | Post comment (records `FirstRespondedAt` if support user) |
| `/api/AI/recommend-priority` | `POST` | Authenticated (Agent / Admin) | Advisory SLA priority recommendation based on impact & urgency |
| `/api/AI/troubleshoot/{id}` | `POST` | Caller owns or is assigned | Grounded ITSM troubleshooting analysis |

### 3. Confirmed Authorization & Status Transition Rules

1. **Status Transitions**: Allowed status values are `Open`, `InProgress`, and `Resolved`.
2. **Priority Values**: Allowed priority values are `Low`, `Medium`, `High`, and `Critical`.
3. **Payload Contract**: `UpdateTicketRequest` requires both `Status` and `Priority`. Quick transitions must preserve current priority.
4. **Agent Edit Boundary**: `TicketsController.UpdateTicket` enforces that Agents can only update tickets where `ticket.AssignedToUserId == currentUserId`. Unassigned tickets or tickets assigned to another agent return `403 Forbidden`.
5. **Reassignment Boundary**: `PATCH /api/Tickets/{id}/assign/{agentId}` is restricted to `[Authorize(Roles = "Admin")]`. Agents cannot assign or reassign tickets.
6. **First Response Milestone**: When an Agent or Admin submits the first comment on a ticket, `TicketCommentsController` automatically records `FirstRespondedAt = DateTime.UtcNow` and logs a `FirstResponse` entry in `TicketHistory`.
7. **AI Non-Mutation**: AI recommendations (`recommend-priority`, `troubleshoot`, `classify-ticket`) are strictly advisory and never modify ticket status, priority, or SLA deadlines in the database.

### 4. Automated Verification Results

- **Client Tests (`npm test`)**: 50 passed across 7 test suites (100% pass rate).
  - `src/test/agentWorkspace.test.tsx`: 12 passed
  - `src/test/employeePortal.test.tsx`: 9 passed
  - `src/test/ticketDetail.test.tsx`: 7 passed
  - `src/test/routes.test.tsx`: 7 passed
  - `src/test/api.test.ts`: 8 passed
  - `src/test/auth.test.tsx`: 4 passed
  - `src/test/dashboards.test.tsx`: 3 passed
- **Client Build (`npm run build`)**: Exit code 0 (1613 modules transformed, 0 TypeScript errors).
- **Backend Tests (`dotnet test --configuration Release`)**: 151 passed across 1 test suite, 0 failures.

### 5. Manual Steps to Test as an Agent

1. **Login as an Agent**:
   - Navigate to `http://localhost:5173/login`.
   - Sign in with an agent account (e.g. `agent@resolveiq.com` / `Agent123!`).
   - You will be automatically redirected to `/agent/dashboard`.
2. **Review Scoped Dashboard**:
   - Verify assigned ticket metric cards (Total, Open, In Progress, High/Critical, At Risk, Breached SLA).
   - Verify the SLA warning alert banner if any assigned tickets are at risk or breached.
   - Test quick status transitions ("Start Work" / "Resolve") directly from the dashboard table.
3. **Explore Assigned Queue**:
   - Click "All Assigned" or navigate to `/agent/assigned` via the sidebar.
   - Search by ticket title, description, or ID.
   - Filter by status and priority; test column sorting and pagination controls.
4. **Manage an Incident**:
   - Click on an assigned ticket to navigate to `/agent/tickets/:id`.
   - Verify role-aware back link returns to `/agent/assigned`.
   - In the **Agent Operations** panel, change status to `InProgress` or update priority. Confirm immediate refresh of ticket status, SLA indicators, and audit history.
   - In the **Comments** tab, enter a support response and submit. Confirm that the comment is logged and that the First Response SLA milestone records your response timestamp.
5. **Test AI Priority Advisor & Troubleshooting**:
   - Switch to the **AI Troubleshooting Assistant** tab.
   - Click "Evaluate Priority" in the AI Priority Advisor card.
   - Verify that the recommended priority, confidence score, and rationale appear *without* changing the ticket's priority.
   - Click "Apply {priority} Priority" to explicitly update the ticket.
   - Click "Generate AI Advice" to inspect grounded hypotheses, troubleshooting steps, KB articles, and similar resolved tickets.

---

## Batch 10 Feature Implementations — Admin Dashboard & Operations Management

### 1. Overview
Batch 10 delivers the complete **Admin Workspace** within the React + TypeScript frontend (`ResolveIQ.Client`) backed by real .NET 10 Web API endpoints, role-based authorization, enterprise SLA monitoring, notifications, and user governance.

### 2. Core Components & Pages Created / Upgraded
- **Admin Dashboard (`src/pages/AdminDashboard.tsx`)**:
  - Live enterprise-wide metrics (`TotalTickets`, `OpenTickets`, `InProgressTickets`, `ResolvedTickets`, `AtRiskTickets`, `BreachedTickets`).
  - Enterprise SLA Performance Panel showing real `SlaCompliancePercentage`, `SlaChecksMet`, and `SlaChecksBreached`.
  - Active SLA Breach Escalation feed fetched directly from `GET /api/Tickets/escalations`.
  - System Readiness monitor connected to `GET /health/ready` probe.
  - Recent platform incident queue with direct navigation to ticket details.
  - Graceful handling of loading states, network transport failures, and retry workflows.
- **User Management Page (`src/pages/UserManagementPage.tsx`)**:
  - Registered account directory powered by `GET /api/Users` with ID, full name, email, role, and registration date.
  - Instant client-side search across names, emails, and IDs.
  - Role filter dropdown (All Roles, Employees, Agents, Administrators).
  - Role transition governance (`PATCH /api/Users/{id}/role`): Safe promotion of Employees to Agents and reversion of Agents to Employees.
  - Mandatory confirmation modal before submitting role modifications.
  - Server-side safeguards: Admin accounts are explicitly protected (`Admin accounts cannot be changed through this endpoint`).
  - State reflection only after server confirmation; zero fabricated success states.
- **Ticket Operations & SLA Escalation Monitoring (`src/pages/TicketsPage.tsx`)**:
  - Standard operations queue (`mode="all"`) with server-side search, status and priority filtering, column sorting (`CreatedAt`, `Priority`, `Status`, `Title`, `Id`), and pagination.
  - Direct links to `/tickets/:id` for full audit trail, comments, and lifecycle inspection.
  - Admin Ticket Assignment modal: Admins can assign or reassign unassigned/assigned tickets to active agents fetched from `GET /api/Users` (role == `Agent`) via `PATCH /api/Tickets/{id}/assign/{agentId}`.
  - Dedicated SLA Breach Escalations Queue (`mode="escalations"`): Powered by `GET /api/Tickets/escalations`, displaying real breach reasons, affected ticket links, assignees, and escalation timestamps.
- **In-App Notifications (`src/components/NotificationsMenu.tsx` & `src/components/Header.tsx`)**:
  - Real unread badge count in global header powered by `GET /api/Notifications/unread-count`.
  - Responsive dropdown with notification summaries, timestamps, and direct ticket links (`/tickets/:id`).
  - Interactive "Mark read" (`PATCH /api/Notifications/{id}/read`) and "Mark all read" (`PATCH /api/Notifications/read-all`).
- **Role-Based Navigation & Security Boundaries (`src/components/Sidebar.tsx` & `src/components/ProtectedRoute.tsx`)**:
  - Dynamic navigation links tailored to role (Admin gets `User Management`, `System Health`, `All Tickets`, `SLA Escalations`).
  - Route protection restricting `/admin/*` routes strictly to users with the `Admin` role claim, redirecting unauthorized users to `/forbidden`.

### 3. Integrated Real Backend API Routes
| HTTP Method | Route | Authorization | Description |
|:---|:---|:---|:---|
| `GET` | `/api/Tickets/dashboard` | Employee / Agent / Admin (scoped) | Global enterprise metrics for Admins |
| `GET` | `/api/Users` | Admin | System user directory (no sensitive credentials exposed) |
| `PATCH` | `/api/Users/{id}/role` | Admin | Updates user role (Employee <-> Agent; Admin protected) |
| `GET` | `/api/Tickets` | Employee / Agent / Admin (scoped) | Enterprise ticket query with search, filter, sort, and pagination |
| `PATCH` | `/api/Tickets/{id}/assign/{agentId}` | Admin | Assigns ticket to designated agent, logs audit history |
| `GET` | `/api/Tickets/escalations` | Admin | Retrieves all tickets with SLA breach escalation events |
| `GET` | `/api/Notifications` | Authenticated | Paginated notifications for caller |
| `GET` | `/api/Notifications/unread-count` | Authenticated | Unread notification count badge |
| `PATCH` | `/api/Notifications/{id}/read` | Authenticated | Marks single notification as read |
| `PATCH` | `/api/Notifications/read-all` | Authenticated | Marks all caller notifications as read |
| `GET` | `/health/ready` | Public / System | Readiness health probe for API and database |

### 4. Automated Verification Results
- **Client Tests (`npm test`)**: **69 passed** across 8 test suites (**100% pass rate**, 0 failures).
  - `src/test/adminWorkspace.test.tsx`: 19 passed
  - `src/test/agentWorkspace.test.tsx`: 12 passed
  - `src/test/employeePortal.test.tsx`: 9 passed
  - `src/test/ticketDetail.test.tsx`: 7 passed
  - `src/test/routes.test.tsx`: 7 passed
  - `src/test/api.test.ts`: 8 passed
  - `src/test/auth.test.tsx`: 4 passed
  - `src/test/dashboards.test.tsx`: 3 passed
- **Client Production Build (`npm run build`)**: Exit code 0 (1614 modules transformed, 0 TypeScript errors).
- **Backend Tests (`dotnet test --configuration Release`)**: **151 passed** across 1 test suite (**100% pass rate**, 0 failures).

### 5. Manual Verification Steps for Admin Workspace
1. **Login as an Admin**:
   - Navigate to `http://localhost:5173/login`.
   - Sign in with administrative credentials (e.g., `admin@resolveiq.com` / `Admin123!`).
   - Confirmation: User lands directly on `/admin/dashboard`.
2. **Verify Operations Dashboard**:
   - Inspect the top cards: Total, Open, In Progress, Resolved, At Risk SLA, and Breached SLA.
   - Verify the SLA Compliance percentage bar and Checks Met vs Breached counters.
   - Verify the system readiness probe card displays API Gateway Online and PostgreSQL DB status.
   - Inspect recent escalation events and recent platform tickets.
3. **Test User Governance**:
   - Navigate to **User Management** (`/admin/users`) via the sidebar.
   - Search by user name or email; filter by role.
   - Confirm Admin accounts are marked as `Admin Protected` (no role change buttons).
   - Click "Promote to Agent" on an Employee.
   - Confirm that a modal appears asking for explicit confirmation.
   - Click "Confirm Change" and verify that the user's role updates and a success message appears.
4. **Test Ticket Assignment & Operations**:
   - Navigate to **All Tickets** (`/tickets`).
   - Search by ticket title or filter by status/priority. Test column sorting (CreatedAt, Priority, Title).
   - For an unassigned ticket, click "Assign".
   - Select an Agent from the dropdown and submit. Confirm assignment updates immediately.
5. **Review SLA Escalations Queue**:
   - Navigate to **SLA Escalations** (`/tickets/escalations`).
   - Review escalated tickets, breach reasons, and timestamps.
   - Click "Inspect" to navigate to the ticket detail page and view full audit history.
6. **Verify Notifications Center**:
   - Click the Bell icon in the header.
   - View recent notifications with links to relevant ticket IDs.
   - Test "Mark read" and "Mark all read" buttons. Confirm unread count badge updates accordingly.

---

## Batch 11: Final Integration, Security Audit & Launch Readiness

### 1. Launch Readiness Verdict: `READY FOR STAGING`

The ResolveIQ application (.NET 10 Web API + React 18 / TypeScript frontend) has undergone comprehensive audit across all architectural layers, automated test suites, end-to-end role workflows, security controls, and deployment configurations.

| Layer / Verification Check | Result | Evidence |
| :--- | :---: | :--- |
| **Backend Release Build** | **PASS** | Exit code 0, 0 warnings, 0 errors |
| **Backend Test Suite (xUnit)** | **PASS** | **151 passed**, 0 failed across 14 test classes |
| **Backend Package Audit** | **PASS** | 0 vulnerable packages identified via `dotnet list package --vulnerable` |
| **Frontend Production Build** | **PASS** | Exit code 0, 1614 modules bundled, 0 TypeScript errors |
| **Frontend Test Suite (Vitest)** | **PASS** | **69 passed**, 0 failed across 8 test suites |
| **Frontend Dependency Audit** | **PARTIAL** | 12 dev/toolchain advisories documented; no SSR in client SPA |
| **End-to-End Role Workflows** | **PASS** | Employee, Agent, and Admin workflows verified live & in tests |
| **Backend Security Boundaries** | **PASS** | Strict server-side RBAC, ownership checks, 403 Forbidden enforcement |
| **JWT & Authentication** | **PASS** | HMAC-SHA256, 256-bit key enforcement, audience/issuer validation |
| **Rate Limiting & Abuse Defense** | **PASS** | Auth (10/min), General (100/min), AI (20/min) policies verified |
| **Centralized Error Handling** | **PASS** | RFC 7807 ProblemDetails sanitized in production, no stack traces leaked |
| **SLA & Notifications Consistency**| **PASS** | Milestones calculated, background monitor idempotent, employee comments excluded |
| **Live Application Smoke Tests** | **PASS** | Verified against running API (port 5022) & Client (port 5173) |
| **Deployment Configuration** | **PASS** | Docker multi-stage build, unprivileged user, gated CD (`autoDeploy: false`) |

### 2. Verified Security Controls & Hardening

1. **Server-Side Authorization Boundaries**:
   - Employees are restricted to tickets where `CreatedByUserId == currentUserId`. Attempts to view unauthorized tickets return HTTP 403 Forbidden.
   - Agents can only modify tickets where `AssignedToUserId == currentUserId`. Updating unassigned tickets or tickets assigned to other agents returns HTTP 403 Forbidden.
   - Ticket assignment (`PATCH /api/Tickets/{id}/assign/{agentId}`) and SLA escalation queries (`GET /api/Tickets/escalations`) are strictly restricted to `Admin`.
   - User governance (`GET /api/Users`, `PATCH /api/Users/{id}/role`) is restricted to `Admin`, with built-in server-side protection preventing modification of Admin accounts.
2. **Authentication & Password Security**:
   - Passwords hashed using ASP.NET Core Identity's PBKDF2 implementation with HMAC-SHA512.
   - Password reset tokens use cryptographically random 32-byte values, stored strictly as SHA-256 hashes with 15-minute expiration and single-use enforcement.
   - User profile endpoints (`GET /api/Users/me`, `GET /api/Users/{id}`, `GET /api/Users`) never expose password hashes or sensitive security stamps.
3. **HTTP Response Security Headers**:
   - `X-Content-Type-Options: nosniff`
   - `X-Frame-Options: DENY`
   - `Referrer-Policy: strict-origin-when-cross-origin`
   - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`
   - `Permissions-Policy: camera=(), microphone=(), geolocation=()`
   - `X-Permitted-Cross-Domain-Policies: none`
   - `X-Correlation-ID: <guid>`
4. **Rate Limiting & Abuse Protection**:
   - Partitioned fixed-window rate limiter active across all endpoints:
     - `auth`: 10 requests/minute per client IP (protects login, register, password reset).
     - `general`: 100 requests/minute per user ID or IP (protects tickets, comments, users, notifications).
     - `ai`: 20 requests/minute per user ID (protects Gemini AI inference calls).
5. **AI Safety & Non-Mutation**:
   - Ticket titles and descriptions are sanitized and wrapped in structural delimiter tags (`<ticket_title>`, `<ticket_description>`) to prevent prompt-injection attacks.
   - AI recommendations (`classify-ticket`, `recommend-priority`, `troubleshoot`) are strictly advisory and never modify ticket fields or SLA deadlines in the database.
   - When AI is unconfigured (`Ai:Enabled = false`), endpoints return HTTP 503 Service Unavailable with RFC 7807 ProblemDetails without making external network calls.

### 3. SLA & Data Consistency Controls

1. **Milestone Deadlines**:
   - SLA targets: Critical (15 min response / 4 hr resolution), High (1 hr response / 8 hr resolution), Medium (4 hr response / 24 hr resolution), Low (8 hr response / 72 hr resolution).
   - Priority changes automatically recalculate resolution deadlines proportionally from ticket creation time.
2. **First Response SLA Milestone Rule**:
   - Employee comments do NOT count as an agent's first response (`ticket.FirstRespondedAt` remains null).
   - Agent or Admin comments automatically record `ticket.FirstRespondedAt = DateTime.UtcNow` and append a `FirstResponse` audit log entry.
3. **Background Breach Monitoring Idempotency**:
   - Hosted service monitors approaching deadlines (15-min warning window) and breaches.
   - Deterministic deduplication keys (`sla_warning_first_response_{id}`, `sla_breached_resolution_{id}`) ensure background polling never generates duplicate notification records.

### 4. Known Limitations & Residual Risks

1. **Frontend Dev Toolchain Advisories**:
   - `npm audit` reports 12 vulnerabilities in dev/toolchain packages (`vitest` &rarr; `tinypool`, `tailwindcss` &rarr; `chokidar`, `react-router-dom` &rarr; `express`).
   - **Risk Assessment**: Low / Acceptable for Staging. The Vite frontend is a client-side Single Page Application (SPA); no server-side Node.js rendering (SSR) is executed in production. Remediating these advisories requires major breaking version upgrades (`vitest@5`, `tailwindcss@4`, `react-router-dom@7`) which should be scheduled as a planned platform upgrade cycle.
2. **Production Deployment Automation Gate**:
   - Render blueprint (`render.yaml`) has been explicitly updated with `autoDeploy: false` to ensure production deployments require deliberate human authorization.
3. **Secret Provisioning for Staging / Production**:
   - Ensure `JWT_KEY`, `POSTGRES_PASSWORD`, and `AI__Gemini__ApiKey` are injected via secure environment variables or cloud secret managers. Never commit real credentials to source control.

### 5. Staging Deployment Checklist

- [ ] Provision managed PostgreSQL 17 instance on staging cloud provider.
- [ ] Run idempotent migration script: `dotnet ef database update --connection "$STAGING_DATABASE_URL"`
- [ ] Configure environment variables: `ConnectionStrings__DefaultConnection`, `Jwt__Key` (min 32 bytes base64), `Jwt__Issuer`, `Jwt__Audience`, `Cors__AllowedOrigins__0`.
- [ ] Deploy container image built from multi-stage `Dockerfile`.
- [ ] Verify readiness probe: `GET https://staging-api.resolveiq.com/health/ready` &rarr; HTTP 200 `{ "status": "Healthy", "database": "Healthy" }`.
- [ ] Build client SPA with `VITE_API_BASE_URL=https://staging-api.resolveiq.com` and deploy static assets to CDN / host.
- [ ] Perform live smoke test with dedicated staging test accounts.
