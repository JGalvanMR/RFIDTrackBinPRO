using System;
using System.Collections.Concurrent;

public class RfidDuplicateDetector
{
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new ConcurrentDictionary<string, DateTime>();

    // 🔹 Ventana de tiempo en milisegundos (ajústalo)
    private readonly int _thresholdMs;

    public RfidDuplicateDetector(int thresholdMs = 2000)
    {
        _thresholdMs = thresholdMs;
    }

    /// <summary>
    /// Devuelve TRUE si el tag es válido (NO duplicado reciente)
    /// </summary>
    public bool ShouldProcess(string epc)
    {
        var now = DateTime.UtcNow;

        if (_lastSeen.TryGetValue(epc, out var lastTime))
        {
            var diff = (now - lastTime).TotalMilliseconds;

            if (diff < _thresholdMs)
            {
                // ❌ duplicado reciente → ignorar
                return false;
            }
        }

        // ✅ actualizar timestamp
        _lastSeen[epc] = now;
        return true;
    }

    /// <summary>
    /// Limpia memoria vieja (evita crecimiento infinito)
    /// </summary>
    public void Cleanup(int maxAgeMs = 10000)
    {
        var now = DateTime.UtcNow;

        foreach (var item in _lastSeen)
        {
            if ((now - item.Value).TotalMilliseconds > maxAgeMs)
            {
                _lastSeen.TryRemove(item.Key, out _);
            }
        }
    }
}