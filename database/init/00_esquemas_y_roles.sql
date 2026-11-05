-- =============================================================================
-- 00 - Esquemas y roles (Hito 4)
-- Un esquema por servicio: pedidos, inventario, pagos, logistica.
--
-- Cada servicio se conecta con SU PROPIO usuario, dueño solo de su esquema.
-- Así la regla "ningún servicio lee las tablas de otro" no depende solo de
-- la disciplina del programador: PostgreSQL la hace cumplir y devuelve
-- "permission denied" si un servicio intenta leer un esquema ajeno.
--
-- Las contraseñas son de desarrollo; en producción vendrían de un gestor de secretos.
-- Este script lo ejecuta PostgreSQL una sola vez, al crear la base por primera vez
-- (carpeta /docker-entrypoint-initdb.d del contenedor).
-- =============================================================================

CREATE ROLE pedidos_app    LOGIN PASSWORD 'pedidos_dev';
CREATE ROLE inventario_app LOGIN PASSWORD 'inventario_dev';
CREATE ROLE pagos_app      LOGIN PASSWORD 'pagos_dev';
CREATE ROLE logistica_app  LOGIN PASSWORD 'logistica_dev';

-- Cada esquema pertenece al rol de su servicio
CREATE SCHEMA pedidos    AUTHORIZATION pedidos_app;
CREATE SCHEMA inventario AUTHORIZATION inventario_app;
CREATE SCHEMA pagos      AUTHORIZATION pagos_app;
CREATE SCHEMA logistica  AUTHORIZATION logistica_app;

-- Nadie usa el esquema public
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- Cada rol busca primero en su propio esquema
ALTER ROLE pedidos_app    SET search_path = pedidos;
ALTER ROLE inventario_app SET search_path = inventario;
ALTER ROLE pagos_app      SET search_path = pagos;
ALTER ROLE logistica_app  SET search_path = logistica;
