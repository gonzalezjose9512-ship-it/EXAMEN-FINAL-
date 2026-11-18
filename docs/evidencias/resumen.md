# Resumen de verificaciones de los escenarios E1 a E5

Generado por `scripts/escenarios.ps1` el 2026-11-18 10:49.

| Escenario | Verificación | Esperado | Obtenido | Resultado |
| --- | --- | --- | --- | --- |
| E1 | Estado de la saga | Completada | Completada | OK |
| E1 | Pasos | ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Completado | ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Completado | OK |
| E1 | Estado del pedido | Confirmado | Confirmado | OK |
| E1 | Stock de P001 (baja 2) | 8 | 8 | OK |
| E2 | Estado de la saga | Fallida | Fallida | OK |
| E2 | Pasos | ReservarStock:Fallido | ReservarStock:Fallido | OK |
| E2 | Estado del pedido | Rechazado | Rechazado | OK |
| E2 | Motivo del rechazo | STOCK_INSUFICIENTE | STOCK_INSUFICIENTE | OK |
| E2 | Stock de P005 (sin cambios) | 2 | 2 | OK |
| E2 | Pagos del pedido (no se cobró) | 0 | 0 | OK |
| E3 | Estado de la saga | Compensada | Compensada | OK |
| E3 | Pasos | ReservarStock:Completado > Cobrar:Fallido > LiberarStock:Completado | ReservarStock:Completado > Cobrar:Fallido > LiberarStock:Completado | OK |
| E3 | Estado del pedido | Rechazado | Rechazado | OK |
| E3 | Motivo del rechazo | TARJETA_RECHAZADA | TARJETA_RECHAZADA | OK |
| E3 | Stock de P001 (vuelve a su valor original) | 8 | 8 | OK |
| E3 | Estado del pago | Rechazado | Rechazado | OK |
| E4 | Estado de la saga | Compensada | Compensada | OK |
| E4 | Pasos | ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Fallido > Reembolsar:Completado > LiberarStock:Completado | ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Fallido > Reembolsar:Completado > LiberarStock:Completado | OK |
| E4 | Estado del pedido | Rechazado | Rechazado | OK |
| E4 | Motivo del rechazo | SIN_CUPO | SIN_CUPO | OK |
| E4 | Stock de P001 (vuelve a su valor original) | 8 | 8 | OK |
| E4 | Estado del pago | Reembolsado | Reembolsado | OK |
| E4 | Envios activos del pedido | 0 | 0 | OK |
| E5 | Pedido con Pagos apagado | PendienteConfirmacion | PendienteConfirmacion | OK |
| E5 | Saga con Pagos apagado | EnCurso | EnCurso | OK |
| E5 | Mensajes esperando en pagos.comandos | 1 | 1 | OK |
| E5 | Saga al levantar Pagos | Completada | Completada | OK |
| E5 | Pedido al levantar Pagos | Confirmado | Confirmado | OK |
| E5 | Mensajes en pagos.comandos al terminar | 0 | 0 | OK |
