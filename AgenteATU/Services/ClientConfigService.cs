using AgenteATU.Models;
using System.Collections.Generic;

namespace AgenteATU.Services
{
    public static class ClientConfigService
    {
        // Los clientes se leen de la sección "Clientes" de appsettings.json
        private static readonly List<ClientConfig> _configs = BuildConfigs();

        private static List<ClientConfig> BuildConfigs()
        {
            var ws = AppConfig.WebSocket;
            var configs = AppConfig.Clientes;

            foreach (var c in configs)
            {
                if (string.IsNullOrWhiteSpace(c.WebSocketUrl))
                    c.WebSocketUrl = $"{ws.BaseUrl}?token={ws.Token}";
            }

            return configs;
        }

        public static ClientConfig? GetByAccountID(string accountID)
            => _configs.Find(c => c.AccountID == accountID);

        public static List<ClientConfig> GetAll() => _configs;
    }
}
