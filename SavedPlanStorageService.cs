using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DalamudRecipeHelper;

internal sealed class SavedPlanStorageService
{
    private const string BackupFileName = "saved-recipe-plans.json";
    private readonly string backupPath;
    private readonly FileLogService fileLog;
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true,
    };

    public SavedPlanStorageService(string configurationDirectory, FileLogService fileLog)
    {
        Directory.CreateDirectory(configurationDirectory);
        this.backupPath = Path.Combine(configurationDirectory, BackupFileName);
        this.fileLog = fileLog;
    }

    public bool RestoreOrMirror(Configuration configuration)
    {
        try
        {
            if (configuration.SavedRecipePlans.Count == 0 &&
                File.Exists(this.backupPath))
            {
                var backup = JsonSerializer.Deserialize<SavedPlanBackup>(
                    File.ReadAllText(this.backupPath),
                    this.serializerOptions);
                if (backup?.Plans is { Count: > 0 })
                {
                    configuration.SavedRecipePlans = backup.Plans;
                    this.fileLog.Info(
                        "SavedPlans",
                        $"Restored {backup.Plans.Count} saved plan(s) from the persistent backup.");
                    return true;
                }
            }

            this.Save(configuration.SavedRecipePlans);
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not restore its saved-plan backup.");
            this.fileLog.Error("SavedPlans", "Could not restore the saved-plan backup.", exception);
        }

        return false;
    }

    public void Save(IReadOnlyList<SavedRecipePlan> plans)
    {
        try
        {
            var temporaryPath = this.backupPath + ".tmp";
            var json = JsonSerializer.Serialize(
                new SavedPlanBackup
                {
                    Plans = [.. plans],
                },
                this.serializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, this.backupPath, true);
            this.fileLog.Info("SavedPlans", $"Backed up {plans.Count} saved plan(s).");
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Recipe Helper could not back up its saved plans.");
            this.fileLog.Error("SavedPlans", "Could not back up saved plans.", exception);
        }
    }

    private sealed class SavedPlanBackup
    {
        public int Version { get; set; } = 1;

        public List<SavedRecipePlan> Plans { get; set; } = [];
    }
}
