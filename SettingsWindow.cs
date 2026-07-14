using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace DalamudRecipeHelper;

public sealed class SettingsWindow : Window
{
    private static readonly int[] MainWindowScaleOptions = [60, 70, 80, 90, 100];
    private static readonly int[] TextScaleOptions = [80, 90, 100, 110, 120, 130, 140, 150];
    private static readonly IReadOnlyList<ReleaseNote> ReleaseNotes = LoadReleaseNotes();

    private readonly Configuration configuration;
    private readonly Action save;
    private readonly AppearanceSettingsWindow appearanceSettingsWindow;
    private readonly DebugWindow debugWindow;
    private bool showingChangelog;

    public SettingsWindow(
        Configuration configuration,
        Action save,
        AppearanceSettingsWindow appearanceSettingsWindow,
        DebugWindow debugWindow)
        : base("Recipe Helper Settings###DalamudRecipeHelperSettings")
    {
        this.configuration = configuration;
        this.save = save;
        this.appearanceSettingsWindow = appearanceSettingsWindow;
        this.debugWindow = debugWindow;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 280),
            MaximumSize = new Vector2(650, 600),
        };
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public override void Draw()
    {
        WindowTheme.ApplyTextScale(this.configuration);
        if (this.showingChangelog)
        {
            this.DrawChangelog();
            return;
        }

        ImGui.TextColored(this.configuration.AccentTextColor, "Settings");
        ImGui.TextDisabled("Open the dedicated windows below for colour customisation and support debugging.");
        ImGui.Spacing();
        var actionScale = WindowTheme.GetTextScale(this.configuration);
        var buttonPadding = 26f * actionScale;
        var customisationButtonWidth = Math.Max(140f * actionScale, ImGui.CalcTextSize("Open customisation").X + buttonPadding);
        var debugButtonWidth = Math.Max(110f * actionScale, ImGui.CalcTextSize("Open debug").X + buttonPadding);
        var changelogButtonWidth = Math.Max(110f * actionScale, ImGui.CalcTextSize("Changelog").X + buttonPadding);
        WindowTheme.PushButtonStyle(this.configuration, actionScale);
        if (WindowTheme.ShadowedButton("Open customisation", new Vector2(customisationButtonWidth, 0)))
            this.appearanceSettingsWindow.IsOpen = true;

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Open debug", new Vector2(debugButtonWidth, 0)))
            this.debugWindow.OpenWithFreshReport();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Changelog", new Vector2(changelogButtonWidth, 0)))
            this.showingChangelog = true;
        WindowTheme.PopButtonStyle();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(this.configuration.AccentTextColor, "Recommended Plugins");
        ImGui.TextDisabled("Recipe Helper works best when the following plugins are enabled.");
        DrawPluginStatus(
            this.configuration,
            "Artisan",
            "https://github.com/PunishXIV/Artisan",
            IsPluginDetected("Artisan"),
            "Enables Craft All recipe queues.");
        DrawPluginStatus(
            this.configuration,
            "GatherBuddy",
            "https://github.com/Ottermandias/GatherBuddy",
            IsPluginDetected("GatherBuddy"),
            "Supports collectables, gathering, fish details, and availability.");
        DrawPluginStatus(
            this.configuration,
            "Lifestream",
            "https://github.com/NightmareXIV/Lifestream",
            IsPluginDetected("Lifestream"),
            "Lets GatherBuddy teleport to gathering and fishing destinations.");
        DrawPluginStatus(
            this.configuration,
            "Auto-Retainer",
            "https://github.com/PunishXIV/AutoRetainer",
            IsPluginDetected("AutoRetainer"),
            "Optional. Manual withdrawal is available without it.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        WindowTheme.PushInputCardStyle(this.configuration);
        ImGui.TextColored(this.configuration.AccentTextColor, "Interface Scaling");
        var changed = false;
        changed |= DrawPercentCombo(
            "Interface scale",
            "##interface-scale",
            MainWindowScaleOptions,
            ref this.configuration.MainWindowScalePercent);
        ImGui.TextDisabled("Scales the main Recipe Helper window and its interface. Default is 100%.");
        changed |= DrawPercentCombo(
            "Text size",
            "##text-size",
            TextScaleOptions,
            ref this.configuration.TextScalePercent);
        ImGui.TextDisabled("Applies to Recipe Helper windows, popups, and tooltips.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(this.configuration.AccentTextColor, "Missing Items Overlay");
        changed |= ImGui.Checkbox(
            "Use transparent overlay background",
            ref this.configuration.UseTransparentOverlayBackground);
        changed |= ImGui.Checkbox(
            "Show vendored items in overlay",
            ref this.configuration.ShowVendoredItemsInOverlay);
        if (this.configuration.UseTransparentOverlayBackground)
        {
            ImGui.SetNextItemWidth(180);
            changed |= ImGui.SliderFloat(
                "Overlay opacity",
                ref this.configuration.OverlayBackgroundOpacity,
                0.20f,
                1f,
                "%.2f");
        }
        WindowTheme.PopInputCardStyle();

        if (changed)
            this.save();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Changes are saved automatically.");
        ImGui.TextDisabled("Use Debug if someone needs a support report for stuck retainer flows.");
    }

    private void DrawChangelog()
    {
        var actionScale = WindowTheme.GetTextScale(this.configuration);
        var buttonPadding = 26f * actionScale;
        var backButtonWidth = Math.Max(80f * actionScale, ImGui.CalcTextSize("Back").X + buttonPadding);

        ImGui.TextColored(this.configuration.AccentTextColor, "Changelog");
        ImGui.TextDisabled("Complete Recipe Helper release history.");
        ImGui.SameLine();
        WindowTheme.PushButtonStyle(this.configuration, actionScale);
        if (WindowTheme.ShadowedButton("Back", new Vector2(backButtonWidth, 0)))
            this.showingChangelog = false;
        WindowTheme.PopButtonStyle();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (!ImGui.BeginChild("changelog-history", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var release in ReleaseNotes)
        {
            ImGui.TextColored(this.configuration.AccentTextColor, release.Version);
            foreach (var line in release.Lines)
            {
                if (line.IsHeading)
                    ImGui.TextColored(Vector4.Lerp(this.configuration.AccentTextColor, this.configuration.TextColor, 0.35f), line.Text);
                else
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    ImGui.TextWrapped(line.Text);
                }
            }

            ImGui.Spacing();
        }

        ImGui.EndChild();
    }

    private static IReadOnlyList<ReleaseNote> LoadReleaseNotes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(".RELEASE_NOTES_v", StringComparison.Ordinal))
            .Select(name => ReadReleaseNote(assembly, name))
            .Where(release => release is not null)
            .Select(release => release!)
            .OrderByDescending(release => Version.Parse(release.Version[1..]))
            .ToArray();
    }

    private static ReleaseNote? ReadReleaseNote(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);
        var lines = new List<ChangelogLine>();
        string? version = null;
        var includeSection = true;

        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# Recipe Helper v", StringComparison.Ordinal))
            {
                version = line[16..];
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                includeSection = !string.Equals(line, "## Notes For Publish", StringComparison.Ordinal);
                if (includeSection)
                    lines.Add(new ChangelogLine(line[3..], true));
                continue;
            }

            if (includeSection && line.StartsWith("- ", StringComparison.Ordinal))
                lines.Add(new ChangelogLine(line[2..], false));
        }

        return version is null ? null : new ReleaseNote(version, lines);
    }

    private sealed record ReleaseNote(string Version, IReadOnlyList<ChangelogLine> Lines);

    private sealed record ChangelogLine(string Text, bool IsHeading);

    private static bool DrawPercentCombo(
        string label,
        string id,
        IReadOnlyList<int> options,
        ref int currentValue)
    {
        var normalized = currentValue;
        if (!options.Contains(normalized))
            normalized = options.OrderBy(option => Math.Abs(option - normalized)).First();

        var selectedIndex = Array.IndexOf(options.ToArray(), normalized);
        var labels = options.Select(option => $"{option}%").ToArray();
        if (!ImGui.Combo(label + id, ref selectedIndex, labels, labels.Length))
            return false;

        currentValue = options[selectedIndex];
        return true;
    }

    private static void DrawPluginStatus(
        Configuration configuration,
        string pluginName,
        string githubUrl,
        bool isDetected,
        string description)
    {
        ImGui.Spacing();
        ImGui.TextColored(Vector4.Lerp(configuration.AccentTextColor, configuration.TextColor, 0.35f), pluginName);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open GitHub project page");
            if (ImGui.IsItemClicked())
                Util.OpenLink(githubUrl);
        }
        ImGui.SameLine();
        ImGui.TextColored(
            isDetected ? configuration.SuccessTextColor : configuration.MissingTextColor,
            isDetected ? "Detected" : "Not detected");
        ImGui.TextDisabled(description);
    }

    private static bool IsPluginDetected(string assemblyName) =>
        AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(assembly => string.Equals(
                assembly.GetName().Name,
                assemblyName,
                StringComparison.OrdinalIgnoreCase));
}
