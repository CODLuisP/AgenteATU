# GPS Retransmitter - Sistema Velsat

Aplicación de consola para monitorear dispositivos GPS desde la base de datos de gestión de villa.

## Características

- ✅ Consulta automática cada 15 segundos
- ✅ Conexión a MySQL
- ✅ Visualización en tiempo real en consola
- ✅ Manejo de errores y reconexión
- ✅ Código preparado para agregar retransmisión a endpoint

## Requisitos

- .NET 6.0 o superior
- Conexión a la base de datos MySQL (63.251.107.133:3306)

## Instalación

1. Asegúrate de tener .NET 6.0 o superior instalado
2. Restaura las dependencias:
   ```bash
   dotnet restore
   ```

## Ejecución

```bash
dotnet run
```

## Uso

- La aplicación se conectará a la base de datos automáticamente
- Consultará los dispositivos cada 15 segundos
- Mostrará la información en consola con colores
- Presiona **ESC** para detener la aplicación

## Datos Monitoreados

Para cada dispositivo se muestra:
- Account ID
- Device ID
- IMEI
- Última posición (latitud, longitud)
- Velocidad
- Timestamp GPS
- Ruta actual
- Código conductor actual

## Cuentas Monitoreadas

- etudv22
- etudvrg
- etudvrb
- transporvilla

## Próximos Pasos

1. ✅ Obtener datos de DB cada 15 segundos
2. ⏳ Agregar retransmisión al endpoint
3. ⏳ Implementar sistema de logs
4. ⏳ Agregar manejo de datos no enviados (buffer)

## Estructura del Proyecto

```
GpsRetransmitter/
├── Models/
│   └── Device.cs          # Modelo de datos del dispositivo
├── Services/
│   └── DatabaseService.cs # Servicio de acceso a base de datos
├── Program.cs             # Punto de entrada principal
└── GpsRetransmitter.csproj
```