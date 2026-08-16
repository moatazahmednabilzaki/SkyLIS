-- =============================================================================
-- Sky LIS — PostgreSQL Row-Level Security (SRS Rev 2.0 §2.4/§10, ADR-002)
-- Run after EF migrations. The application NEVER disables RLS; the tenant id is
-- injected per request:  SET app.tenant_id = '<uuid>'  (session/transaction local).
-- The application role must NOT be the table owner (owners bypass RLS unless FORCE).
-- =============================================================================

DO $$
DECLARE
  t record;
BEGIN
  FOR t IN
    SELECT format('%I.%I', schemaname, tablename) AS fqtn
    FROM pg_tables
    WHERE schemaname IN ('org', 'patients', 'catalog', 'visits', 'billing', 'results', 'reports', 'users', 'files')
  LOOP
    EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY', t.fqtn);
    EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY', t.fqtn);
    EXECUTE format($p$
      DROP POLICY IF EXISTS tenant_isolation ON %s;
      CREATE POLICY tenant_isolation ON %s
        USING (tenant_id = current_setting('app.tenant_id')::uuid)
        WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid)
    $p$, t.fqtn, t.fqtn);
  END LOOP;
END $$;

-- Audit trail (FR-SYS-001): append is always allowed (platform events carry NULL
-- tenant_id); reads are tenant-scoped. In production the application role is NOT the
-- table owner and additionally gets: REVOKE UPDATE, DELETE ON audit.audit_events —
-- making the table insert-only at the grant level on top of the hash chain.
ALTER TABLE audit.audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit.audit_events FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS audit_append ON audit.audit_events;
CREATE POLICY audit_append ON audit.audit_events FOR INSERT WITH CHECK (true);
DROP POLICY IF EXISTS audit_read ON audit.audit_events;
CREATE POLICY audit_read ON audit.audit_events FOR SELECT
  USING (
    tenant_id = current_setting('app.tenant_id')::uuid
    OR (tenant_id IS NULL
        AND current_setting('app.tenant_id')::uuid = '00000000-0000-0000-0000-000000000000')
  );

-- The platform schema (tenants registry, number series) is platform-operated:
-- number_series is tenant-owned and gets the same policy; tenants registry does not.
ALTER TABLE platform.number_series ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.number_series FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON platform.number_series;
CREATE POLICY tenant_isolation ON platform.number_series
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
