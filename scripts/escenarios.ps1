<#
.SYNOPSIS
    Ejecuta los 5 escenarios del enunciado (E1 a E5) contra el sistema en Docker,
    verifica cada resultado esperado y guarda las evidencias en docs/evidencias.

.DESCRIPTION
    Usa solo la API de Pedidos para operar (como el cliente) y los GET de consulta de
    Inventario, Pagos y Logística para verificar que cada acción y cada compensación
    tuvo efecto real. Para E4 levanta Logística con CupoDiario = 0 y para E5 detiene
    Pagos con "docker compose stop pagos", tal como indica el enunciado.

    Por cada escenario guarda un JSON con los requests, el pedido (endpoint 4), sus
    eventos (endpoint 5), la saga (endpoint 6) y el estado de las colas de RabbitMQ.
    Al final escribe docs/evidencias/resumen.md con todas las verificaciones.

.PARAMETER Reiniciar
    Borra todos los datos (docker compose down -v) y levanta el sistema desde cero
    antes de empezar. Recomendado para generar las evidencias del README.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\escenarios.ps1 -Reiniciar
#>
[CmdletBinding()]
param(
    [switch] $Reiniciar
)

$ErrorActionPreference = 'Stop'

# ----- Rutas y direcciones -----
$raiz       = Split-Path -Parent $PSScriptRoot
$compose    = Join-Path $raiz 'docker-compose.yml'
$evidencias = Join-Path $raiz 'docs\evidencias'
$api        = 'http://localhost:5070'
$inventario = 'http://localhost:5172'
$pagos      = 'http://localhost:5149'
$logistica  = 'http://localhost:5250'
$rabbit     = 'http://localhost:15672'
$authRabbit = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('tiendanorte:tiendanorte_dev')) }

# ----- Datos de prueba -----
$ciudad           = 'San Miguel de Tucum' + [char]0x00E1 + 'n'
$direccion        = [ordered]@{ calle = 'Av. Mate de Luna'; numero = '1234'; ciudad = $ciudad; codigoPostal = '4000' }
$tarjetaValida    = [ordered]@{ numero = '4111111111111111'; titular = 'Juan Perez'; vencimiento = '12/28' }
$tarjetaRechazada = [ordered]@{ numero = '4111111111110000'; titular = 'Juan Perez'; vencimiento = '12/28' }
$dosTeclados      = [ordered]@{ productoId = 'P001'; cantidad = 2; precioUnitario = 45000 }
$tresNotebooks    = [ordered]@{ productoId = 'P005'; cantidad = 3; precioUnitario = 950000 }

$verificaciones = New-Object System.Collections.Generic.List[object]

# =============================================================================
# Funciones auxiliares
# =============================================================================

function Escribir-Titulo([string] $texto) {
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
    Write-Host " $texto" -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor DarkCyan
}

