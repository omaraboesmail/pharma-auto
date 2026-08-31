BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE OR REPLACE FUNCTION canonical_product_search_vector(
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

DO $validate_base_schema$
DECLARE
    required_table text;
BEGIN
    FOREACH required_table IN ARRAY ARRAY[
        'tenants',
        'connector_registrations',
        'subscriptions',
        'subscription_periods',
        'quota_reservations',
        'ocr_jobs',
        'canonical_products',
        'audit_events'
    ]
    LOOP
        IF to_regclass(required_table) IS NULL THEN
            RAISE EXCEPTION
                'Phase 1 hardening requires the base table %; apply 001-phase-1.sql first.',
                required_table;
        END IF;
    END LOOP;

    IF EXISTS (
        SELECT 1
        FROM subscription_periods AS period
        JOIN subscriptions AS subscription
          ON subscription.subscription_id = period.subscription_id
        WHERE subscription.tenant_id <> period.tenant_id
    ) THEN
        RAISE EXCEPTION
            'Cannot harden subscription_periods: a period is bound to another tenant.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM quota_reservations AS reservation
        JOIN subscription_periods AS period
          ON period.entitlement_id = reservation.entitlement_id
        WHERE period.tenant_id <> reservation.tenant_id
    ) THEN
        RAISE EXCEPTION
            'Cannot harden quota_reservations: a reservation is bound to another tenant.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ocr_jobs AS job
        JOIN connector_registrations AS connector
          ON connector.connector_id = job.connector_id
        WHERE connector.tenant_id <> job.tenant_id
    ) THEN
        RAISE EXCEPTION
            'Cannot harden ocr_jobs: a job is bound to another tenant Connector.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ocr_jobs AS job
        JOIN quota_reservations AS reservation
          ON reservation.reservation_id = job.reservation_id
        WHERE reservation.tenant_id <> job.tenant_id
           OR reservation.job_id <> job.job_id
           OR reservation.page_count <> job.page_count
    ) THEN
        RAISE EXCEPTION
            'Cannot harden ocr_jobs: a job and quota reservation have different identities or page counts.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM subscription_periods AS first_period
        JOIN subscription_periods AS second_period
          ON second_period.subscription_id = first_period.subscription_id
         AND second_period.entitlement_id > first_period.entitlement_id
         AND tstzrange(
                second_period.period_start,
                second_period.period_end,
                '[)') &&
             tstzrange(
                first_period.period_start,
                first_period.period_end,
                '[)')
    ) THEN
        RAISE EXCEPTION
            'Cannot harden subscription_periods: overlapping periods exist.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ocr_jobs
        WHERE state = 'COMPLETED'
          AND (result_json IS NULL OR provider_model IS NULL OR failure_code IS NOT NULL)
    ) THEN
        RAISE EXCEPTION
            'Cannot harden ocr_jobs: a completed legacy job has an invalid result payload.';
    END IF;
END;
$validate_base_schema$;

ALTER TABLE ocr_jobs
    ADD COLUMN IF NOT EXISTS attempt_id uuid;

UPDATE ocr_jobs
SET attempt_id = job_id
WHERE attempt_id IS NULL;

UPDATE ocr_jobs
SET result_json = NULL,
    provider_model = NULL,
    failure_code = NULL
WHERE state IN ('RESERVED', 'PROCESSING')
  AND (result_json IS NOT NULL OR provider_model IS NOT NULL OR failure_code IS NOT NULL);

UPDATE ocr_jobs
SET result_json = NULL,
    provider_model = NULL,
    failure_code = coalesce(nullif(failure_code, ''), 'OCR_LEGACY_FAILURE')
WHERE state = 'FAILED'
  AND (result_json IS NOT NULL OR provider_model IS NOT NULL OR failure_code IS NULL OR failure_code = '');

ALTER TABLE ocr_jobs
    ALTER COLUMN attempt_id SET NOT NULL;

DO $validate_legacy_finalization$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM ocr_jobs AS job
        JOIN quota_reservations AS reservation
          ON reservation.reservation_id = job.reservation_id
         AND reservation.tenant_id = job.tenant_id
         AND reservation.job_id = job.job_id
        WHERE job.state = 'COMPLETED'
          AND reservation.released_at IS NOT NULL
    ) THEN
        RAISE EXCEPTION
            'Cannot reconcile legacy OCR completion: completed quota was released.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ocr_jobs AS job
        JOIN quota_reservations AS reservation
          ON reservation.reservation_id = job.reservation_id
         AND reservation.tenant_id = job.tenant_id
         AND reservation.job_id = job.job_id
        WHERE job.state = 'FAILED'
          AND reservation.settled_at IS NOT NULL
    ) THEN
        RAISE EXCEPTION
            'Cannot reconcile legacy OCR failure: failed quota was settled.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ocr_jobs AS job
        JOIN quota_reservations AS reservation
          ON reservation.reservation_id = job.reservation_id
         AND reservation.tenant_id = job.tenant_id
         AND reservation.job_id = job.job_id
        WHERE job.state IN ('RESERVED', 'PROCESSING')
          AND (reservation.settled_at IS NOT NULL OR reservation.released_at IS NOT NULL)
    ) THEN
        RAISE EXCEPTION
            'Cannot reconcile an active legacy OCR job with finalized quota.';
    END IF;
