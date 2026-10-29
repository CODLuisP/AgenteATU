namespace AgenteATU.Models
{
    public class ClientConfig
    {
        public string AccountID { get; set; }
        public string NombreEmpresa { get; set; }
        public string? WebSocketUrl { get; set; }  // opcional: si no se define se arma con WebSocket:BaseUrl + Token
        public string RouteId { get; set; }  // se usará solo para cuentas de una sola ruta
        public string RutaActIda { get; set; }
        public string RutaActVuelta { get; set; }

        // Para cuentas con múltiples rutas: codRuta -> (routeId, codIda, codVuelta)
        public Dictionary<string, RutaInfo>? RutaMap { get; set; }
    }

    public class RutaInfo
    {
        public string RouteId { get; set; }
        public string CodIda { get; set; }
        public string CodVuelta { get; set; }
    }
}
