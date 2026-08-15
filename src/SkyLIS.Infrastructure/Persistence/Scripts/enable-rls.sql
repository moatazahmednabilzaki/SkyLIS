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
    WHERE schemaname IN ('patients', 'catalog', 'visits', 'billing', 'results', 'reports')
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

-- The platform schema (tenants registry, number series) is platform-operated:
-- number_series is tenant-owned and gets the same policy; tenants registry does not.
ALTER TABLE platform.number_series ENABLE ROW LEVEL SECURITY;
ALTER TABLE platform.number_series FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON platform.number_series;
CREATE POLICY tenant_isolation ON platform.number_series
  USING (tenant_id = current_setting('app.tenant_id')::uuid)
  WITH CHECK (tenant_id = current_setting('app.tenant_id')::uuid);