END;
$validate_legacy_finalization$;

UPDATE quota_reservations AS reservation
SET settled_at = job.updated_at
FROM ocr_jobs AS job
WHERE job.reservation_id = reservation.reservation_id
  AND job.tenant_id = reservation.tenant_id
  AND job.job_id = reservation.job_id
  AND job.state = 'COMPLETED'
  AND reservation.settled_at IS NULL
  AND reservation.released_at IS NULL;

UPDATE quota_reservations AS reservation
SET released_at = job.updated_at
FROM ocr_jobs AS job
WHERE job.reservation_id = reservation.reservation_id
  AND job.tenant_id = reservation.tenant_id
  AND job.job_id = reservation.job_id
  AND job.state = 'FAILED'
  AND reservation.settled_at IS NULL
  AND reservation.released_at IS NULL;

-- A legacy crash could commit quota before creating the OCR job. There is no
-- Connector identity from which a trustworthy job can be reconstructed, so the
-- only safe recovery is to remove the active orphan and rebuild counters below.
DELETE FROM quota_reservations AS reservation
WHERE reservation.settled_at IS NULL
  AND reservation.released_at IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM ocr_jobs AS job
      WHERE job.reservation_id = reservation.reservation_id
        AND job.tenant_id = reservation.tenant_id
        AND job.job_id = reservation.job_id
  );

DO $validate_rebuilt_quota$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM subscription_periods AS period
        LEFT JOIN LATERAL (
            SELECT
                coalesce(sum(reservation.page_count)
                    FILTER (
                        WHERE reservation.settled_at IS NULL
                          AND reservation.released_at IS NULL), 0) AS reserved_pages,
                coalesce(sum(reservation.page_count)
                    FILTER (WHERE reservation.settled_at IS NOT NULL), 0) AS settled_pages
            FROM quota_reservations AS reservation
            WHERE reservation.tenant_id = period.tenant_id
              AND reservation.entitlement_id = period.entitlement_id
        ) AS ledger ON true
        WHERE ledger.reserved_pages + ledger.settled_pages > period.page_limit
    ) THEN
        RAISE EXCEPTION
            'Cannot harden subscription_periods: reconstructed quota exceeds a period limit.';
    END IF;
END;
$validate_rebuilt_quota$;

UPDATE subscription_periods AS period
SET pages_reserved = (
        SELECT coalesce(sum(reservation.page_count), 0)::integer
        FROM quota_reservations AS reservation
        WHERE reservation.tenant_id = period.tenant_id
          AND reservation.entitlement_id = period.entitlement_id
          AND reservation.settled_at IS NULL
          AND reservation.released_at IS NULL
    ),
    pages_settled = (
        SELECT coalesce(sum(reservation.page_count), 0)::integer
        FROM quota_reservations AS reservation
        WHERE reservation.tenant_id = period.tenant_id
          AND reservation.entitlement_id = period.entitlement_id
          AND reservation.settled_at IS NOT NULL
    );

