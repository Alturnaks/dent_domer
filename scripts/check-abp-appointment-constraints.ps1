# Tests the real PostgreSQL exclusion constraints. Every inserted row is rolled back.
param([string]$Container = 'dental-abp-postgres-1')
$ErrorActionPreference = 'Stop'
$constraintSql = @'
BEGIN;
CREATE FUNCTION pg_temp.book(t uuid, d uuid, c uuid, b uuid, st timestamp, en timestamp, s integer DEFAULT 0)
RETURNS void LANGUAGE SQL AS $fn$
  INSERT INTO "AppAppointments"
    ("Id", "TenantId", "BranchId", "PatientId", "DoctorId", "ChairId", "StartsAt", "EndsAt", "Status", "Source",
     "ForcedOutsideSchedule", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "IsDeleted")
  VALUES (gen_random_uuid(), t, b, gen_random_uuid(), d, c, st, en, s, 0, false, '{}', gen_random_uuid()::text, now(), false);
$fn$;
DO $test$
DECLARE
  tenant uuid := gen_random_uuid(); doctor uuid := gen_random_uuid(); chair uuid := gen_random_uuid(); branch uuid := gen_random_uuid();
BEGIN
  PERFORM pg_temp.book(tenant, doctor, chair, branch, '2030-01-01 09:00', '2030-01-01 10:00');
  BEGIN
    PERFORM pg_temp.book(tenant, doctor, null, gen_random_uuid(), '2030-01-01 09:30', '2030-01-01 10:30');
    RAISE EXCEPTION 'FAIL doctor overlap across branches was accepted';
  EXCEPTION WHEN exclusion_violation THEN RAISE NOTICE 'PASS database forbids doctor overlap across branches'; END;
  BEGIN
    PERFORM pg_temp.book(tenant, gen_random_uuid(), chair, branch, '2030-01-01 09:30', '2030-01-01 10:30');
    RAISE EXCEPTION 'FAIL chair overlap was accepted';
  EXCEPTION WHEN exclusion_violation THEN RAISE NOTICE 'PASS database forbids chair overlap with different doctors'; END;
  PERFORM pg_temp.book(tenant, doctor, chair, branch, '2030-01-01 10:00', '2030-01-01 11:00');
  RAISE NOTICE 'PASS adjacent appointments are allowed';
  PERFORM pg_temp.book(gen_random_uuid(), doctor, chair, branch, '2030-01-01 09:00', '2030-01-01 10:00');
  RAISE NOTICE 'PASS exclusion constraints isolate tenants';
  PERFORM pg_temp.book(tenant, doctor, chair, branch, '2030-01-01 09:00', '2030-01-01 10:00', 5);
  PERFORM pg_temp.book(tenant, doctor, chair, branch, '2030-01-01 09:00', '2030-01-01 10:00', 6);
  RAISE NOTICE 'PASS cancelled and no-show appointments do not occupy time';
  BEGIN
    UPDATE "AppAppointments" SET "Status"=2 WHERE "TenantId"=tenant AND "DoctorId"=doctor AND "Status"=6;
    RAISE EXCEPTION 'FAIL restoring no-show into occupied time was accepted';
  EXCEPTION WHEN exclusion_violation THEN RAISE NOTICE 'PASS reactivating a conflicting no-show is forbidden'; END;
  PERFORM pg_temp.book(null, doctor, null, branch, '2030-01-02 09:00', '2030-01-02 10:00');
  BEGIN
    PERFORM pg_temp.book(null, doctor, null, branch, '2030-01-02 09:30', '2030-01-02 10:30');
    RAISE EXCEPTION 'FAIL host doctor overlap was accepted';
  EXCEPTION WHEN exclusion_violation THEN RAISE NOTICE 'PASS host null-tenant appointments are protected'; END;
END;
$test$;
ROLLBACK;
'@
$constraintSql | docker exec -i $Container psql -U abp -d dental_abp -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL appointment constraint verification failed.' }
