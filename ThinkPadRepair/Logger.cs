using System;
using System.Collections.Concurrent;
using System.IO;

namespace ThreeFingerDragOnWindows.utils;
public static class Logger {
    private static readonly ConcurrentQueue<string> entries = new();
    public static void Log(string message) {
        entries.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        while(entries.Count > 10000) entries.TryDequeue(out _);
    }
    public static void Export(string path) => File.WriteAllLines(path, entries.ToArray());
}
