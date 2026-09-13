BEGIN;

CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE FUNCTION canonical_product_search_vector(
    product_display_name text,
    product_aliases text[])
RETURNS tsvector
LANGUAGE plpgsql
IMMUTABLE
PARALLEL SAFE
SET search_path = pg_catalog
AS $function$
DECLARE
    search_result tsvector := setweight(
        to_tsvector('simple'::regconfig, coalesce(product_display_name, '')),
        'A');
    alias_value text;
BEGIN
    FOREACH alias_value IN ARRAY coalesce(product_aliases, ARRAY[]::text[])
    LOOP
        search_result := search_result || setweight(
            to_tsvector('simple'::regconfig, coalesce(alias_value, '')),
            'B');
    END LOOP;
    RETURN search_result;
END;
$function$;

CREATE TABLE tenants (
    tenant_id uuid PRIMARY KEY,
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 120),
    created_at timestamptz NOT NULL
);

CREATE TABLE connector_registrations (
    connector_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(tenant_id),
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 120),
    certificate_thumbprint text,
    activated_at timestamptz NOT NULL,
    revoked_at timestamptz,
    UNIQUE (tenant_id, connector_id)
);

CREATE TABLE subscriptions (
    subscription_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL UNIQUE REFERENCES tenants(tenant_id),
    status text NOT NULL CHECK (status IN ('ACTIVE', 'SUSPENDED', 'EXPIRED')),
    valid_from timestamptz NOT NULL,
    valid_until timestamptz NOT NULL CHECK (valid_until > valid_from),
    offline_review_allowed boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    UNIQUE (tenant_id, subscription_id)
);

CREATE TABLE subscription_periods (
    entitlement_id uuid PRIMARY KEY,
    subscription_id uuid NOT NULL,
    tenant_id uuid NOT NULL REFERENCES tenants(tenant_id),
    period_start timestamptz NOT NULL,
    period_end timestamptz NOT NULL CHECK (period_end > period_start),
    page_limit integer NOT NULL CHECK (page_limit >= 0),
    pages_reserved integer NOT NULL DEFAULT 0 CHECK (pages_reserved >= 0),
    pages_settled integer NOT NULL DEFAULT 0 CHECK (pages_settled >= 0),
    updated_at timestamptz NOT NULL,
    UNIQUE (subscription_id, period_start),
    UNIQUE (tenant_id, entitlement_id),
    FOREIGN KEY (tenant_id, subscription_id)
        REFERENCES subscriptions(tenant_id, subscription_id),
    CHECK (pages_reserved + pages_settled <= page_limit),
    EXCLUDE USING gist (
        subscription_id WITH =,
        tstzrange(period_start, period_end, '[)') WITH &&
    )
);

CREATE TABLE quota_reservations (
    reservation_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(tenant_id),
    entitlement_id uuid NOT NULL,
    job_id uuid NOT NULL,
    page_count integer NOT NULL CHECK (page_count BETWEEN 1 AND 100),
    reserved_at timestamptz NOT NULL,
    settled_at timestamptz,
    released_at timestamptz,
    CHECK (settled_at IS NULL OR released_at IS NULL),
    UNIQUE (tenant_id, job_id),
    UNIQUE (tenant_id, reservation_id, job_id, page_count),
    FOREIGN KEY (tenant_id, entitlement_id)
        REFERENCES subscription_periods(tenant_id, entitlement_id)
);

CREATE TABLE ocr_jobs (
    job_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(tenant_id),
    connector_id uuid NOT NULL,
    page_count integer NOT NULL CHECK (page_count BETWEEN 1 AND 100),
    source_sha256 text NOT NULL CHECK (source_sha256 ~ '^[a-f0-9]{64}$'),
    state text NOT NULL CHECK (state IN ('RESERVED', 'PROCESSING', 'COMPLETED', 'FAILED')),
    reservation_id uuid NOT NULL,
    result_json jsonb,
    provider_model text,
    failure_code text,
    attempt_id uuid NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT ck_ocr_jobs_state_payload CHECK (
        (state IN ('RESERVED', 'PROCESSING')
            AND result_json IS NULL
            AND provider_model IS NULL
            AND failure_code IS NULL)
        OR (state = 'COMPLETED'
            AND result_json IS NOT NULL
            AND provider_model IS NOT NULL
            AND failure_code IS NULL)
        OR (state = 'FAILED'
            AND result_json IS NULL
            AND provider_model IS NULL
            AND failure_code IS NOT NULL)
    ),
    UNIQUE (tenant_id, job_id),
    FOREIGN KEY (tenant_id, connector_id)
        REFERENCES connector_registrations(tenant_id, connector_id),
    FOREIGN KEY (tenant_id, reservation_id, job_id, page_count)
        REFERENCES quota_reservations(tenant_id, reservation_id, job_id, page_count)
);

CREATE TABLE canonical_products (
    canonical_product_id uuid PRIMARY KEY,
    display_name text NOT NULL,
    aliases text[] NOT NULL DEFAULT '{}',
    identifiers text[] NOT NULL DEFAULT '{}',
    active_ingredient text,
    strength text,
    dosage_form text,
    pack text,
    manufacturer text,
    embedding vector(768),
    embedding_version text NOT NULL,
    active boolean NOT NULL DEFAULT true,
    search_vector tsvector GENERATED ALWAYS AS (
        canonical_product_search_vector(display_name, aliases)
    ) STORED,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE INDEX ix_canonical_products_search ON canonical_products USING gin(search_vector);
CREATE INDEX ix_canonical_products_name_trgm ON canonical_products USING gin(display_name gin_trgm_ops);

CREATE TABLE audit_events (
    event_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES tenants(tenant_id),
    actor_type text NOT NULL,
    actor_reference text NOT NULL,
    action text NOT NULL,
    target_reference text NOT NULL,
    result text NOT NULL,
    correlation_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL
);

ALTER TABLE connector_registrations ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscription_periods ENABLE ROW LEVEL SECURITY;
ALTER TABLE quota_reservations ENABLE ROW LEVEL SECURITY;
ALTER TABLE ocr_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;

ALTER TABLE connector_registrations FORCE ROW LEVEL SECURITY;
ALTER TABLE subscriptions FORCE ROW LEVEL SECURITY;
ALTER TABLE subscription_periods FORCE ROW LEVEL SECURITY;
ALTER TABLE quota_reservations FORCE ROW LEVEL SECURITY;
ALTER TABLE ocr_jobs FORCE ROW LEVEL SECURITY;
ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;

CREATE POLICY connector_tenant_policy ON connector_registrations
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY subscription_tenant_policy ON subscriptions
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY period_tenant_policy ON subscription_periods
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY reservation_tenant_policy ON quota_reservations
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY ocr_job_tenant_policy ON ocr_jobs
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY audit_tenant_policy ON audit_events
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);

COMMIT;
