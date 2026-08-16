# FPAI Connect

A management system for the **Football Players Association of India**: player welfare casework,
legal matters (FIFA DRC / CAS / PSC / arbitration), finance, governance and voting, events,
documents, tasks and approvals — with role- and department-scoped access enforced on the server.

Built as a replacement for the non-functional Lovable prototype. The design is new; only the
domain (modules, workflow stages, roles) was carried across.

| | |
|---|---|
| **Backend** | .NET 10, ASP.NET Core Minimal APIs, EF Core 10, ASP.NET Core Identity |
| **Frontend** | React 19, Vite 8, TypeScript 6, Tailwind CSS 4, TanStack Query 5, Recharts 3 |
| **Database** | SQLite for development, Azure SQL in production (one connection-string change) |
| **Hosting** | Azure App Service (Linux), Azure Blob Storage for documents |
| **Tests** | 152 xUnit · 35 Vitest · 42 Playwright = **229 passing** |

---

## Quick start

Prerequisites: **.NET 10 SDK** and **Node.js 22+**.

```bash
# Terminal 1 — API on http://localhost:5099
cd backend/src/FpaiConnect.Api
dotnet run

# Terminal 2 — SPA on http://localhost:5173
cd frontend
npm install
npm run dev
```

Open <http://localhost:5173>. The database is created, migrated and seeded automatically on
first run.

### Demo accounts

All use the password `Fpai@Connect2025!` (configurable via `Seed:DemoPassword`).

| Email | Role | Department | Use it to see |
|---|---|---|---|
| `admin@fpai.in` | Super Admin | Executive | Everything, including user management and the audit trail |
| `welfare.head@fpai.in` | Department Head | Welfare | Approve and close welfare cases; read-only elsewhere |
| `legal.head@fpai.in` | Department Head | Legal | Legal matters; cannot write to Finance |
| `finance.head@fpai.in` | Department Head | Finance | Approve/reject vouchers and expenses |
| `welfare.staff@fpai.in` | Staff | Welfare | Create and progress cases, but never approve or close |
| `accountant@external-ca.in` | External Accountant | Finance | Finance only — reconciliation and queries |

To see the approval flow, open **Request access** on the sign-in page, sign up with any
address, then sign in as `admin@fpai.in` and go to **Settings → Access requests**.

> Signing in as the accountant and then visiting Player Welfare is the quickest way to see
> authorization working: the module disappears from the sidebar **and** the API returns 403.

---

## Architecture

```
backend/
  src/FpaiConnect.Domain          entities, enums, role and department constants
  src/FpaiConnect.Application     DTOs, workflow state machines, policy names, abstractions
  src/FpaiConnect.Infrastructure  EF Core, Identity, seeding, file storage, audit interceptor
  src/FpaiConnect.Api             minimal-API endpoints, JWT auth, authorization policies
  tests/FpaiConnect.Tests         unit + integration tests against a real host
frontend/
  src/lib                         API client, auth context, formatting, shared hooks
  src/components                  design system, app shell, document and query panels
  src/pages                       one file per module, plus detail pages
  e2e                             Playwright specs
deploy/main.bicep                 App Service + Azure SQL + Blob Storage + App Insights
```

### Authorization

Two independent dimensions, both enforced server-side:

1. **Module gate** — declarative policies (`Welfare.Read`, `Finance.Approve`, …) decide which
   roles may touch a module at all.
2. **Row scope** — `ICurrentUser.Can{Read,Write,Approve}Department` decides which departments'
   records they may see or change.

| Role | Read | Write | Approve |
|---|---|---|---|
| Super Admin | Everything | Everything | Everything |
| Department Head | All modules, all departments | Own department | Own department |
| Staff | Own department | Own department | Never |
| External Accountant | Finance and documents | Queries and reconciliation | Never |

The frontend mirrors these rules for presentation only — it hides what you cannot do. Every
rule is re-checked on the server, and the E2E suite asserts both layers by calling the API
directly with the UI bypassed.

### Workflows

Transitions are declared once in `WorkflowRules` and enforced on every status change; an
illegal jump returns **409 Conflict** listing what *is* allowed.