function Invocar([string] $metodo, [string] $url, $cuerpo = $null) {
    $parametros = @{ Method = $metodo; Uri = $url }
    if ($null -ne $cuerpo) {
        $json = ConvertTo-Json -InputObject $cuerpo -Depth 10 -Compress
        $parametros.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $parametros.ContentType = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @parametros
}

function Lista([string] $url) {
    # Windows PowerShell 5.1 devuelve un arreglo JSON como UN solo objeto (el arreglo entero).
    # Se recorre para obtener los elementos; la coma inicial evita que PowerShell lo vuelva a desarmar.
    $respuesta = Invocar 'GET' $url
    return ,@($respuesta | ForEach-Object { $_ })
}

function Docker-Compose([string[]] $argumentos) {
    & docker compose -f $compose @argumentos
    if ($LASTEXITCODE -ne 0) { throw "Falló: docker compose $($argumentos -join ' ')" }
}

function Esperar-Servicio([string] $nombre, [string] $url, [int] $segundos = 180) {
    Write-Host "Esperando a $nombre..." -NoNewline
    $limite = (Get-Date).AddSeconds($segundos)
    while ((Get-Date) -lt $limite) {
        try {
            $null = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            Write-Host ' listo.' -ForegroundColor Green
            return
        }
        catch {
            # Cualquier respuesta HTTP (aunque sea 404) significa que el servicio ya atiende
            if ($null -ne $_.Exception.Response) { Write-Host ' listo.' -ForegroundColor Green; return }
            Write-Host '.' -NoNewline
            Start-Sleep -Seconds 2
        }
    }
    throw "$nombre no respondió en $segundos segundos."
}

function Crear-Pedido($item) {
    $creado = Invocar 'POST' "$api/api/pedidos" ([ordered]@{ clienteId = 'CLI-001'; direccionEnvio = $direccion })
    $null = Invocar 'POST' "$api/api/pedidos/$($creado.pedidoId)/items" $item
    return $creado.pedidoId
}

function Confirmar-Pedido([string] $pedidoId, $tarjeta, [string] $fecha) {
    $cuerpo = [ordered]@{ tarjeta = $tarjeta }
    if ($fecha) { $cuerpo.fechaEnvioPreferida = $fecha }
    $respuesta = Invocar 'POST' "$api/api/pedidos/$pedidoId/confirmar" $cuerpo
    return $respuesta.sagaId
}

function Esperar-Saga([string] $sagaId, [string[]] $estados, [int] $segundos = 90) {
    $limite = (Get-Date).AddSeconds($segundos)
    do {
        $saga = Invocar 'GET' "$api/api/sagas/$sagaId"
        if ($estados -contains $saga.estado) { return $saga }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $limite)
    throw "La saga $sagaId no llegó a $($estados -join '/') en $segundos segundos (estado: $($saga.estado))."
}

function Esperar-Paso([string] $sagaId, [string] $paso, [int] $segundos = 60) {
    $limite = (Get-Date).AddSeconds($segundos)
    do {
        $saga = Invocar 'GET' "$api/api/sagas/$sagaId"
        if (@($saga.pasos | Where-Object { $_.nombre -eq $paso }).Count -gt 0) { return $saga }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $limite)
    throw "La saga $sagaId no llegó al paso $paso en $segundos segundos."
}

function Stock([string] $productoId) {
    return (Invocar 'GET' "$inventario/api/productos/$productoId").stockDisponible
}

function Estado-Colas {
    $estado = [ordered]@{}
    foreach ($cola in (Invoke-RestMethod -Uri "$rabbit/api/queues" -Headers $authRabbit | Sort-Object name)) {
        $estado[$cola.name] = [ordered]@{ ready = $cola.messages_ready; unacked = $cola.messages_unacknowledged }
    }
    return $estado
}

function Esperar-Cola([string] $cola, [int] $esperado, [int] $segundos = 30) {
    # El panel de RabbitMQ recalcula sus estadísticas cada ~5 segundos:
    # se consulta hasta que refleje el valor esperado (o se agote el tiempo).
    $limite = (Get-Date).AddSeconds($segundos)
    do {
        $colas = Estado-Colas
        if ($colas[$cola].ready -eq $esperado) { return $colas }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $limite)
    return $colas
}

function Fecha-Con-Cupo([int] $desdeDias = 7) {
    $fecha = (Get-Date).Date.AddDays($desdeDias)
    for ($i = 0; $i -lt 90; $i++) {
        if ($fecha.DayOfWeek -ne 'Saturday' -and $fecha.DayOfWeek -ne 'Sunday') {
            $texto = $fecha.ToString('yyyy-MM-dd')
            $dia = Invocar 'GET' "$logistica/api/envios?fecha=$texto"
            if ($dia.programados -lt $dia.cupoDiario) { return $texto }
        }
        $fecha = $fecha.AddDays(1)
    }
    throw 'No hay fechas hábiles con cupo en los próximos 90 días.'
}

function Verificar([string] $escenario, [string] $descripcion, $esperado, $obtenido) {
    $ok = ("$esperado" -eq "$obtenido")
    $verificaciones.Add([pscustomobject]@{
        Escenario    = $escenario
        Verificacion = $descripcion
        Esperado     = "$esperado"
        Obtenido     = "$obtenido"
        Resultado    = $(if ($ok) { 'OK' } else { 'FALLA' })
    })
    if ($ok) { Write-Host "  [OK]    $descripcion = $obtenido" -ForegroundColor Green }
    else     { Write-Host "  [FALLA] $descripcion = $obtenido (se esperaba $esperado)" -ForegroundColor Red }
}

function Nombres-Pasos($saga) {
    return (@($saga.pasos) | ForEach-Object { "$($_.nombre):$($_.estado)" }) -join ' > '
}

function Guardar-Evidencia([string] $archivo, $datos) {
    $ruta = Join-Path $evidencias $archivo
    $json = ConvertTo-Json -InputObject $datos -Depth 20
    [IO.File]::WriteAllText($ruta, $json, (New-Object Text.UTF8Encoding $false))
    Write-Host "  Evidencia guardada en docs/evidencias/$archivo" -ForegroundColor DarkGray
}

function Consultar-Pedido([string] $pedidoId) {
    return [ordered]@{
        pedido  = Invocar 'GET' "$api/api/pedidos/$pedidoId"
        eventos = Lista "$api/api/pedidos/$pedidoId/eventos"
    }
}

# =============================================================================
# Preparación
# =============================================================================

New-Item -ItemType Directory -Force -Path $evidencias | Out-Null

if ($Reiniciar) {
    Escribir-Titulo 'Reiniciando el sistema desde cero (docker compose down -v)'
    Docker-Compose @('down', '-v')
    Docker-Compose @('up', '-d', '--build')
}
else {
    Docker-Compose @('up', '-d')
}

Esperar-Servicio 'pedidos-api' "$api/swagger/index.html"
Esperar-Servicio 'inventario' "$inventario/api/productos"
Esperar-Servicio 'pagos' "$pagos/api/pagos?pedidoId=00000000-0000-0000-0000-000000000000"
Esperar-Servicio 'logistica' "$logistica/api/envios?fecha=2030-01-01"
Start-Sleep -Seconds 3 # que los consumidores terminen de conectarse a RabbitMQ

# =============================================================================
# E1 - Todo funciona
# =============================================================================
Escribir-Titulo 'E1 - Todo funciona'
$fecha = Fecha-Con-Cupo
$stockAntes = Stock 'P001'
$pedidoId = Crear-Pedido $dosTeclados
$sagaId = Confirmar-Pedido $pedidoId $tarjetaValida $fecha
$saga = Esperar-Saga $sagaId @('Completada', 'Compensada', 'Fallida')
$consulta = Consultar-Pedido $pedidoId

Verificar 'E1' 'Estado de la saga' 'Completada' $saga.estado
Verificar 'E1' 'Pasos' 'ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Completado' (Nombres-Pasos $saga)
Verificar 'E1' 'Estado del pedido' 'Confirmado' $consulta.pedido.estado
Verificar 'E1' 'Stock de P001 (baja 2)' ($stockAntes - 2) (Stock 'P001')

Guardar-Evidencia 'E1-todo-funciona.json' ([ordered]@{
    escenario = 'E1 - Todo funciona'
    requests  = [ordered]@{ item = $dosTeclados; tarjeta = $tarjetaValida; fechaEnvioPreferida = $fecha }
    pedidoId  = $pedidoId
    sagaId    = $sagaId
    pedido    = $consulta.pedido
    eventos   = $consulta.eventos
    saga      = $saga
    colas     = Estado-Colas
})

# =============================================================================
# E2 - Falla Inventario (sin stock)
# =============================================================================
Escribir-Titulo 'E2 - Falla Inventario: 3 notebooks con stock 2'
$stockAntes = Stock 'P005'
$pedidoId = Crear-Pedido $tresNotebooks
$sagaId = Confirmar-Pedido $pedidoId $tarjetaValida $null
$saga = Esperar-Saga $sagaId @('Completada', 'Compensada', 'Fallida')
$consulta = Consultar-Pedido $pedidoId
$pagosDelPedido = Lista "$pagos/api/pagos?pedidoId=$pedidoId"

Verificar 'E2' 'Estado de la saga' 'Fallida' $saga.estado
Verificar 'E2' 'Pasos' 'ReservarStock:Fallido' (Nombres-Pasos $saga)
Verificar 'E2' 'Estado del pedido' 'Rechazado' $consulta.pedido.estado
Verificar 'E2' 'Motivo del rechazo' 'STOCK_INSUFICIENTE' $consulta.pedido.motivoRechazo
Verificar 'E2' 'Stock de P005 (sin cambios)' $stockAntes (Stock 'P005')
Verificar 'E2' 'Pagos del pedido (no se cobró)' 0 $pagosDelPedido.Count

Guardar-Evidencia 'E2-falla-inventario.json' ([ordered]@{
    escenario = 'E2 - Falla Inventario (sin stock)'
    requests  = [ordered]@{ item = $tresNotebooks; tarjeta = $tarjetaValida }
    pedidoId  = $pedidoId
    sagaId    = $sagaId
    pedido    = $consulta.pedido
    eventos   = $consulta.eventos
    saga      = $saga
    verificacion = [ordered]@{ stockP005Antes = $stockAntes; stockP005Despues = (Stock 'P005'); pagosDelPedido = $pagosDelPedido }
    colas     = Estado-Colas
})

# =============================================================================
# E3 - Falla Pagos (tarjeta rechazada)
# =============================================================================
Escribir-Titulo 'E3 - Falla Pagos: tarjeta terminada en 0000'
$stockAntes = Stock 'P001'
$pedidoId = Crear-Pedido $dosTeclados
$sagaId = Confirmar-Pedido $pedidoId $tarjetaRechazada $null
$saga = Esperar-Saga $sagaId @('Completada', 'Compensada', 'Fallida')
$consulta = Consultar-Pedido $pedidoId
$pagosDelPedido = Lista "$pagos/api/pagos?pedidoId=$pedidoId"
$stockDespues = Stock 'P001'

Verificar 'E3' 'Estado de la saga' 'Compensada' $saga.estado
Verificar 'E3' 'Pasos' 'ReservarStock:Completado > Cobrar:Fallido > LiberarStock:Completado' (Nombres-Pasos $saga)
Verificar 'E3' 'Estado del pedido' 'Rechazado' $consulta.pedido.estado
Verificar 'E3' 'Motivo del rechazo' 'TARJETA_RECHAZADA' $consulta.pedido.motivoRechazo
Verificar 'E3' 'Stock de P001 (vuelve a su valor original)' $stockAntes $stockDespues
Verificar 'E3' 'Estado del pago' 'Rechazado' $pagosDelPedido[0].estado

Guardar-Evidencia 'E3-falla-pagos.json' ([ordered]@{
    escenario = 'E3 - Falla Pagos (tarjeta rechazada)'
    requests  = [ordered]@{ item = $dosTeclados; tarjeta = $tarjetaRechazada }
    pedidoId  = $pedidoId
    sagaId    = $sagaId
    pedido    = $consulta.pedido
    eventos   = $consulta.eventos
    saga      = $saga
    verificacion = [ordered]@{ stockP001Antes = $stockAntes; stockP001Despues = $stockDespues; pagosDelPedido = $pagosDelPedido }
    colas     = Estado-Colas
})

# =============================================================================
# E4 - Falla Logística (sin cupo)
# =============================================================================
Escribir-Titulo 'E4 - Falla Logistica: CupoDiario = 0'
$fecha = Fecha-Con-Cupo   # se calcula antes de bajar el cupo a 0
Write-Host 'Reiniciando logistica con CupoDiario = 0...'
$env:CUPO_DIARIO = '0'
try {
    Docker-Compose @('up', '-d', 'logistica')
    Esperar-Servicio 'logistica' "$logistica/api/envios?fecha=2030-01-01"
    Start-Sleep -Seconds 3

    $stockAntes = Stock 'P001'
    $pedidoId = Crear-Pedido $dosTeclados
    $sagaId = Confirmar-Pedido $pedidoId $tarjetaValida $fecha
    $saga = Esperar-Saga $sagaId @('Completada', 'Compensada', 'Fallida')
    $consulta = Consultar-Pedido $pedidoId
    $pagosDelPedido = Lista "$pagos/api/pagos?pedidoId=$pedidoId"
    $enviosDelDia = Invocar 'GET' "$logistica/api/envios?fecha=$fecha"
    $enviosDelPedido = @($enviosDelDia.envios | Where-Object { $_.pedidoId -eq $pedidoId -and $_.estado -eq 'Programado' })
    $stockDespues = Stock 'P001'

    Verificar 'E4' 'Estado de la saga' 'Compensada' $saga.estado
    Verificar 'E4' 'Pasos' 'ReservarStock:Completado > Cobrar:Completado > ProgramarEnvio:Fallido > Reembolsar:Completado > LiberarStock:Completado' (Nombres-Pasos $saga)
    Verificar 'E4' 'Estado del pedido' 'Rechazado' $consulta.pedido.estado
    Verificar 'E4' 'Motivo del rechazo' 'SIN_CUPO' $consulta.pedido.motivoRechazo
    Verificar 'E4' 'Stock de P001 (vuelve a su valor original)' $stockAntes $stockDespues
    Verificar 'E4' 'Estado del pago' 'Reembolsado' $pagosDelPedido[0].estado
    Verificar 'E4' 'Envios activos del pedido' 0 $enviosDelPedido.Count

    Guardar-Evidencia 'E4-falla-logistica.json' ([ordered]@{
        escenario = 'E4 - Falla Logistica (sin cupo para la fecha)'
        requests  = [ordered]@{ item = $dosTeclados; tarjeta = $tarjetaValida; fechaEnvioPreferida = $fecha; cupoDiario = 0 }
        pedidoId  = $pedidoId
        sagaId    = $sagaId
        pedido    = $consulta.pedido
        eventos   = $consulta.eventos
        saga      = $saga
        verificacion = [ordered]@{ stockP001Antes = $stockAntes; stockP001Despues = $stockDespues; pagosDelPedido = $pagosDelPedido; enviosDelDia = $enviosDelDia }
        colas     = Estado-Colas
    })
}
finally {
    Write-Host 'Volviendo logistica a CupoDiario = 3...'
    Remove-Item Env:CUPO_DIARIO -ErrorAction SilentlyContinue
    Docker-Compose @('up', '-d', 'logistica')
    Esperar-Servicio 'logistica' "$logistica/api/envios?fecha=2030-01-01"
    Start-Sleep -Seconds 3
}

# =============================================================================
# E5 - Pagos apagado al confirmar
# =============================================================================
Escribir-Titulo 'E5 - Pagos apagado al confirmar'
$fecha = Fecha-Con-Cupo
Write-Host 'docker compose stop pagos'
Docker-Compose @('stop', 'pagos')
try {
    $pedidoId = Crear-Pedido $dosTeclados
    $sagaId = Confirmar-Pedido $pedidoId $tarjetaValida $fecha
    $null = Esperar-Paso $sagaId 'Cobrar'
    Start-Sleep -Seconds 2

    $sagaDurante = Invocar 'GET' "$api/api/sagas/$sagaId"
    $consultaDurante = Consultar-Pedido $pedidoId
    $colasDurante = Esperar-Cola 'pagos.comandos' 1

    Verificar 'E5' 'Pedido con Pagos apagado' 'PendienteConfirmacion' $consultaDurante.pedido.estado
    Verificar 'E5' 'Saga con Pagos apagado' 'EnCurso' $sagaDurante.estado
    Verificar 'E5' 'Mensajes esperando en pagos.comandos' 1 $colasDurante['pagos.comandos'].ready
}
finally {
    Write-Host 'docker compose start pagos'
    Docker-Compose @('start', 'pagos')
}

$saga = Esperar-Saga $sagaId @('Completada', 'Compensada', 'Fallida') 120
$consulta = Consultar-Pedido $pedidoId
$colasDespues = Esperar-Cola 'pagos.comandos' 0

Verificar 'E5' 'Saga al levantar Pagos' 'Completada' $saga.estado
Verificar 'E5' 'Pedido al levantar Pagos' 'Confirmado' $consulta.pedido.estado
Verificar 'E5' 'Mensajes en pagos.comandos al terminar' 0 $colasDespues['pagos.comandos'].ready

Guardar-Evidencia 'E5-pagos-apagado.json' ([ordered]@{
    escenario = 'E5 - El servicio de Pagos esta apagado al confirmar'
    requests  = [ordered]@{ item = $dosTeclados; tarjeta = $tarjetaValida; fechaEnvioPreferida = $fecha }
    pedidoId  = $pedidoId
    sagaId    = $sagaId
    conPagosApagado = [ordered]@{ pedido = $consultaDurante.pedido; saga = $sagaDurante; colas = $colasDurante }
    alLevantarPagos = [ordered]@{ pedido = $consulta.pedido; eventos = $consulta.eventos; saga = $saga; colas = $colasDespues }
})

# =============================================================================
# Resumen
# =============================================================================
Escribir-Titulo 'Resumen de verificaciones'
$verificaciones | Format-Table Escenario, Verificacion, Esperado, Obtenido, Resultado -AutoSize | Out-Host

$lineas = New-Object System.Collections.Generic.List[string]
$lineas.Add('# Resumen de verificaciones de los escenarios E1 a E5')
$lineas.Add('')
$lineas.Add("Generado por ``scripts/escenarios.ps1`` el $((Get-Date).ToString('yyyy-MM-dd HH:mm')).")
$lineas.Add('')
$lineas.Add('| Escenario | Verificación | Esperado | Obtenido | Resultado |')
$lineas.Add('| --- | --- | --- | --- | --- |')
foreach ($v in $verificaciones) {
    $lineas.Add("| $($v.Escenario) | $($v.Verificacion) | $($v.Esperado) | $($v.Obtenido) | $($v.Resultado) |")
}
[IO.File]::WriteAllLines((Join-Path $evidencias 'resumen.md'), $lineas, (New-Object Text.UTF8Encoding $false))

$fallas = @($verificaciones | Where-Object { $_.Resultado -ne 'OK' }).Count
if ($fallas -eq 0) {
    Write-Host "Las $($verificaciones.Count) verificaciones dieron OK." -ForegroundColor Green
}
else {
    Write-Host "$fallas de $($verificaciones.Count) verificaciones fallaron." -ForegroundColor Red
    exit 1
}
