# Sky LIS — On-Premises Deployment Runbook

Single-host production deployment with Docker Compose: PostgreSQL 17, the API, and both
portals behind nginx (same-origin `/api` + `/hubs` proxy — no CORS in production).

## 1. Prerequisites

- Linux host (or Windows Server with Docker Desktop), 4+ CPU / 8+ GB RAM / SSD
- Docker Engine 24+ with the compose plugin
- The Sky LIS repository (or a release archive) on the host

## 2. First installation

```bash
cp .env.example .env
# Fill EVERY value in .env:
#  - SKYLIS_DB_PASSWORD  : long random DB password
#  - AUTH_SIGNING_KEY    : openssl rand -base64 48
#  - PLATFORM_ADMIN_*    : the first Admin Portal operator (created once on empty install)
chmod 600 .env

docker compose -f docker-compose.prod.yml up -d --build
```

What happens on first boot, in order:

1. `postgres` starts and passes its health check.
2. The API applies **all EF migrations** and re-applies the **Row-Level Security
   policies** (`Database__MigrateOnStartup=true`; the RLS script ships inside the binary
   and is idempotent). RLS uses `FORCE`, so even the table-owning application role is
   subject to tenant isolation.
3. Seeders run: canonical country packs, subscription plans, and — only if the operators
   table is empty — the bootstrap platform operator from `.env`.
4. The API refuses to boot if `AUTH_SIGNING_KEY` is missing or shorter than 32
   characters (fail-fast guard — production can never run on the checked-in dev key).

Verify:

```bash
curl -fsS http://localhost:8080/health          # via the client-portal nginx proxy
```

- Client Portal (lab staff): `http://<host>:8080`
- Admin Portal (platform console): `http://<host>:8081` — sign in with the bootstrap
  operator, then remove `PLATFORM_ADMIN_PASSWORD` from `.env`. The password must be at
  least 12 characters; the API refuses to start and seed a weaker one.

## 3. TLS

Terminate TLS in front of the two portal containers with the host's reverse proxy
(nginx/Caddy/Traefik) or your existing corporate proxy. Minimal host-nginx example:

```nginx
server {
    listen 443 ssl;
    server_name lab.example.local;
    ssl_certificate     /etc/ssl/skylis/fullchain.pem;
    ssl_certificate_key /etc/ssl/skylis/privkey.pem;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto https;
        # WebSockets (SignalR live worklists)
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 3600s;
    }
}
```

Repeat for the Admin Portal on its own hostname. Do not expose port 5432.

## 4. Backups

```bash
# nightly at 02:00, keep 14 dumps
0 2 * * * cd /opt/skylis && ./deploy/scripts/backup.sh /opt/skylis/backups
```

Restore drill (run one after installation — an untested backup is not a backup):

```bash
./deploy/scripts/restore.sh backups/skylis-<stamp>.dump
```

Keep a copy of `.env` (especially `AUTH_SIGNING_KEY`) in your secret store: the database
alone is not enough to bring the system back.

## 5. Upgrades

```bash
git pull                                    # or unpack the new release
# bump SKYLIS_VERSION in .env to the new release (images are tagged with it)
docker compose -f docker-compose.prod.yml up -d --build
```

Images are tagged `skylis/<component>:${SKYLIS_VERSION}` so every release is immutable.
The API applies new migrations on startup. Take a backup first; roll back by restoring
the dump, setting `SKYLIS_VERSION` back to the previous tag, and running `up -d` again.

Each service has CPU/memory ceilings under `deploy.resources` in the compose file — tune
them to your host before go-live.

## 6. Operations

- Logs: `docker compose -f docker-compose.prod.yml logs -f api` (structured Serilog output)
- Health: `/health` on either portal origin; container health checks restart unhealthy services
- Outbox/poison-message status: Admin Portal → Platform Health
- Audit chain: Client Portal → Audit Trail → "verify chain" (tamper evidence)

## 7. Security posture & known limits

- Access tokens live 60 minutes (configurable); refresh tokens rotate on every use and
  are revoked on logout; five failed sign-ins lock the account (§4.3).
- The application DB role owns the schema (it runs migrations). RLS `FORCE` keeps it
  subject to tenant policies; a split owner/runtime-role setup is a further hardening
  step for multi-node deployments.
- Single-node semantics: `MigrateOnStartup` and the in-process outbox dispatcher assume
  one API instance. Scale-out requires the RabbitMQ transport swap (Phase 2) and running
  migrations as a dedicated job.
- Report artifacts are hash-stamped PDFs (QuestPDF, Community license — free tier for
  organizations under USD 1M annual revenue; upgrade the license if that changes). The
  bilingual HTML preview serves portal viewing; the PDF itself uses Latin labels until
  an Arabic-capable font ships with the binary.