- **Welfare** New → Under Review → Assigned → In Progress → Resolved → Closed
- **Legal** Registered → Documents Pending → Filed → Hearing Scheduled → Decision Received → Closed
- **Voucher** Draft → Pending → Approved → Reconciled → Closed (Rejected returns to Draft)
- **Expense** Created → Invoice Attached → Pending Approval → Accountant Review → Reconciled → Closed
- **Task** Todo → In Progress → Blocked → Done
- **Meeting** Scheduled → In Progress → Completed · **Motion** Draft → Open → Passed/Failed
- **Event** Planned → Dispatched → Ongoing → Completed

Approval is **single-step**: the head of the owning department, or a Super Admin, decides.
Requesters cannot approve their own submissions. Approving a voucher or expense also advances
the underlying record, so the two never drift apart.

### Auditing

A `SaveChanges` interceptor writes an append-only `AuditLog` row for every insert, update and
delete, with the acting user, a timestamp and a before/after diff (password hashes and tokens
are excluded). Deletes are soft, so history survives. Super Admins read it at
**User Management → audit**.

---

## Settings

Everything that governs access, plus each person's own appearance preferences, lives under
**Settings**. Sections appear only for roles that may use them.

| Section | Who sees it | What it does |
|---|---|---|
| Appearance | Everyone | Colour scheme, typeface, light/dark |
| Users & roles | Super Admin, Department Head | Accounts, roles, department assignment, password resets |
| Access requests | Super Admin | Approve or decline people who have signed up |
| Departments | Super Admin | Create and rename the departments that scope every record |
| Clubs & vendors | Super Admin, Department Head | Shared reference data |

The old `/users` URL redirects into `Settings → Users & roles`.

### Appearance

Six colour schemes — Pitch Green, Indian Saffron, Royal Blue, Deep Violet, Slate Monochrome
and Crimson — each defined for light *and* dark, giving twelve combinations. Six typefaces:
System Sans, Grotesk, Classic Serif, Rounded, Monospace and High Legibility.

Choices are **stored against the user account**, not the browser, so they follow the person to
any device; they are returned with the sign-in response and cached locally so the first paint
is already correct, with no flash of the wrong theme.

Every typeface is a stack of fonts already present on the user's machine. Nothing is
downloaded, switching is instant, it works offline, and the Content-Security-Policy needs no
exception for a font CDN.

Components never name a colour — they use semantic tokens (`--accent-solid`, `--accent-text`,
`--accent-soft-bg`, `--chrome`…) defined in `src/index.css` and listed in `src/lib/theme.ts`.
Adding a seventh scheme means adding one CSS block and one catalogue entry; no component
changes. Charts read the active accent at runtime, so they recolour with the scheme.

---

## Signing up and being approved

Anyone may request an account, either through **Request access** or by signing in with Google
or Microsoft using an address the system has never seen. In every case:

1. The account is created with status **PendingApproval**, **no role** and **no department**.
2. **No access token is issued** — not a limited one, none at all. An unapproved account
   cannot read a single record, even by calling the API directly.
3. Every Super Admin gets an in-app notification.
4. An administrator approves it in **Settings → Access requests**, choosing the role and
   department at that moment, or declines it with a reason the applicant then sees.

Registering with an address that already exists returns the same "request received" response
as a new one and changes nothing, so the endpoint cannot be used to discover who has an
account. Registration is rate-limited per client address, more tightly than sign-in.

Administrators can still create users directly in **Settings → Users & roles**; that path is
unchanged and skips the queue.

---

## Testing

```bash
# Backend: unit + integration against a real in-memory host
cd backend && dotnet test

# Frontend unit tests
cd frontend && npm run test

# End-to-end (needs the API running on :5099)
cd frontend && npm run e2e
```

The integration tests boot the genuine ASP.NET Core pipeline — real authentication, real
authorization policies, real EF Core against a per-class SQLite file — rather than mocks, so a
passing suite means the shipped pipeline works. `ConcurrencyTests` additionally hammers the
login endpoint with 20 simultaneous sign-ins.

---

## Deploying to Azure

### 1. Provision

```bash
az group create --name fpai-connect-rg --location centralindia

az deployment group create \
  --resource-group fpai-connect-rg \
  --template-file deploy/main.bicep \
  --parameters environmentName=prod \
               sqlAdminLogin='fpaiadmin' \
               sqlAdminPassword='<strong-password>' \
               jwtSigningKey='<at least 32 random characters>' \
               googleClientId='<optional>'
```

