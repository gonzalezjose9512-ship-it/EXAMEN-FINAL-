-- =============================================================================
-- 02 - Esquema inventario: productos, reservas y mensajes procesados
-- Las tablas se crean con el rol inventario_app para que sea su dueño.
-- =============================================================================
SET ROLE inventario_app;

-- Catálogo y stock
CREATE TABLE inventario.productos (
    producto_id       varchar(20)    PRIMARY KEY,
    nombre            varchar(100)   NOT NULL,
    precio            numeric(12, 2) NOT NULL CHECK (precio > 0),
    -- El CHECK impide que el stock quede negativo aunque falle la lógica del handler
    stock_disponible  int            NOT NULL CHECK (stock_disponible >= 0)
);

-- Reservas: Activa mientras descuenta stock, Liberada después de la compensación
CREATE TABLE inventario.reservas (
    reserva_id  varchar(20)  PRIMARY KEY,
    pedido_id   uuid         NOT NULL,
    saga_id     uuid         NOT NULL,
    estado      varchar(20)  NOT NULL CHECK (estado IN ('Activa', 'Liberada')),
    fecha_utc   timestamptz  NOT NULL
);

-- Ítems de cada reserva (uno por producto)
CREATE TABLE inventario.reserva_items (
    reserva_id   varchar(20)  NOT NULL REFERENCES inventario.reservas (reserva_id),
    producto_id  varchar(20)  NOT NULL REFERENCES inventario.productos (producto_id),
    cantidad     int          NOT NULL CHECK (cantidad > 0),
    PRIMARY KEY (reserva_id, producto_id)
);

-- Secuencia para los ids con prefijo: R-000001, R-000002...
CREATE SEQUENCE inventario.reservas_seq;

-- Idempotencia del consumidor de inventario.comandos (Hito 6)
CREATE TABLE inventario.mensajes_procesados (
    message_id  uuid         PRIMARY KEY,
    fecha_utc   timestamptz  NOT NULL DEFAULT now()
);

-- Datos iniciales obligatorios del enunciado.
-- ON CONFLICT DO NOTHING: si el producto ya existe, no se pisa su stock.
INSERT INTO inventario.productos (producto_id, nombre, precio, stock_disponible) VALUES
    ('P001', 'Teclado mecánico',  45000,  10),
    ('P002', 'Mouse inalámbrico', 18000,  20),
    ('P003', 'Monitor 24"',       210000,  5),
    ('P004', 'Auriculares',       32000,   8),
    ('P005', 'Notebook',          950000,  2)
ON CONFLICT (producto_id) DO NOTHING;

RESET ROLE;
