using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class AppearanceSettingsWindow : Window
{
    private const ImGuiColorEditFlags ColorEditFlags =
        ImGuiColorEditFlags.AlphaBar |
        ImGuiColorEditFlags.AlphaPreviewHalf |
        ImGuiColorEditFlags.DisplayRgb |
        ImGuiColorEditFlags.InputRgb;
    private const float ColorLabelWidth = 170f;

    private readonly Configuration configuration;
    private readonly Action save;
    private string presetName = string.Empty;
    private int selectedPresetIndex = -1;

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
        ImGui.TextColored(this.configuration.AccentColor, "Appearance");
        ImGui.TextDisabled("Use the colour controls below to customise the interface.");
        ImGui.Spacing();

        var changed = false;
        changed |= this.DrawColor("Title bar", () => this.configuration.TitleBarColor, value => this.configuration.TitleBarColor = value);
        changed |= this.DrawColor("Window background", () => this.configuration.WindowBackgroundColor, value => this.configuration.WindowBackgroundColor = value);
        changed |= this.DrawColor("Main text", () => this.configuration.TextColor, value => this.configuration.TextColor = value);
        changed |= this.DrawColor("Interface accent", () => this.configuration.AccentColor, value => this.configuration.AccentColor = value);
        changed |= this.DrawColor("Button colour", () => this.configuration.ButtonColor, value => this.configuration.ButtonColor = value);
        changed |= this.DrawColor("Sufficient row", () => this.configuration.EnoughRowColor, value => this.configuration.EnoughRowColor = value);
        changed |= this.DrawColor("Success text", () => this.configuration.SuccessTextColor, value => this.configuration.SuccessTextColor = value);
        changed |= this.DrawColor("Missing/error text", () => this.configuration.MissingTextColor, value => this.configuration.MissingTextColor = value);
        changed |= this.DrawColor("Warning text", () => this.configuration.WarningTextColor, value => this.configuration.WarningTextColor = value);
        changed |= this.DrawColor("Ready to craft button", () => this.configuration.ReadyButtonColor, value => this.configuration.ReadyButtonColor = value);
        changed |= this.DrawColor("Editable card background", () => this.configuration.InputCardColor, value => this.configuration.InputCardColor = value);
        changed |= ImGui.Checkbox(
            "Use interface accent for folder headers",
            ref this.configuration.UseAccentForFolderHeaders);
        changed |= this.DrawColor("Folder header", () => this.configuration.FolderHeaderColor, value => this.configuration.FolderHeaderColor = value);
        changed |= this.DrawColor("Folder header text", () => this.configuration.FolderHeaderTextColor, value => this.configuration.FolderHeaderTextColor = value);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(this.configuration.AccentColor, "Theme Presets");
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##theme-preset-name", "Preset name", ref this.presetName, 80);
        ImGui.SameLine();
        if (ImGui.Button("Save preset"))
        {
            this.SavePreset();
            changed = true;
        }

        if (this.configuration.ThemePresets.Count > 0)
        {
            var names = this.configuration.ThemePresets.Select(preset => preset.Name).ToArray();
            this.selectedPresetIndex = Math.Clamp(this.selectedPresetIndex, 0, names.Length - 1);
            ImGui.SetNextItemWidth(220);
            ImGui.Combo(
                "Saved presets",
                ref this.selectedPresetIndex,
                names,
                names.Length);

            if (this.selectedPresetIndex >= 0 && this.selectedPresetIndex < this.configuration.ThemePresets.Count)
            {
                if (ImGui.Button("Load preset"))
                {
                    this.ApplyPreset(this.configuration.ThemePresets[this.selectedPresetIndex]);
                    changed = true;
                }

                ImGui.SameLine();
                if (ImGui.Button("Delete preset"))
                {
                    this.configuration.ThemePresets.RemoveAt(this.selectedPresetIndex);
                    this.selectedPresetIndex = Math.Min(
                        this.selectedPresetIndex,
                        this.configuration.ThemePresets.Count - 1);
                    changed = true;
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Reset colours"))
        {
            this.configuration.ResetColors();
            this.save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Changes are saved automatically.");

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
        ImGui.SetNextItemWidth(-1);
        changed |= ImGui.ColorEdit4("##value", ref color, ColorEditFlags);
        if (changed)
            setter(color);

        ImGui.PopID();
        return changed;
    }

    private void SavePreset()
    {
        var name = this.presetName.Trim();
        if (name.Length == 0)
            return;

        var preset = new ThemePreset
        {
            Name = name,
            EnoughRowColor = this.configuration.EnoughRowColor,
            SuccessTextColor = this.configuration.SuccessTextColor,
            MissingTextColor = this.configuration.MissingTextColor,
            WarningTextColor = this.configuration.WarningTextColor,
            ReadyButtonColor = this.configuration.ReadyButtonColor,
            AccentColor = this.configuration.AccentColor,
            ButtonColor = this.configuration.ButtonColor,
            TitleBarColor = this.configuration.TitleBarColor,
            WindowBackgroundColor = this.configuration.WindowBackgroundColor,
            TextColor = this.configuration.TextColor,
            InputCardColor = this.configuration.InputCardColor,
            FolderHeaderColor = this.configuration.FolderHeaderColor,
            FolderHeaderTextColor = this.configuration.FolderHeaderTextColor,
        };

        var existingIndex = this.configuration.ThemePresets.FindIndex(existing =>
            string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            this.configuration.ThemePresets[existingIndex] = preset;
        else
            this.configuration.ThemePresets.Add(preset);
    }

    private void ApplyPreset(ThemePreset preset)
    {
        this.configuration.EnoughRowColor = preset.EnoughRowColor;
        this.configuration.SuccessTextColor = preset.SuccessTextColor;
        this.configuration.MissingTextColor = preset.MissingTextColor;
        this.configuration.WarningTextColor = preset.WarningTextColor;
        this.configuration.ReadyButtonColor = preset.ReadyButtonColor;
        this.configuration.AccentColor = preset.AccentColor;
        this.configuration.ButtonColor = preset.ButtonColor;
        this.configuration.TitleBarColor = preset.TitleBarColor;
        this.configuration.WindowBackgroundColor = preset.WindowBackgroundColor;
        this.configuration.TextColor = preset.TextColor;
        this.configuration.InputCardColor = preset.InputCardColor;
        this.configuration.FolderHeaderColor = preset.FolderHeaderColor;
        this.configuration.FolderHeaderTextColor = preset.FolderHeaderTextColor;
    }

}