This creates the App Service plan and web app (health check on `/api/health`), Azure SQL server
and database, a private Blob container for documents, and Application Insights.

### 2. Deploy

Set the repository secrets `AZURE_CREDENTIALS`, `AZURE_RESOURCE_GROUP`, `AZURE_WEBAPP_NAME`,
`SQL_ADMIN_LOGIN`, `SQL_ADMIN_PASSWORD`, `JWT_SIGNING_KEY` and `GOOGLE_CLIENT_ID`, then run the
**Deploy to Azure** workflow. It builds the SPA into the API's `wwwroot`, publishes, deploys and
smoke-tests `/api/health`.

### Switching to Azure SQL

Nothing in the code changes. The provider is configuration:

```
Database__Provider = SqlServer
ConnectionStrings__Default = Server=tcp:<server>.database.windows.net,1433;Database=...
```

EF Core migrations run automatically at startup. The domain deliberately uses UTC `DateTime`
rather than `DateTimeOffset` so that ordering and comparison behave identically on SQLite and
SQL Server — a `DateTimeOffset` sort works on Azure SQL but throws on SQLite, which would mean
development and production diverging.

> **Production checklist:** set `Seed__Enabled=false`, supply a real `Jwt__SigningKey` (the app
> refuses to start in Production without one of at least 32 characters), and set
> `Storage__Provider=AzureBlob` — App Service's local disk is ephemeral and not shared between
> instances.

### Manual setup with Entra-only SQL and managed identity (no passwords, near-zero cost)

If the App Service and Azure SQL server already exist and Entra authentication is enabled on
the server, both the database and Blob Storage can be reached with **no secret stored
anywhere** — App Service's own system-assigned managed identity is the credential for both.
`Microsoft.Data.SqlClient` and the Blob SDK both authenticate through `Azure.Identity` directly
from the connection string / account URL; no code change is needed to use this path.

**1. Turn on the App Service's managed identity** (skip if already on):

```bash
az webapp identity assign --name <app-service-name> --resource-group <rg>
```

Note the `principalId` it prints — App Service's identity's *display name* in Entra is the
app's own name, which is what the SQL grant below uses.

**2. Grant that identity a database user**, connected as the server's Entra admin (Query editor
in the portal, or `sqlcmd`/Azure Data Studio with Entra MFA auth):

```sql
CREATE USER [<app-service-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<app-service-name>];
ALTER ROLE db_datawriter ADD MEMBER [<app-service-name>];
-- db_ddladmin is required because EF Core migrations run automatically at startup.
ALTER ROLE db_ddladmin ADD MEMBER [<app-service-name>];
```

**3. Create the storage account and container** for documents, then grant the same identity
`Storage Blob Data Contributor` — a role, not a key:

```bash
az storage account create --name <storageaccountname> --resource-group <rg> \
  --sku Standard_LRS --kind StorageV2 --access-tier Hot \
  --min-tls-version TLS1_2 --allow-blob-public-access false

az storage container create --account-name <storageaccountname> --name documents --auth-mode login

az role assignment create \
  --assignee-object-id <principalId from step 1> --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<storageaccountname>"
```

`Standard_LRS` is the cheapest redundancy tier; for a low-traffic association's documents the
monthly cost is a few cents of storage plus fractions of a cent per request — there is no fixed
minimum fee.

**4. App Service → Configuration → Application settings:**

```
Database__Provider           = SqlServer
ConnectionStrings__Default    = Server=tcp:<sql-server-name>.database.windows.net,1433;Database=<db-name>;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
Jwt__SigningKey               = <32+ random characters>
Seed__Enabled                 = false
Cors__AllowedOrigins__0       = https://<app-service-name>.azurewebsites.net
Storage__Provider             = AzureBlob
Storage__AccountUrl           = https://<storageaccountname>.blob.core.windows.net
Storage__ContainerName        = documents
```

No `Storage__ConnectionString` and no SQL password appear anywhere — `Storage__AccountUrl`
(not `Storage__ConnectionString`) is what routes Blob access through the managed identity
instead of an account key. Restart the App Service after saving so it picks up the new
settings and connects with its own identity on the next request.

---

## Enabling Google and Microsoft sign-in

