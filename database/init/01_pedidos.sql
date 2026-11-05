-- =============================================================================
-- 01 - Esquema pedidos: event store, sagas y mensajes procesados
-- Las tablas se crean con el rol pedidos_app para que sea su dueño.
-- =============================================================================
SET ROLE pedidos_app;

-- Event store del pedido (Hito 2). NO existe una tabla de pedidos:
-- el pedido es su lista de eventos (stream_id = pedidoId).
CREATE TABLE pedidos.eventos (
    id           bigserial    PRIMARY KEY,
    stream_id    uuid         NOT NULL,
    version      int          NOT NULL CHECK (version > 0),
    tipo_evento  varchar(100) NOT NULL,
    datos        jsonb        NOT NULL,
    fecha_utc    timestamptz  NOT NULL,
    -- Concurrencia optimista: dos procesos no pueden guardar la misma versión
    CONSTRAINT uq_eventos_stream_version UNIQUE (stream_id, version)
);

-- Estado de cada saga de confirmación (Hito 3)
CREATE TABLE pedidos.sagas (
    saga_id              uuid         PRIMARY KEY,
    pedido_id            uuid         NOT NULL,
    estado               varchar(20)  NOT NULL
        CHECK (estado IN ('EnCurso', 'Completada', 'Compensando', 'Compensada', 'Fallida')),
    paso_actual          varchar(30)  NOT NULL,
    datos                jsonb        NOT NULL,
    error                varchar(100),
    fecha_creacion       timestamptz  NOT NULL,
    fecha_actualizacion  timestamptz  NOT NULL
);

CREATE INDEX ix_sagas_pedido_id ON pedidos.sagas (pedido_id);

-- Historial de pasos y compensaciones de cada saga (en orden de id)
CREATE TABLE pedidos.saga_pasos (
    id         bigserial    PRIMARY KEY,
    saga_id    uuid         NOT NULL REFERENCES pedidos.sagas (saga_id),
    nombre     varchar(30)  NOT NULL,
    estado     varchar(20)  NOT NULL
        CHECK (estado IN ('Pendiente', 'Completado', 'Fallido')),
    motivo     varchar(100),
    resultado  jsonb,
    fecha_utc  timestamptz  NOT NULL
);

CREATE INDEX ix_saga_pasos_saga_id ON pedidos.saga_pasos (saga_id);

-- Idempotencia del consumidor de pedidos.respuestas (Hito 6)
CREATE TABLE pedidos.mensajes_procesados (
    message_id  uuid         PRIMARY KEY,
    fecha_utc   timestamptz  NOT NULL DEFAULT now()
);

RESET ROLE;
