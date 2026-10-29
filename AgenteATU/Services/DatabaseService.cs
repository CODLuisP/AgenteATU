using MySqlConnector;
using AgenteATU.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgenteATU.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly List<string> _accountIds;

        public DatabaseService()
        {
            _connectionString = AppConfig.ConnectionString;
            _accountIds = ClientConfigService.GetAll().Select(c => c.AccountID).ToList();
        }

        public async Task<List<Device>> GetDevicesAsync()
        {
            var devices = new List<Device>();

            if (_accountIds.Count == 0)
                return devices;

            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Cuentas tomadas de la sección "Clientes" de appsettings.json
                    string inParams = string.Join(", ", _accountIds.Select((_, i) => $"@acc{i}"));

                    string query = $@"SELECT
                        d.imeiNumber, 
                        d.lastValidLatitude, 
                        d.lastValidLongitude, 
                        d.accountID, 
                        d.lastGPSTimestamp, 
                        d.deviceID, 
                        d.lastValidSpeed, 
                        d.rutaact, 
                        d.codconductoract, 
                        d.feciniruta,
                        IFNULL(t.dni, '0') AS dni
                    FROM device d
                    LEFT JOIN taxi t ON d.codconductoract = t.codtaxi
                    WHERE d.accountID IN ({inParams})
                    ";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        for (int i = 0; i < _accountIds.Count; i++)
                            command.Parameters.AddWithValue($"@acc{i}", _accountIds[i]);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var device = new Device
                                {
                                    ImeiNumber = reader.IsDBNull(0) ? "" : reader.GetString(0),
                                    LastValidLatitude = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                                    LastValidLongitude = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                                    AccountID = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                    LastGPSTimestamp = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                                    DeviceID = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                    LastValidSpeed = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
                                    RutaAct = reader.IsDBNull(7) ? "0" : reader.GetString(7).Trim(),
                                    CodConductorAct = reader.IsDBNull(8) ? null : reader.GetString(8),
                                    FecIniRuta = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                                    DniConductor = reader.IsDBNull(10) ? "0" : reader.GetString(10)
                                };

                                devices.Add(device);
                            }
                        }
                    }
                }

                return devices;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Error al consultar la base de datos: {ex.Message}");
                Console.ResetColor();
                return devices;
            }
        }

        public async Task InsertarAuditoriaAsync(string tramaEnviada, string respuesta)
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"INSERT INTO auditoria_atu (trama_enviada, respuesta, fecha)
                            VALUES (@trama, @respuesta, @fecha)";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@trama", tramaEnviada ?? "");
                        command.Parameters.AddWithValue("@respuesta", respuesta ?? "");
                        command.Parameters.AddWithValue("@fecha", DateTime.Now);

                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // No detenemos el flujo principal por un error de auditoría
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"  [WARN] Error al insertar auditoría: {ex.Message}");
                Console.ResetColor();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using (var connection = new MySqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] No se pudo conectar a la base de datos: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }
    }
}