Both flows are built and waiting for credentials; no code changes are needed. Whichever
provider a person uses, the rules are identical (see [Signing up and being
approved](#signing-up-and-being-approved)): a known address signs straight in, an
admin-created invitation is completed by the first federated sign-in, and an unrecognised
address registers itself as **PendingApproval** with no role and no token — exactly like the
email/password registration form.

### Google

1. In Google Cloud Console create an **OAuth 2.0 Client ID** of type *Web application*.
2. Authorised JavaScript origins: `http://localhost:5173` for development, and
   `https://<your-app>.azurewebsites.net` for production.
3. Set the client id in **both** places — the browser needs it to render the button and the
   server needs it to validate the token audience:
   - Frontend: `VITE_GOOGLE_CLIENT_ID` in `frontend/.env.local`
   - Backend: `Authentication__Google__ClientId`

### Microsoft

Accepts **any Microsoft account** — work, school or personal — via the multi-tenant `common`
authority; there is no tenant restriction. Sign-in uses a full-page redirect (MSAL.js, bundled
in the frontend build, not loaded from a CDN), so no popup-blocker or CSP `frame-src`
exception is needed — only `connect-src https://login.microsoftonline.com`, already present in
`SecurityHeaders.cs`.

1. In the [Entra admin center](https://entra.microsoft.com) → **App registrations** → **New
   registration**.
2. Supported account types: **Accounts in any organizational directory and personal Microsoft
   accounts**.
3. Platform: **Single-page application (SPA)**. Redirect URI: `<origin>/login` for every origin
   the app is served from — `http://localhost:5173/login` in development,
   `https://<your-app>.azurewebsites.net/login` in production. The app returns to `/login` and
   resolves the sign-in there before redirecting into the dashboard.
4. Copy the **Application (client) ID** into:
   - Frontend: `VITE_MICROSOFT_CLIENT_ID` in `frontend/.env.local`
   - Backend: `Authentication__Microsoft__ClientId` — required for the server to validate the
     token's signature and audience; the frontend id alone is not enough.

No client secret is needed or used — this is a public client (SPA), and the backend verifies
the ID token against Microsoft's published signing keys rather than holding a credential of
its own.

---

## Security notes

- Passwords: ASP.NET Core Identity (PBKDF2), minimum 10 characters with mixed case, a digit and
  a symbol; five failed attempts lock the account for 15 minutes.
- Self-registered accounts receive no token until approved, so the approval queue is a hard
  gate rather than a UI convention.
- Rate limits are **partitioned per client address**, not global, so one busy office cannot
  exhaust the budget for everyone. Sign-in and registration have separate budgets, both
  configurable (`RateLimiting:SignInPerMinutePerClient`, `RateLimiting:RegisterPerHourPerClient`).
- Tokens: short-lived JWT access tokens plus rotating refresh tokens, stored **hashed**. Using a
  refresh token invalidates it. Changing a role, department, status or password revokes every
  session for that user.
- Login responses are identical for an unknown address and a wrong password, so the endpoint
  cannot be used to enumerate accounts.
- Uploads are capped at 25 MB, executable extensions are rejected, and the stored name is a
  random GUID — a hostile filename cannot escape the storage root.
- Documents marked confidential are restricted to the owning department and heads, re-checked on
  every download rather than trusted from the list response.
- Baseline headers on every response (CSP, `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy`) and a fixed-window rate limiter on authentication.

---

## Known gaps

Honest list of what is **not** built:

- **No email delivery.** Invitations, approvals and rejections are in-app only; there is no
  SMTP integration. An applicant is not emailed when they are approved — they discover it by
  signing in again, and administrators must share initial passwords out of band. This is the
  most significant gap for the self-registration flow.
- **No email verification at signup.** Anyone can register with any address they can type;
  the administrator approval step is the only check. Ask for a domain allowlist if this
  matters on a public URL.
- **Approval routing is single-step by design** (as specified). There is no amount-threshold
  escalation or multi-approver chain.
- **Document versioning** stores a `Version` column but the UI always uploads as version 1;
  there is no revision history view.
- **Reports** cover the five summaries listed in the Reports module. There is no ad-hoc report
  builder, and export is CSV only (no PDF).
- **No realtime push.** Notifications poll once a minute rather than using SignalR.
- Seeded data is realistic but synthetic; no production data has been imported.
