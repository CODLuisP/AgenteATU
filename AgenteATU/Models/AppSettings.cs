namespace AgenteATU.Models
{
    public class AgenteSettings
    {
        public string TituloConsola { get; set; } = "Agente ATU - Velsat";
        public int IntervaloConsultaMs { get; set; } = 7000;
        public int EsperaTrasErrorMs { get; set; } = 10000;
    }

    public class WebSocketSettings
    {
        public string BaseUrl { get; set; } = "";
        public string Token { get; set; } = "";
        public int MaxConnectionAgeMinutes { get; set; } = 10;
        public int MaxRetries { get; set; } = 3;
        public int KeepAliveSeconds { get; set; } = 30;
        public int ConnectTimeoutMs { get; set; } = 10000;
        public int SendTimeoutMs { get; set; } = 5000;
        public int ReceiveTimeoutMs { get; set; } = 10000;
        public int MaxDelayTramaSegundos { get; set; } = 600;
    }
}
