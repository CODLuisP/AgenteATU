using System;
using System.Collections.Generic;
using System.Text;

namespace AgenteATU.Models
{
    public class Device
    {
        public string ImeiNumber { get; set; }
        public double? LastValidLatitude { get; set; }
        public double? LastValidLongitude { get; set; }
        public string AccountID { get; set; }
        public int? LastGPSTimestamp { get; set; }
        public string DeviceID { get; set; }
        public double? LastValidSpeed { get; set; }
        public string RutaAct { get; set; }
        public string? CodConductorAct { get; set; }
        public int FecIniRuta { get; set; }
        public string DniConductor { get; set; }  // NUEVO CAMPO


        public override string ToString()
        {
            return $"[{AccountID}] Device: {DeviceID} | IMEI: {ImeiNumber} | " +
                   $"Pos: ({LastValidLatitude:F6}, {LastValidLongitude:F6}) | " +
                   $"Speed: {LastValidSpeed:F2} km/h | " +
                   $"Time: {LastGPSTimestamp} | " +
                   $"Ruta: {RutaAct} | Conductor: {CodConductorAct} | DNI: {DniConductor} | " +
                   $"FecIni: {FecIniRuta}";
        }
    }
}
