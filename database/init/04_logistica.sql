-- =============================================================================
-- 04 - Esquema logistica: envíos y mensajes procesados
-- Las tablas se crean con el rol logistica_app para que sea su dueño.
-- =============================================================================
SET ROLE logistica_app;

-- Envíos: Programado ocupa cupo de su fecha de despacho; Anulado lo libera
CREATE TABLE logistica.envios (
    envio_id                varchar(20)   PRIMARY KEY,
    pedido_id               uuid          NOT NULL,
    saga_id                 uuid          NOT NULL,
    estado                  varchar(20)   NOT NULL CHECK (estado IN ('Programado', 'Anulado')),
    calle                   varchar(150)  NOT NULL,
    numero                  varchar(20)   NOT NULL,
    ciudad                  varchar(100)  NOT NULL,
    codigo_postal           varchar(20)   NOT NULL,
    fecha_despacho          date          NOT NULL,
    fecha_estimada_entrega  date          NOT NULL,
    codigo_seguimiento      varchar(20)   NOT NULL UNIQUE,
    fecha_utc               timestamptz   NOT NULL
);

-- El control de cupo cuenta los envíos Programado de una fecha
CREATE INDEX ix_envios_fecha_estado ON logistica.envios (fecha_despacho, estado);

-- Una sola secuencia para E-000031 y TN-000031 (mismo número)
CREATE SEQUENCE logistica.envios_seq;

-- Idempotencia del consumidor de logistica.comandos (Hito 6)
CREATE TABLE logistica.mensajes_procesados (
    message_id  uuid         PRIMARY KEY,
    fecha_utc   timestamptz  NOT NULL DEFAULT now()
);

RESET ROLE;
