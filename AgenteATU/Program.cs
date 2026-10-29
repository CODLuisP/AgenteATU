using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AgenteATU.Models;
using AgenteATU.Services;

namespace AgenteATU
{
    class Program
    {
        private static DatabaseService _dbService;
        private static Dictionary<string, AtuWebSocketService> _atuServices = new();
        private static CancellationTokenSource _cancellationTokenSource;
        private static int _queryCount = 0;

        // En Docker (sin -it) no hay consola interactiva: se corre como servicio y se detiene con SIGTERM/SIGINT
        private static readonly bool _isInteractive = !Console.IsInputRedirected;


        static async Task Main(string[] args)
        {
            try { Console.Title = AppConfig.Agente.TituloConsola; } catch { }
            PrintHeader();

            _dbService = new DatabaseService();
            _cancellationTokenSource = new CancellationTokenSource();

            // Probar conexión a DB
            Console.WriteLine("Probando conexión a la base de datos...");
            bool connectionOk = await _dbService.TestConnectionAsync();

            if (!connectionOk)
            {
                ExitWithError();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Conexión exitosa a la base de datos");
            Console.ResetColor();

            // Conectar WebSocket de cada cliente configurado
            Console.WriteLine("\nConectando WebSockets de todos los clientes...");
            Console.WriteLine(new string('─', 120));

            var configs = ClientConfigService.GetAll();

            if (configs.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No hay clientes configurados en la sección 'Clientes' de appsettings.json.");
                Console.ResetColor();
                ExitWithError();
                return;
            }

            foreach (var config in configs)
            {
                Console.Write($"  → {config.NombreEmpresa} ({config.AccountID}) ... ");
                var service = new AtuWebSocketService(_dbService, config);
                bool connected = await service.ConnectAsync();

                if (!connected)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("    ⚠ No se pudo conectar al iniciar (se reintentará durante el monitoreo)");
                    Console.ResetColor();
                }

                // Se agrega de todas formas; EnsureConnectedAsync lo reconectará si hace falta
                _atuServices[config.AccountID] = service;
            }

            Console.WriteLine(new string('─', 120));
            Console.WriteLine($"\nClientes registrados: {_atuServices.Count}");
            Console.WriteLine($"\nIniciando monitoreo y envío cada {AppConfig.Agente.IntervaloConsultaMs / 1000} segundos...");
            Console.WriteLine(_isInteractive ? "Presiona ESC para detener\n" : "Modo servicio: se detiene con SIGTERM (docker stop)\n");
            Console.WriteLine(new string('=', 120));

            // Iniciar el monitoreo
            var monitorTask = MonitorDevicesAsync(_cancellationTokenSource.Token);

            if (_isInteractive)
            {
                // Esperar a que el usuario presione ESC
                while (Console.ReadKey(true).Key != ConsoleKey.Escape) { }
            }
            else
            {
                // Sin consola: esperar señal de parada (docker stop / Ctrl+C)
                var stopTcs = new TaskCompletionSource();
                using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stopTcs.TrySetResult(); });
                using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stopTcs.TrySetResult(); });
                await stopTcs.Task;
            }

            Console.WriteLine("\n\nDeteniendo monitoreo...");
            _cancellationTokenSource.Cancel();
            await monitorTask;

            foreach (var svc in _atuServices.Values)
                svc.Dispose();

            Console.WriteLine("Aplicación finalizada.");
        }

        static async Task MonitorDevicesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _queryCount++;
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"\n╔════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗");
                    Console.WriteLine($"║ [{timestamp}] CONSULTA #{_queryCount}");
                    Console.WriteLine($"╚════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╝");
                    Console.ResetColor();

                    var devices = await _dbService.GetDevicesAsync();

                    if (devices.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"📊 Dispositivos encontrados: {devices.Count}\n");
                        Console.ResetColor();

                        int aceptados = 0;
                        int rechazados = 0;
                        int omitidos = 0;
                        int sinConfig = 0;

                        foreach (var device in devices)
                        {
                            // Buscar el servicio WebSocket correspondiente al accountID del dispositivo
                            if (!_atuServices.TryGetValue(device.AccountID, out var atuService))
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine($"  ⊘ Sin configuración para accountID '{device.AccountID}' — dispositivo omitido ({device.DeviceID})");
                                Console.ResetColor();
                                sinConfig++;
                                continue;
                            }


                            var resultado = await atuService.SendDeviceDataAsync(device);

                            if (resultado.Success)
                                aceptados++;
                            else if (resultado.Code == "SKIP")
                                omitidos++;
                            else
                                rechazados++;
                        }

                        Console.WriteLine(new string('─', 120));
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($"📈 RESUMEN: ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"{aceptados} aceptados");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($" | ");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write($"{rechazados} rechazados");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.Write($" | ");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"{omitidos} omitidos");
                        if (sinConfig > 0)
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write($" | ");
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.Write($"{sinConfig} sin configuración");
                        }
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("⚠ No se encontraron dispositivos.");
                        Console.ResetColor();
                    }

                    await Task.Delay(AppConfig.Agente.IntervaloConsultaMs, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Error en el monitoreo: {ex.Message}");
                    Console.ResetColor();

                    try { await Task.Delay(AppConfig.Agente.EsperaTrasErrorMs, cancellationToken); }
                    catch (TaskCanceledException) { break; }
                }
            }
        }

        static void ExitWithError()
        {
            // Código de salida != 0 para que Docker (restart: unless-stopped) reintente
            Environment.ExitCode = 1;
            if (_isInteractive)
            {
                Console.WriteLine("\nPresiona cualquier tecla para salir...");
                Console.ReadKey();
            }
        }

        static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════╗
║                                                               ║
║              AGENTE ATU - VELSAT SYSTEM                       ║
║          Retransmisión de Datos GPS a ATU                     ║
║                                                               ║
╚═══════════════════════════════════════════════════════════════╝
");
            Console.ResetColor();
        }
    }
}