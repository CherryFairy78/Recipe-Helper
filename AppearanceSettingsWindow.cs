using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class AppearanceSettingsWindow : Window
{
    private const ImGuiColorEditFlags ColorEditFlags =
        ImGuiColorEditFlags.AlphaBar |
        ImGuiColorEditFlags.AlphaPreviewHalf |
        ImGuiColorEditFlags.DisplayHex;
    private const float ColorLabelWidth = 170f;

    private readonly Configuration configuration;
    private readonly Action save;
    private static readonly ThemePreset[] BuiltInPresets =
    [
        CreateBuiltInPreset(
            "Blue",
            new Vector4(0.26f, 0.58f, 0.88f, 1f),
            new Vector4(0.26f, 0.58f, 0.88f, 1f),
            new Vector4(0.26f, 0.58f, 0.88f, 1f),
            new Vector4(0.10f, 0.24f, 0.38f, 1f),
            new Vector4(0.42f, 0.52f, 0.74f, 0.92f),
            new Vector4(0.34f, 0.42f, 0.60f, 0.88f)),
        CreateBuiltInPreset(
            "Pink",
            new Vector4(0.86f, 0.34f, 0.58f, 1f),
            new Vector4(0.66f, 0.16f, 0.36f, 1f),
            new Vector4(0.79f, 0.27f, 0.50f, 1f),
            new Vector4(0.95f, 0.42f, 0.64f, 1f),
            new Vector4(0.92f, 0.53f, 0.72f, 0.92f),
            new Vector4(0.96f, 0.64f, 0.80f, 0.88f)),
        CreateBuiltInPreset(
            "Purple",
            new Vector4(0.55f, 0.42f, 0.88f, 1f),
            new Vector4(0.46f, 0.32f, 0.80f, 1f),
            new Vector4(0.49f, 0.36f, 0.82f, 1f),
            new Vector4(0.22f, 0.14f, 0.36f, 1f),
            new Vector4(0.54f, 0.45f, 0.78f, 0.92f),
            new Vector4(0.43f, 0.36f, 0.66f, 0.88f)),
        CreateBuiltInPreset(
            "Green",
            new Vector4(0.23f, 0.68f, 0.46f, 1f),
            new Vector4(0.18f, 0.56f, 0.37f, 1f),
            new Vector4(0.21f, 0.62f, 0.42f, 1f),
            new Vector4(0.09f, 0.28f, 0.20f, 1f),
            new Vector4(0.37f, 0.64f, 0.50f, 0.92f),
            new Vector4(0.29f, 0.51f, 0.40f, 0.88f)),
        CreateBuiltInPreset(
            "Orange",
            new Vector4(0.92f, 0.52f, 0.22f, 1f),
            new Vector4(0.79f, 0.42f, 0.13f, 1f),
            new Vector4(0.86f, 0.46f, 0.17f, 1f),
            new Vector4(0.38f, 0.20f, 0.08f, 1f),
            new Vector4(0.76f, 0.49f, 0.23f, 0.92f),
            new Vector4(0.63f, 0.39f, 0.18f, 0.88f)),
        CreateBuiltInPreset(
            "Red",
            new Vector4(0.86f, 0.26f, 0.28f, 1f),
            new Vector4(0.78f, 0.18f, 0.20f, 1f),
            new Vector4(0.80f, 0.21f, 0.24f, 1f),
            new Vector4(0.33f, 0.10f, 0.12f, 1f),
            new Vector4(0.67f, 0.28f, 0.31f, 0.92f),
            new Vector4(0.55f, 0.22f, 0.25f, 0.88f)),
        CreateBuiltInPreset(
            "Yellow",
            new Vector4(0.93f, 0.76f, 0.20f, 1f),
            new Vector4(0.64f, 0.50f, 0.09f, 1f),
            new Vector4(0.84f, 0.68f, 0.17f, 1f),
            new Vector4(0.36f, 0.29f, 0.07f, 1f),
            new Vector4(0.74f, 0.60f, 0.21f, 0.92f),
            new Vector4(0.61f, 0.49f, 0.17f, 0.88f)),
        CreateBuiltInPreset(
            "Grey",
            new Vector4(0.58f, 0.62f, 0.68f, 1f),
            new Vector4(0.49f, 0.53f, 0.58f, 1f),
            new Vector4(0.54f, 0.58f, 0.64f, 1f),
            new Vector4(0.20f, 0.23f, 0.28f, 1f),
            new Vector4(0.45f, 0.49f, 0.55f, 0.92f),
            new Vector4(0.36f, 0.40f, 0.46f, 0.88f)),
    ];
    private string presetName = string.Empty;
    private int selectedSavedPresetIndex = -1;

    public AppearanceSettingsWindow(Configuration configuration, Action save)
        : base("Recipe Helper Customisation###DalamudRecipeHelperAppearanceSettings")
    {
        this.configuration = configuration;
        this.save = save;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(430, 360),
            MaximumSize = new Vector2(760, 860),
        };
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public override void Draw()
    {
        WindowTheme.ApplyTextScale(this.configuration);
        ImGui.TextColored(this.configuration.AccentTextColor, "Appearance");
        ImGui.TextDisabled("Use the colour controls below to customise the interface.");
        ImGui.Spacing();

        WindowTheme.PushInputCardStyle(this.configuration);
        var changed = false;
        changed |= this.DrawColor("Title bar", () => this.configuration.TitleBarColor, value => this.configuration.TitleBarColor = value);
        changed |= this.DrawColor("Window background", () => this.configuration.WindowBackgroundColor, value => this.configuration.WindowBackgroundColor = value);
        changed |= this.DrawColor("Main text", () => this.configuration.TextColor, value => this.configuration.TextColor = value);
        changed |= this.DrawColor("Interface accent", () => this.configuration.AccentColor, value => this.configuration.AccentColor = value);
        changed |= this.DrawColor("Interface accent text", () => this.configuration.AccentTextColor, value => this.configuration.AccentTextColor = value);
        changed |= this.DrawColor("Button colour", () => this.configuration.ButtonColor, value => this.configuration.ButtonColor = value);
        changed |= this.DrawColor("Button text", () => this.configuration.ButtonTextColor, value => this.configuration.ButtonTextColor = value);
        changed |= this.DrawColor("Sufficient row", () => this.configuration.EnoughRowColor, value => this.configuration.EnoughRowColor = value);
        changed |= this.DrawColor("Success text", () => this.configuration.SuccessTextColor, value => this.configuration.SuccessTextColor = value);
        changed |= this.DrawColor("Missing/error text", () => this.configuration.MissingTextColor, value => this.configuration.MissingTextColor = value);
        changed |= this.DrawColor("Warning text", () => this.configuration.WarningTextColor, value => this.configuration.WarningTextColor = value);
        changed |= this.DrawColor("Editable card background", () => this.configuration.InputCardColor, value => this.configuration.InputCardColor = value);
        changed |= ImGui.Checkbox(
            "Use interface accent for folder headers",
            ref this.configuration.UseAccentForFolderHeaders);
        changed |= this.DrawColor("Folder header", () => this.configuration.FolderHeaderColor, value => this.configuration.FolderHeaderColor = value);
        changed |= this.DrawColor("Section header text", () => this.configuration.SectionHeaderTextColor, value => this.configuration.SectionHeaderTextColor = value);
        changed |= this.DrawColor("Folder header text", () => this.configuration.FolderHeaderTextColor, value => this.configuration.FolderHeaderTextColor = value);
        changed |= this.DrawColor("Subfolder header", () => this.configuration.SubfolderHeaderColor, value => this.configuration.SubfolderHeaderColor = value);
        changed |= this.DrawColor("Subfolder header text", () => this.configuration.SubfolderHeaderTextColor, value => this.configuration.SubfolderHeaderTextColor = value);
        changed |= this.DrawColor("Saved plan text", () => this.configuration.SavedPlanTextColor, value => this.configuration.SavedPlanTextColor = value);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(this.configuration.AccentTextColor, "Theme Presets");
        ImGui.TextDisabled("Load a built-in preset or save the current colours locally.");
        if (this.DrawBuiltInPresetButtons())
            changed = true;

        if (this.configuration.ThemePresets.Count > 0)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(MathF.Max(220f, ImGui.GetContentRegionAvail().X - (114f * WindowTheme.GetTextScale(this.configuration))));
            var preview = this.GetSelectedSavedPresetName();
            if (ImGui.BeginCombo("##saved-theme-presets", preview))
            {
                for (var i = 0; i < this.configuration.ThemePresets.Count; i++)
                {
                    var isSelected = i == this.selectedSavedPresetIndex;
                    if (ImGui.Selectable(this.configuration.ThemePresets[i].Name, isSelected))
                        this.selectedSavedPresetIndex = i;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Load preset"))
            {
                if (this.selectedSavedPresetIndex >= 0 &&
                    this.selectedSavedPresetIndex < this.configuration.ThemePresets.Count)
                {
                    this.ApplyPreset(this.configuration.ThemePresets[this.selectedSavedPresetIndex]);
                    changed = true;
                }
            }
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(MathF.Max(220f, ImGui.GetContentRegionAvail().X - (140f * WindowTheme.GetTextScale(this.configuration))));
        ImGui.InputTextWithHint("##preset-name", "Preset name", ref this.presetName, 100);
        ImGui.SameLine();

        WindowTheme.PushButtonStyle(this.configuration, WindowTheme.GetTextScale(this.configuration));
        if (WindowTheme.ShadowedButton("Save preset"))
        {
            this.SavePreset();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (WindowTheme.ShadowedButton("Reset colours"))
        {
            this.configuration.ResetColors();
            this.save();
        }
        WindowTheme.PopButtonStyle();

        ImGui.SameLine();
        ImGui.TextDisabled("Changes are saved automatically.");

        WindowTheme.PopInputCardStyle();

        if (changed)
            this.save();
    }

    private bool DrawColor(string label, Func<Vector4> getter, Action<Vector4> setter)
    {
        var changed = false;
        var color = getter();

        ImGui.PushID(label);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(ColorLabelWidth);
        var editorWidth = Math.Min(
            ImGui.GetContentRegionAvail().X,
            420f * WindowTheme.GetTextScale(this.configuration));
        ImGui.SetNextItemWidth(Math.Max(260f, editorWidth));
        changed |= ImGui.ColorEdit4("##value", ref color, ColorEditFlags);
        if (changed)
            setter(color);

        ImGui.PopID();
        return changed;
    }

    private void SavePreset()
    {
        var name = this.presetName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var preset = CreatePresetFromConfiguration(this.configuration, name);

        var existingIndex = this.configuration.ThemePresets.FindIndex(
            existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            this.configuration.ThemePresets[existingIndex] = preset;
            this.selectedSavedPresetIndex = existingIndex;
        }
        else
        {
            this.configuration.ThemePresets.Add(preset);
            this.selectedSavedPresetIndex = this.configuration.ThemePresets.Count - 1;
        }

        this.save();
    }

    private bool DrawBuiltInPresetButtons()
    {
        var changed = false;
        for (var i = 0; i < BuiltInPresets.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine();

            if (WindowTheme.ShadowedButton(BuiltInPresets[i].Name))
            {
                this.ApplyPreset(BuiltInPresets[i]);
                changed = true;
            }
        }

        return changed;
    }

    private string GetSelectedSavedPresetName() =>
        this.selectedSavedPresetIndex >= 0 &&
        this.selectedSavedPresetIndex < this.configuration.ThemePresets.Count
            ? this.configuration.ThemePresets[this.selectedSavedPresetIndex].Name
            : "Saved presets";

    private void ApplyPreset(ThemePreset preset)
    {
        this.configuration.EnoughRowColor = preset.EnoughRowColor;
        this.configuration.SuccessTextColor = preset.SuccessTextColor;
        this.configuration.MissingTextColor = preset.MissingTextColor;
        this.configuration.WarningTextColor = preset.WarningTextColor;
        this.configuration.AccentColor = preset.AccentColor;
        this.configuration.AccentTextColor = preset.AccentTextColor;
        this.configuration.ButtonColor = preset.ButtonColor;
        this.configuration.ButtonTextColor = preset.ButtonTextColor;
        this.configuration.TitleBarColor = preset.TitleBarColor;
        this.configuration.WindowBackgroundColor = preset.WindowBackgroundColor;
        this.configuration.TextColor = preset.TextColor;
        this.configuration.InputCardColor = preset.InputCardColor;
        this.configuration.FolderHeaderColor = preset.FolderHeaderColor;
        this.configuration.SectionHeaderTextColor = preset.SectionHeaderTextColor;
        this.configuration.FolderHeaderTextColor = preset.FolderHeaderTextColor;
        this.configuration.SubfolderHeaderColor = preset.SubfolderHeaderColor;
        this.configuration.SubfolderHeaderTextColor = preset.SubfolderHeaderTextColor;
        this.configuration.SavedPlanTextColor = preset.SavedPlanTextColor;
        this.save();
    }

    private static ThemePreset CreatePresetFromConfiguration(Configuration configuration, string name) =>
        new()
        {
            Name = name,
            EnoughRowColor = configuration.EnoughRowColor,
            SuccessTextColor = configuration.SuccessTextColor,
            MissingTextColor = configuration.MissingTextColor,
            WarningTextColor = configuration.WarningTextColor,
            AccentColor = configuration.AccentColor,
            AccentTextColor = configuration.AccentTextColor,
            ButtonColor = configuration.ButtonColor,
            ButtonTextColor = configuration.ButtonTextColor,
            TitleBarColor = configuration.TitleBarColor,
            WindowBackgroundColor = configuration.WindowBackgroundColor,
            TextColor = configuration.TextColor,
            InputCardColor = configuration.InputCardColor,
            FolderHeaderColor = configuration.FolderHeaderColor,
            SectionHeaderTextColor = configuration.SectionHeaderTextColor,
            FolderHeaderTextColor = configuration.FolderHeaderTextColor,
            SubfolderHeaderColor = configuration.SubfolderHeaderColor,
            SubfolderHeaderTextColor = configuration.SubfolderHeaderTextColor,
            SavedPlanTextColor = configuration.SavedPlanTextColor,
        };

    private static ThemePreset CreateBuiltInPreset(
        string name,
        Vector4 accentColor,
        Vector4 accentTextColor,
        Vector4 buttonColor,
        Vector4 titleBarColor,
        Vector4 folderHeaderColor,
        Vector4 subfolderHeaderColor) =>
        new()
        {
            Name = name,
            EnoughRowColor = new Vector4(0.15f, 0.45f, 0.2f, 0.35f),
            SuccessTextColor = new Vector4(0.45f, 0.9f, 0.55f, 1f),
            MissingTextColor = new Vector4(1f, 0.55f, 0.45f, 1f),
            WarningTextColor = new Vector4(1f, 0.65f, 0.35f, 1f),
            AccentColor = accentColor,
            AccentTextColor = accentTextColor,
            ButtonColor = buttonColor,
            ButtonTextColor = new Vector4(0.97f, 0.98f, 0.99f, 1f),
            TitleBarColor = titleBarColor,
            WindowBackgroundColor = new Vector4(0.055f, 0.065f, 0.08f, 0.98f),
            TextColor = new Vector4(0.94f, 0.95f, 0.97f, 1f),
            InputCardColor = new Vector4(0.22f, 0.28f, 0.36f, 0.88f),
            FolderHeaderColor = folderHeaderColor,
            SectionHeaderTextColor = new Vector4(0.98f, 0.98f, 0.99f, 1f),
            FolderHeaderTextColor = new Vector4(0.98f, 0.98f, 0.99f, 1f),
            SubfolderHeaderColor = subfolderHeaderColor,
            SubfolderHeaderTextColor = new Vector4(0.95f, 0.96f, 0.98f, 1f),
            SavedPlanTextColor = new Vector4(0.94f, 0.95f, 0.97f, 1f),
        };
}
