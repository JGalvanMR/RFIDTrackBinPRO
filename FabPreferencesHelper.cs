using Android.Content;

namespace RFIDTrackBin
{
    public static class FabPreferencesHelper
    {
        private const string PrefsName = "FabPreferences";
        private const string KeyFabX = "FabX";
        private const string KeyFabY = "FabY";

        // FIX FAB-1: Reemplazado PreferenceManager.GetDefaultSharedPreferences
        // (deprecated desde API 29, Android 10) por context.GetSharedPreferences
        // con nombre de archivo explícito. Comportamiento idéntico para todos
        // los call sites existentes; no requiere migración de datos.
        public static void SaveFabPosition(Context context, float x, float y)
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();
            editor.PutFloat(KeyFabX, x);
            editor.PutFloat(KeyFabY, y);
            editor.Apply();
        }

        public static (float x, float y) GetFabPosition(Context context)
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            float x = prefs.GetFloat(KeyFabX, -1);
            float y = prefs.GetFloat(KeyFabY, -1);
            return (x, y);
        }

        public static void ClearFabPosition(Context context)
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();
            editor.Remove(KeyFabX);
            editor.Remove(KeyFabY);
            editor.Apply();
        }
    }
}