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
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Java.IO;

namespace RFIDTrackBin
{
    public class GuardaLocal
    {
        // Versión síncrona conservada para compatibilidad.
        // ADVERTENCIA: bloquea el hilo llamante — no usar desde UI thread.
        public bool HayConexion(string direccionweb)
        {
            try
            {
                using (var client = new WebClient())
                using (client.OpenRead(direccionweb))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        // FIX G-1: Versión async no bloqueante para usar desde UI thread.
        public async Task<bool> HayConexionAsync(string direccionweb)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    using (var response = await client.GetAsync(
                        direccionweb,
                        HttpCompletionOption.ResponseHeadersRead))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public void creartxt(string error)
        {
            Java.IO.File sdCard = Android.App.Application.Context.GetExternalFilesDir(null);

            Java.IO.File dir = new Java.IO.File(sdCard.AbsolutePath + "/MyFolder");
            dir.Mkdirs();
            Java.IO.File file = new Java.IO.File(dir, "errores.txt");

            if (!file.Exists())
            {
                file.CreateNewFile();

                FileWriter writer = null;
                try
                {
                    writer = new FileWriter(file);
                    writer.Write(error + System.Environment.NewLine);
                    writer.Flush();
                }
                finally
                {
                    writer?.Close();
                }
            }
            else
            {
                string rutaarchivo = file.ToString();
                System.IO.File.AppendAllText(rutaarchivo, error + System.Environment.NewLine);
            }
        }
    }
}