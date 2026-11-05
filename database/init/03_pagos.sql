-- =============================================================================
-- 03 - Esquema pagos: intentos de cobro y mensajes procesados
-- Las tablas se crean con el rol pagos_app para que sea su dueño.
-- =============================================================================
SET ROLE pagos_app;

-- Cada intento de cobro, aprobado o rechazado, queda registrado.
-- Se guardan solo los últimos 4 dígitos de la tarjeta: NUNCA el número completo.
CREATE TABLE pagos.pagos (
    pago_id              varchar(20)    PRIMARY KEY,
    pedido_id            uuid           NOT NULL,
    saga_id              uuid           NOT NULL,
    monto                numeric(12, 2) NOT NULL,
    estado               varchar(20)    NOT NULL
        CHECK (estado IN ('Aprobado', 'Rechazado', 'Reembolsado')),
    motivo               varchar(50),
    tarjeta_ultimos4     char(4),
    titular              varchar(100),
    codigo_autorizacion  varchar(6),
    fecha_utc            timestamptz    NOT NULL,
    fecha_reembolso      timestamptz
);

-- GET /api/pagos?pedidoId={id} ordena por fecha descendente
CREATE INDEX ix_pagos_pedido_fecha ON pagos.pagos (pedido_id, fecha_utc DESC);

-- Secuencia para los ids con prefijo: PG-000001, PG-000002...
CREATE SEQUENCE pagos.pagos_seq;

-- Idempotencia del consumidor de pagos.comandos (Hito 6)
CREATE TABLE pagos.mensajes_procesados (
    message_id  uuid         PRIMARY KEY,
    fecha_utc   timestamptz  NOT NULL DEFAULT now()
);

RESET ROLE;
