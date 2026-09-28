-- Роли: владелец схемы (миграции, системные выборки) и приложение (без BYPASSRLS, подчиняется RLS).
CREATE ROLE dental_owner LOGIN PASSWORD 'dental_owner';
CREATE ROLE dental_app LOGIN PASSWORD 'dental_app' NOSUPERUSER NOBYPASSRLS;
CREATE DATABASE dental OWNER dental_owner;
\connect dental
ALTER SCHEMA public OWNER TO dental_owner;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS btree_gist;
