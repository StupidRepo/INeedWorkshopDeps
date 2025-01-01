using UnityEngine;

namespace INeedWorkshopDeps;

/// <summary>
/// Just a simple logger that prefixes messages with [INWD] so that they're easier to find in the console.
/// </summary>
internal static class Logger {
    public static void Log(string message) {
        Debug.Log($"[INWD] {message}");
    }
    
    public static void LogWarning(string message) {
        Debug.LogWarning($"[INWD] {message}");
    }
    
    public static void LogError(string message) {
        Debug.LogError($"[INWD] {message}");
    }
}