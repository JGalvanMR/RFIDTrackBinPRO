using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RFIDTrackBin.Helpers
{
    public static class HoraServidorService
    {
        private static readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        public class ResultadoHora
        {
            public DateTime Hora { get; set; }
            public bool MostrarInventario { get; set; }
            public bool MostrarEntradas { get; set; }
            public bool MostrarSalidas { get; set; }
        }

        public static async Task<ResultadoHora?> ObtenerAsync()
        {
            try
            {
                string url = "https://tuservidor.com/api/hora.php";  // cámbialo
                var json = await _client.GetStringAsync(url);
                var j = JObject.Parse(json);

                return new ResultadoHora
                {
                    Hora = DateTime.Parse(j["hora"]!.ToString()), // ISO‑8601 → DateTime
                    MostrarInventario = j["mostrar_inventario"]!.Value<bool>(),
                    MostrarEntradas = j["mostrar_entradas"]!.Value<bool>(),
                    MostrarSalidas = j["mostrar_salidas"]!.Value<bool>()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error hora servidor: " + ex);
                return null; // Maneja null en la UI
            }
        }
    }
}