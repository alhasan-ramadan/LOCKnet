# Analytics Rollout Readiness (Current State)

## 1) Deployment/runtime setup inspection (what exists today)

This repository currently contains:

- GitHub Actions CI (`.github/workflows/ci.yml`) for build/lint/test/coverage.
- GitHub Actions release (`.github/workflows/release.yml`) for self-contained app binaries.
- GitHub Pages docs workflow (`.github/workflows/pages.yml`).
- SQLCipher smoke workflow (`.github/workflows/sqlcipher-smoke.yml`).

This repository currently does **not** contain:

- Cloudflare/Wrangler config (`wrangler.toml`) or any D1 binding config.
- Analytics ingestion routes/endpoints.
- UTM/cookie attribution parsing code.
- Preview vs production environment split for analytics.
- Analytics feature flags.
- Analytics migrations (schema or migration runner for analytics storage).

## 2) Rollout risk assessment (analytics-specific)

For this repo **as-is**, analytics rollout risk cannot be operationally validated because the analytics runtime surface does not exist yet.

The following risks are real once analytics is introduced:

- deploy before migration (write path fails or partial writes)
- missing DB binding in target environment
- wrong feature-flag order (traffic enabled before storage/validation)
- cookie/attribution differences between local and edge runtime
- analytics endpoint exposed without auth/rate-limits where required
- admin/internal routes polluting business metrics
- production-only runtime differences (headers, cache behavior, bot traffic)

## 3) Safe rollout sequence (use when analytics is introduced)

1. **Pre-deploy checks**
   - verify analytics migration is present and reversible
   - verify all required environment bindings/variables are configured in preview + production
   - verify analytics endpoint auth/rate-limiting decisions are explicit
   - verify internal/admin route filtering behavior is explicit
2. **Deploy migration first**
   - run analytics schema migration before enabling writes
3. **Deploy code with writes disabled**
   - keep analytics flag off in production
4. **Smoke in preview/staging**
   - page-view, source/UTM, and key event writes visible in storage
5. **Enable production flag gradually**
   - small traffic slice first, then full enable
6. **Post-enable validation**
   - verify non-zero writes, attribution fields, and event mix sanity
7. **Rollback path**
   - disable analytics write flag first
   - keep read/query path operational for diagnosis
   - avoid destructive rollback on analytics table unless explicitly required

## 4) Production smoke-test checklist (analytics)

Use this checklist once analytics endpoints and storage exist:

- page view/landing event appears in storage
- duel/quiz event appears with expected payload shape
- signup/login event appears with expected user/session context
- UTM/source values persist correctly for attributed sessions
- query layer can read expected event counts for the test window
- wrong/malformed payload handling is deterministic (4xx/validation)
- no admin-only route noise in business dashboards

## 5) Monitoring recommendations (lightweight)

- alert on sustained analytics write failures (non-2xx or DB-write exceptions)
- alert on zero-event anomaly after deploy (expected traffic but no writes)
- alert on sudden spike of missing attribution fields
- track a small set of meaningful event counters (not all events)

## 6) Observability limits (explicit)

What is currently observable in this repository:

- build/test/release/docs pipeline health
- desktop/security storage behavior for LOCKnet

What is **not** currently observable here (because not implemented):

- analytics ingestion health
- analytics storage write success rate
- attribution correctness
- analytics endpoint security posture in production

## 7) Practical prerequisite before analytics rollout work

Before executing a real analytics rollout plan, add the missing analytics surfaces first:

- ingestion endpoint(s)
- analytics storage schema + migration workflow
- environment binding/config model for preview/prod
- feature flag(s) controlling write rollout
- minimal query/report path for smoke validation
