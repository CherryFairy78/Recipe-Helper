using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class SettingsWindow : Window
{
    private static readonly int[] MainWindowScaleOptions = [60, 70, 80, 90, 100];
    private static readonly int[] TextScaleOptions = [80, 90, 100, 110, 120, 130, 140, 150];

    private readonly Configuration configuration;
    private readonly Action save;
    private readonly AppearanceSettingsWindow appearanceSettingsWindow;
    private readonly DebugWindow debugWindow;

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
        ImGui.TextColored(this.configuration.AccentColor, "Settings");
        ImGui.TextDisabled("Open the dedicated windows below for colour customisation and support debugging.");
        ImGui.Spacing();
        if (ImGui.Button("Open customisation"))
            this.appearanceSettingsWindow.IsOpen = true;

        ImGui.SameLine();
        if (ImGui.Button("Open debug"))
            this.debugWindow.OpenWithFreshReport();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(this.configuration.AccentColor, "Interface Scaling");
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

        ImGui.TextColored(this.configuration.AccentColor, "Missing Items Overlay");
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

        if (changed)
            this.save();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled("Changes are saved automatically.");
        ImGui.TextDisabled("Use Debug if someone needs a support report for stuck retainer flows.");
    }

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
}