DO $add_tenant_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscriptions'::regclass
          AND contype = 'u'
          AND position(
                'UNIQUE (tenant_id, subscription_id)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE subscriptions
            ADD CONSTRAINT uq_subscriptions_tenant_subscription
            UNIQUE (tenant_id, subscription_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscription_periods'::regclass
          AND contype = 'u'
          AND position(
                'UNIQUE (tenant_id, entitlement_id)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE subscription_periods
            ADD CONSTRAINT uq_subscription_periods_tenant_entitlement
            UNIQUE (tenant_id, entitlement_id);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscription_periods'::regclass
          AND contype = 'f'
          AND confrelid = 'subscriptions'::regclass
          AND position(
                'FOREIGN KEY (tenant_id, subscription_id)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE subscription_periods
            ADD CONSTRAINT fk_subscription_periods_tenant_subscription
            FOREIGN KEY (tenant_id, subscription_id)
            REFERENCES subscriptions(tenant_id, subscription_id)
            NOT VALID;
        ALTER TABLE subscription_periods
            VALIDATE CONSTRAINT fk_subscription_periods_tenant_subscription;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'quota_reservations'::regclass
          AND contype = 'u'
          AND position(
                'UNIQUE (tenant_id, reservation_id, job_id, page_count)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE quota_reservations
            ADD CONSTRAINT uq_quota_reservations_tenant_reservation_job_page
            UNIQUE (tenant_id, reservation_id, job_id, page_count);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'quota_reservations'::regclass
          AND contype = 'f'
          AND confrelid = 'subscription_periods'::regclass
          AND position(
                'FOREIGN KEY (tenant_id, entitlement_id)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE quota_reservations
            ADD CONSTRAINT fk_quota_reservations_tenant_entitlement
            FOREIGN KEY (tenant_id, entitlement_id)
            REFERENCES subscription_periods(tenant_id, entitlement_id)
            NOT VALID;
        ALTER TABLE quota_reservations
            VALIDATE CONSTRAINT fk_quota_reservations_tenant_entitlement;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'ocr_jobs'::regclass
          AND contype = 'f'
          AND confrelid = 'connector_registrations'::regclass
          AND position(
                'FOREIGN KEY (tenant_id, connector_id)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE ocr_jobs
            ADD CONSTRAINT fk_ocr_jobs_tenant_connector
            FOREIGN KEY (tenant_id, connector_id)
            REFERENCES connector_registrations(tenant_id, connector_id)
            NOT VALID;
        ALTER TABLE ocr_jobs
            VALIDATE CONSTRAINT fk_ocr_jobs_tenant_connector;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'ocr_jobs'::regclass
          AND contype = 'f'
          AND confrelid = 'quota_reservations'::regclass
          AND position(
                'FOREIGN KEY (tenant_id, reservation_id, job_id, page_count)'
                IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE ocr_jobs
            ADD CONSTRAINT fk_ocr_jobs_tenant_reservation_job_page
            FOREIGN KEY (tenant_id, reservation_id, job_id, page_count)
            REFERENCES quota_reservations(tenant_id, reservation_id, job_id, page_count)
            NOT VALID;
        ALTER TABLE ocr_jobs
            VALIDATE CONSTRAINT fk_ocr_jobs_tenant_reservation_job_page;
    END IF;
END;
$add_tenant_constraints$;

DO $add_domain_constraints$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscription_periods'::regclass
          AND contype = 'c'
          AND position('pages_reserved' IN pg_get_constraintdef(oid)) > 0
          AND position('pages_settled' IN pg_get_constraintdef(oid)) > 0
          AND position('page_limit' IN pg_get_constraintdef(oid)) > 0
    ) THEN
        ALTER TABLE subscription_periods
            ADD CONSTRAINT ck_subscription_period_quota_totals
            CHECK (pages_reserved + pages_settled <= page_limit)
            NOT VALID;
        ALTER TABLE subscription_periods
            VALIDATE CONSTRAINT ck_subscription_period_quota_totals;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'subscription_periods'::regclass
          AND contype = 'x'
    ) THEN
        ALTER TABLE subscription_periods
            ADD CONSTRAINT ex_subscription_periods_no_overlap
            EXCLUDE USING gist (
                subscription_id WITH =,
                tstzrange(period_start, period_end, '[)') WITH &&
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'ocr_jobs'::regclass
          AND conname = 'ck_ocr_jobs_state_payload'
    ) THEN
        ALTER TABLE ocr_jobs
            ADD CONSTRAINT ck_ocr_jobs_state_payload CHECK (
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
            ) NOT VALID;
        ALTER TABLE ocr_jobs
            VALIDATE CONSTRAINT ck_ocr_jobs_state_payload;
    END IF;
END;
$add_domain_constraints$;

DO $replace_search_vector$
DECLARE
    generated_expression text;
BEGIN
    SELECT pg_get_expr(definition.adbin, definition.adrelid)
    INTO generated_expression
    FROM pg_attribute AS attribute
    JOIN pg_attrdef AS definition
      ON definition.adrelid = attribute.attrelid
     AND definition.adnum = attribute.attnum
    WHERE attribute.attrelid = 'canonical_products'::regclass
      AND attribute.attname = 'search_vector'
      AND NOT attribute.attisdropped;

    IF generated_expression IS NULL OR
       position('canonical_product_search_vector' IN generated_expression) = 0 THEN
        DROP INDEX IF EXISTS ix_canonical_products_search;
        ALTER TABLE canonical_products DROP COLUMN IF EXISTS search_vector;
        ALTER TABLE canonical_products
            ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                canonical_product_search_vector(display_name, aliases)
            ) STORED;
    END IF;
END;
$replace_search_vector$;

CREATE INDEX IF NOT EXISTS ix_canonical_products_search
    ON canonical_products USING gin(search_vector);
CREATE INDEX IF NOT EXISTS ix_canonical_products_name_trgm
    ON canonical_products USING gin(display_name gin_trgm_ops);

COMMIT;
