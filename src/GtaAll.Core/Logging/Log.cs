using System.Text;

namespace GtaAll.Core.Logging;

public enum LogLevel { Info, Warning, Error }

/// <summary>Journal commun. Les implémentations ne doivent jamais lever d'exception.</summary>
public interface ILog
{
    void Write(LogLevel level, string source, string message, Exception? ex = null);
}

public sealed class NullLog : ILog { public void Write(LogLevel level, string source, string message, Exception? ex = null) { } }

/// <summary>Accès global au journal courant (NullLog tant que l'application n'en a pas installé un).</summary>
public static class Log
{
    public static ILog Current { get; set; } = new NullLog();
    public static void Info(string source, string message) => Current.Write(LogLevel.Info, source, message);
    public static void Warn(string source, string message, Exception? ex = null) => Current.Write(LogLevel.Warning, source, message, ex);
    public static void Error(string source, string message, Exception? ex = null) => Current.Write(LogLevel.Error, source, message, ex);
}

/// <summary>Un fichier par jour dans le dossier choisi ; écriture sérialisée ; toute erreur d'E/S est avalée.</summary>
public sealed class FileLog : ILog
{
    private readonly string _dir; private readonly object _lock = new();
    public FileLog(string directory) { _dir = directory; }
    public string PathForToday => Path.Combine(_dir, $"gtaall_{DateTime.UtcNow:yyyyMMdd}.log");

    public void Write(LogLevel level, string source, string message, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.UtcNow.ToString("O")).Append(' ').Append(level.ToString().ToUpperInvariant()).Append(' ')
              .Append(source).Append(" : ").Append(message.ReplaceLineEndings(" "));
            if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message.ReplaceLineEndings(" "));
            lock (_lock) { Directory.CreateDirectory(_dir); File.AppendAllText(PathForToday, sb.AppendLine().ToString()); }
        }
        catch (Exception) { /* le journal ne doit jamais faire échouer l'application */ }
    }
}
