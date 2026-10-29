using AgenteATU.Models;
using Microsoft.Extensions.Configuration;

namespace AgenteATU.Services
{
    /// <summary>
    /// Carga appsettings.json (y variables de entorno con prefijo AGENTEATU_) una sola vez.
    /// Ejemplo de override: AGENTEATU_ConnectionStrings__Default, AGENTEATU_WebSocket__Token
    /// </summary>
    public static class AppConfig
    {
        private static readonly IConfigurationRoot _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "AGENTEATU_")
            .Build();

        public static string ConnectionString =>
            _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta 'ConnectionStrings:Default' en appsettings.json");

        public static AgenteSettings Agente { get; } =
            _configuration.GetSection("Agente").Get<AgenteSettings>() ?? new AgenteSettings();

        public static WebSocketSettings WebSocket { get; } =
            _configuration.GetSection("WebSocket").Get<WebSocketSettings>() ?? new WebSocketSettings();

        public static List<ClientConfig> Clientes { get; } =
            _configuration.GetSection("Clientes").Get<List<ClientConfig>>() ?? new List<ClientConfig>();
    }
}
