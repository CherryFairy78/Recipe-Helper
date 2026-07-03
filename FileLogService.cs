using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DalamudRecipeHelper;

public sealed class FileLogService
{
    private const int RetentionDays = 30;
    private readonly string logDirectory;
    private readonly object writeLock = new();
    private DateOnly lastCleanupDate;

    public FileLogService(string pluginConfigDirectory)
    {
        this.logDirectory = Path.Combine(pluginConfigDirectory, "Logs");
        Directory.CreateDirectory(this.logDirectory);
        this.RemoveExpiredLogs();
        this.lastCleanupDate = DateOnly.FromDateTime(DateTime.Now);
    }

    public string LogDirectory => this.logDirectory;

    public void Info(string component, string message) =>
        this.Write("INFO", component, message);

    public void Warning(string component, string message) =>
        this.Write("WARN", component, message);

    public void Error(string component, string message, Exception? exception = null) =>
        this.Write(
            "ERROR",
            component,
            exception is null
                ? message
                : $"{message} | {exception.GetType().Name}: {exception.Message}");

    public string GetLatestLogPath()
    {
        try
        {
            return new DirectoryInfo(this.logDirectory)
                .EnumerateFiles("recipe-helper-*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault() ?? string.Empty;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not locate its latest file log.");
            return string.Empty;
        }
    }

    public IReadOnlyList<string> GetRecentLines(int maxLines)
    {
        if (maxLines <= 0)
            return [];

        try
        {
            var latestLogPath = this.GetLatestLogPath();
            if (string.IsNullOrWhiteSpace(latestLogPath) || !File.Exists(latestLogPath))
                return [];

            return File.ReadLines(latestLogPath)
                .TakeLast(maxLines)
                .ToArray();
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not read recent log lines.");
            return [$"[log-read-failed] {exception.GetType().Name}: {exception.Message}"];
        }
    }

    public void ClearLogs()
    {
        try
        {
            lock (this.writeLock)
            {
                foreach (var file in new DirectoryInfo(this.logDirectory).EnumerateFiles("recipe-helper-*.log"))
                    file.Delete();
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not clear its file logs.");
        }
    }

    private void Write(string level, string component, string message)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var today = DateOnly.FromDateTime(now.LocalDateTime);
            if (today != this.lastCleanupDate)
            {
                this.RemoveExpiredLogs();
                this.lastCleanupDate = today;
            }

            var path = Path.Combine(
                this.logDirectory,
                $"recipe-helper-{now:yyyy-MM-dd}.log");
            var cleanMessage = message
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{component}] {cleanMessage}{Environment.NewLine}";

            lock (this.writeLock)
                File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not write its file log.");
        }
    }

    private void RemoveExpiredLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            var files = new DirectoryInfo(this.logDirectory)
                .EnumerateFiles("recipe-helper-*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff))
                file.Delete();

            foreach (var file in files
                         .Where(file => file.Exists)
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(RetentionDays))
                file.Delete();
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not clean up its old file logs.");
        }
    }
}
