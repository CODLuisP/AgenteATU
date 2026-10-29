using AgenteATU.Models;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AgenteATU.Services
{
    public class AtuWebSocketService : IDisposable
    {
        private ClientWebSocket _webSocket;
        private readonly string _url;
        private readonly ClientConfig _config;
        private bool _isConnected = false;
        private DatabaseService _dbService;
        private DateTime _lastConnectionTime;
        private int _messagesSentInCurrentConnection = 0;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, (double lat, double lon)> _lastSentPosition = new();

        // Parámetros leídos de la sección "WebSocket" de appsettings.json
        private static readonly WebSocketSettings _ws = AppConfig.WebSocket;
        private static int MAX_CONNECTION_AGE_MINUTES => _ws.MaxConnectionAgeMinutes;
        private static int MAX_RETRIES => _ws.MaxRetries;

        public AtuWebSocketService(DatabaseService dbService, ClientConfig config)
        {
            _webSocket = new ClientWebSocket();
            _dbService = dbService;
            _config = config;
            _url = config.WebSocketUrl;
            _lastConnectionTime = DateTime.MinValue;
        }

        // ─────────────────────────────────────────────────────────────
        //  CONEXIÓN
        // ─────────────────────────────────────────────────────────────

        private async Task<bool> EnsureConnectedAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_webSocket.State == WebSocketState.Aborted ||
                    _webSocket.State == WebSocketState.Closed)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{_config.NombreEmpresa}] ⟳ WebSocket en estado {_webSocket.State}, recreando...");
                    Console.ResetColor();

                    _webSocket?.Dispose();
                    _webSocket = new ClientWebSocket();
                    _isConnected = false;
                    _messagesSentInCurrentConnection = 0;
                }

                var connectionAge = DateTime.Now - _lastConnectionTime;

                if (_webSocket.State == WebSocketState.Open)
                {
                    if (connectionAge.TotalMinutes > MAX_CONNECTION_AGE_MINUTES)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"  [{_config.NombreEmpresa}] ⟳ Reconectando preventivamente ({connectionAge.TotalMinutes:F1} min)");
                        Console.ResetColor();
                        await DisconnectAsync();
                    }
                    else
                    {
                        return true;
                    }
                }

                if (_webSocket.State != WebSocketState.Open)
                    return await ConnectAsync();

                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [{_config.NombreEmpresa}] [ERROR] EnsureConnectedAsync: {ex.Message}");
                Console.ResetColor();

                try { _webSocket?.Dispose(); } catch { }
                _webSocket = new ClientWebSocket();
                _isConnected = false;
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                if (_webSocket != null)
                {
                    if (_webSocket.State == WebSocketState.Open ||
                        _webSocket.State == WebSocketState.CloseReceived)
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(2000);
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconexión programada", cts.Token);
                        }
                        catch (OperationCanceledException) { _webSocket.Abort(); }
                        catch (WebSocketException) { _webSocket.Abort(); }
                    }
                    else if (_webSocket.State != WebSocketState.Aborted &&
                             _webSocket.State != WebSocketState.Closed)
                    {
                        _webSocket.Abort();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  [{_config.NombreEmpresa}] [WARN] Error al desconectar: {ex.Message}");
                Console.ResetColor();
                try { _webSocket?.Abort(); } catch { }
            }
            finally
            {
                try { _webSocket?.Dispose(); } catch { }
                _webSocket = new ClientWebSocket();
                _isConnected = false;
                _messagesSentInCurrentConnection = 0;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                    return true;

                if (_webSocket.State != WebSocketState.None)
                {
                    try { _webSocket.Dispose(); } catch { }
                    _webSocket = new ClientWebSocket();
                }

                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(_ws.KeepAliveSeconds);

                using var cts = new CancellationTokenSource(_ws.ConnectTimeoutMs);
                await _webSocket.ConnectAsync(new Uri(_url), cts.Token);

                _isConnected = true;
                _lastConnectionTime = DateTime.Now;
                _messagesSentInCurrentConnection = 0;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [{_config.NombreEmpresa}] ✓ Conectado al WebSocket");
                Console.ResetColor();

                return true;
            }
            catch (OperationCanceledException)
            {
                _isConnected = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [{_config.NombreEmpresa}] [ERROR] Timeout al conectar (>{_ws.ConnectTimeoutMs / 1000}s)");
                Console.ResetColor();
                return false;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [{_config.NombreEmpresa}] [ERROR] No se pudo conectar: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  ENVÍO
        // ─────────────────────────────────────────────────────────────

        public async Task<AtuResponse> SendDeviceDataAsync(Device device)
        {
            string jsonData = "";
            string responseText = "";

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    // Validar tsinitialtrip
                    if (device.FecIniRuta <= 0)
                    {
                        return new AtuResponse
                        {
                            Success = false,
                            Code = "SKIP",
                            Message = "Sin fecha de inicio de ruta (tsinitialtrip = 0), no se envía"
                        };
                    }

                    if (!await EnsureConnectedAsync())
                    {
                        if (attempt < MAX_RETRIES)
                        {
                            await Task.Delay(1000 * attempt);
                            continue;
                        }
                        return new AtuResponse { Success = false, Code = "ERROR", Message = "No se pudo establecer conexión" };
                    }

                    // Procesar IMEI
                    string imeiToSend = device.ImeiNumber;
                    if (!string.IsNullOrEmpty(imeiToSend) && imeiToSend.StartsWith("0") && imeiToSend.Length == 16)
                        imeiToSend = imeiToSend.Substring(1);

                    // Procesar placa
                    string licensePlate = ProcessLicensePlate(device.DeviceID);

                    // ── FILTRO POSICIÓN REPETIDA ──────────────────────────────
                    if (_lastSentPosition.TryGetValue(licensePlate, out var lastPos))
                    {
                        if (lastPos.lat == device.LastValidLatitude &&
                            lastPos.lon == device.LastValidLongitude)
                        {
                            return new AtuResponse
                            {
                                Success = false,
                                Code = "SKIP",
                                Message = "Posición sin cambio, trama omitida"
                            };
                        }
                    }
                    _lastSentPosition[licensePlate] = (device.LastValidLatitude ?? 0, device.LastValidLongitude ?? 0);
                    // ─────────────────────────────────────────────────────────

                    // direction_id
                    int directionId = GetDirectionId(device.RutaAct);

                    // Obtener routeId y verificar que existe
                    string routeId = GetRouteId(device.RutaAct);
                    if (routeId == null)
                    {
                        return new AtuResponse
                        {
                            Success = false,
                            Code = "SKIP",
                            Message = $"RutaAct '{device.RutaAct}' no encontrada en RutaMap"
                        };
                    }

                    // Validaciones
                    var validationResult = ValidateDevice(imeiToSend, licensePlate, device, routeId, directionId);

                    if (!validationResult.Success)
                    {
                        PrintValidationError(licensePlate, validationResult);
                        return validationResult;
                    }

                    // Timestamps a milisegundos
                    long tsMs = (long)(device.LastGPSTimestamp ?? 0) * 1000;
                    long tsinitialTripMs = (long)device.FecIniRuta * 1000;

                    // Después de calcular tsMs
                    long ahoraMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    long diferenciaSegundos = (ahoraMs - tsMs) / 1000;

                    if (diferenciaSegundos > _ws.MaxDelayTramaSegundos)
                    {
                        return new AtuResponse
                        {
                            Success = false,
                            Code = "SKIP",
                            Message = $"Trama muy antigua ({diferenciaSegundos}s de delay), descartada"
                        };
                    }

                    var data = new
                    {
                        imei = imeiToSend,
                        latitude = device.LastValidLatitude,
                        longitude = device.LastValidLongitude,
                        route_id = GetRouteId(device.RutaAct),
                        ts = tsMs,
                        license_plate = licensePlate,
                        speed = device.LastValidSpeed,
                        direction_id = directionId,
                        driver_id = string.IsNullOrEmpty(device.DniConductor) ? "0" : device.DniConductor,
                        tsinitialtrip = tsinitialTripMs
                    };

                    jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
                    byte[] buffer = Encoding.UTF8.GetBytes(jsonData);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  → [{_config.NombreEmpresa}] Enviando: {jsonData}");
                    Console.ResetColor();

                    using (var cts = new CancellationTokenSource(_ws.SendTimeoutMs))
                    {
                        await _webSocket.SendAsync(
                            new ArraySegment<byte>(buffer),
                            WebSocketMessageType.Text,
                            true,
                            cts.Token);
                    }

                    _messagesSentInCurrentConnection++;

                    var response = await ReceiveResponseAsync();
                    responseText = $"{{\"codigo\":\"{response.Code}\",\"mensaje\":\"{response.Message}\"}}";

                    await _dbService.InsertarAuditoriaAsync(jsonData, responseText);

                    PrintResponse(licensePlate, response);

                    return response;
                }
                catch (WebSocketException wex) when (attempt < MAX_RETRIES)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{_config.NombreEmpresa}] ⚠ Error WebSocket (intento {attempt}/{MAX_RETRIES}): {wex.Message}");
                    Console.ResetColor();
                    await DisconnectAsync();
                    await Task.Delay(1000 * attempt);
                }
                catch (WebSocketException wex)
                {
                    var errorResponse = new AtuResponse { Success = false, Code = "WEBSOCKET_ERROR", Message = $"Error WebSocket: {wex.Message}" };
                    responseText = $"{{\"codigo\":\"WEBSOCKET_ERROR\",\"mensaje\":\"{wex.Message}\"}}";
                    await _dbService.InsertarAuditoriaAsync(jsonData, responseText);

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [{_config.NombreEmpresa}] ✗ ERROR WEBSOCKET (después de {MAX_RETRIES} intentos): {wex.Message}");
                    Console.ResetColor();

                    await DisconnectAsync();
                    return errorResponse;
                }
                catch (Exception ex)
                {
                    var errorResponse = new AtuResponse { Success = false, Code = "EXCEPTION", Message = ex.Message };
                    responseText = $"{{\"codigo\":\"EXCEPTION\",\"mensaje\":\"{ex.Message}\"}}";
                    await _dbService.InsertarAuditoriaAsync(jsonData, responseText);

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [{_config.NombreEmpresa}] ✗ EXCEPCIÓN: {ex.Message}");
                    Console.ResetColor();

                    _isConnected = false;
                    return errorResponse;
                }
            }

            return new AtuResponse { Success = false, Code = "MAX_RETRIES", Message = "Se agotaron los reintentos" };
        }

        // ─────────────────────────────────────────────────────────────
        //  RECEPCIÓN DE RESPUESTA
        // ─────────────────────────────────────────────────────────────

        private async Task<AtuResponse> ReceiveResponseAsync()
        {
            try
            {
                var buffer = new byte[4096];
                using var cts = new CancellationTokenSource(_ws.ReceiveTimeoutMs);

                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string responseText = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    try
                    {
                        var jsonDoc = JsonDocument.Parse(responseText);
                        var root = jsonDoc.RootElement;

                        string code = root.TryGetProperty("codigo", out var codigoEl)
                            ? codigoEl.GetString()
                            : (root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null);

                        string message = root.TryGetProperty("mensaje", out var mensajeEl)
                            ? mensajeEl.GetString()
                            : (root.TryGetProperty("message", out var messageEl) ? messageEl.GetString() : null);

                        if (code != null)
                        {
                            return new AtuResponse
                            {
                                Success = code == "00",
                                Code = code,
                                Message = message ?? GetErrorDescription(code)
                            };
                        }

                        return new AtuResponse { Success = false, Code = "UNKNOWN", Message = $"Formato desconocido: {responseText}" };
                    }
                    catch (JsonException)
                    {
                        return new AtuResponse { Success = false, Code = "INVALID_JSON", Message = $"Respuesta no es JSON: {responseText}" };
                    }
                }

                return new AtuResponse { Success = false, Code = "ERROR", Message = "Respuesta inválida" };
            }
            catch (OperationCanceledException)
            {
                return new AtuResponse { Success = false, Code = "TIMEOUT", Message = "Timeout esperando respuesta" };
            }
            catch (WebSocketException wex)
            {
                return new AtuResponse { Success = false, Code = "WEBSOCKET_ERROR", Message = wex.Message };
            }
            catch (Exception ex)
            {
                return new AtuResponse { Success = false, Code = "ERROR", Message = ex.Message };
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────

        private string ProcessLicensePlate(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "";
            if (!char.IsDigit(deviceId[0])) return deviceId;

            int guionIndex = deviceId.IndexOf('-');
            return (guionIndex > 0 && guionIndex < deviceId.Length - 1)
                ? deviceId.Substring(guionIndex + 1)
                : deviceId;
        }

        private string GetRouteId(string rutaAct)
        {
            rutaAct = rutaAct?.Trim() ?? "";
            if (_config.RutaMap != null && _config.RutaMap.TryGetValue(rutaAct, out var info))
                return info.RouteId;
            if (_config.RutaMap != null)
                return null;
            return _config.RouteId;
        }

        private int GetDirectionId(string rutaAct)
        {
            rutaAct = rutaAct?.Trim() ?? "";
            if (_config.RutaMap != null && _config.RutaMap.TryGetValue(rutaAct, out var info))
                return rutaAct == info.CodVuelta ? 1 : 0;
            if (string.IsNullOrEmpty(rutaAct)) return 0;
            if (rutaAct == _config.RutaActVuelta) return 1;
            return 0;
        }

        private AtuResponse ValidateDevice(string imei, string licensePlate, Device device, string routeId, int directionId)
        {
            if (string.IsNullOrEmpty(imei) || imei.Length != 15 || !Regex.IsMatch(imei, @"^[a-zA-Z0-9]{15}$"))
                return new AtuResponse { Success = false, Code = "06", Message = $"IMEI inválido: debe tener 15 caracteres alfanuméricos (actual: {imei})" };

            if (string.IsNullOrEmpty(licensePlate) || licensePlate.Length > 7 || !Regex.IsMatch(licensePlate, @"^[a-zA-Z0-9\-]+$"))
                return new AtuResponse { Success = false, Code = "07", Message = $"Placa inválida: máximo 7 caracteres alfanuméricos (actual: {licensePlate})" };

            if (device.LastValidLatitude < -90 || device.LastValidLatitude > 90 ||
                device.LastValidLongitude < -180 || device.LastValidLongitude > 180)
                return new AtuResponse { Success = false, Code = "08", Message = $"Coordenadas inválidas: lat={device.LastValidLatitude}, lon={device.LastValidLongitude}" };

            if (device.LastValidSpeed < 0 || device.LastValidSpeed > 999.99)
                return new AtuResponse { Success = false, Code = "09", Message = $"Velocidad inválida: {device.LastValidSpeed} km/h" };

            if (string.IsNullOrEmpty(routeId) || routeId.Length > 10)
                return new AtuResponse { Success = false, Code = "12", Message = "ID de ruta inválido: máximo 10 caracteres" };

            if (directionId != 0 && directionId != 1)
                return new AtuResponse { Success = false, Code = "13", Message = $"ID de dirección inválido: {directionId}" };

            string driverId = string.IsNullOrEmpty(device.DniConductor) ? "0" : device.DniConductor;
            if (driverId.Length > 20)
                return new AtuResponse { Success = false, Code = "14", Message = "ID de conductor inválido: máximo 20 caracteres" };

            return new AtuResponse { Success = true, Code = "OK", Message = "Validación exitosa" };
        }

        private void PrintValidationError(string licensePlate, AtuResponse response)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"  ⚠ [{_config.NombreEmpresa}] Validación fallida - Placa: {licensePlate} | [{response.Code}] {response.Message}");
            Console.ResetColor();
        }

        private void PrintResponse(string licensePlate, AtuResponse response)
        {
            switch (response.Code)
            {
                case "00":
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  ✓ ACEPTADO  [{_config.NombreEmpresa}] Placa: {licensePlate} | [{response.Code}] {response.Message}");
                    break;
                case "OK":
                case "SKIP":
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  ⊗ OMITIDO   [{_config.NombreEmpresa}] Placa: {licensePlate} | {response.Message}");
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  ✗ RECHAZADO [{_config.NombreEmpresa}] Placa: {licensePlate} | [{response.Code}] {response.Message}");
                    break;
            }
            Console.ResetColor();
        }

        private string GetErrorDescription(string code) => code switch
        {
            "00" => "Se recepcionó la trama correctamente",
            "01" => "Verificar el formato de la trama enviada",
            "03" => "InvalidToken",
            "05" => "El identificador de la trama está vacía",
            "06" => "IMEI inválido",
            "07" => "Placa inválida",
            "08" => "Coordenadas inválidas",
            "09" => "Velocidad inválida",
            "10" => "Operador inválido",
            "11" => "Identificador inválido",
            "12" => "ID de ruta inválido",
            "13" => "ID de dirección inválido",
            "14" => "ID de conductor inválido",
            _ => "Código desconocido"
        };

        public void Dispose()
        {
            try { DisconnectAsync().Wait(2000); }
            catch { }
            finally { _connectionLock?.Dispose(); }
        }
    }

    public class AtuResponse
    {
        public bool Success { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}