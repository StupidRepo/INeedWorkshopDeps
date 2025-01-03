using UnityEngine;

namespace INeedWorkshopDeps.Logger;

/// <summary>
/// Just a simple logger that prefixes messages with [INWD] so that they're easier to find in the console.
/// </summary>
internal static class Logger {
    private const string Prefix = "[INWD] ";
    
    public static void Log(string message) {
        Debug.Log(Prefix + message);
    }
    
    public static void LogWarning(string message) {
        Debug.LogWarning(Prefix + message);
    }
    
    public static void LogError(string message) {
        Debug.LogError(Prefix + message);
    }
}