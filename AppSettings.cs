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

namespace RFIDTrackBin
{
    public static class AppSettings
    {
#if DEBUG
        public static bool ModoPrueba => true;
#else
        public static bool ModoPrueba => false; // En Release SIEMPRE guarda
#endif

        // ── Salida de envases (SistemaEnvases) ─────────────────────────────
        // Cada bin leído en una salida RFID equivale a 1 caja del envase/producto
        // configurado aquí. Deben existir en tb_cat_envases / tb_cat_producto.
        // 80 = CAJONES ARMABLES (tb_cat_envases); "vacios" = producto creado en tb_cat_producto.
        public const string EnvaseSalidaClave = "80";
        public const string ProductoSalidaClave = "vacios";
    }
}