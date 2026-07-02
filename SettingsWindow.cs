using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action save;
    private string presetName = string.Empty;
    private int selectedPresetIndex = -1;

    public SettingsWindow(Configuration configuration, Action save)
        : base("Recipe Helper Settings###DalamudRecipeHelperSettings")
    {
        this.configuration = configuration;
        this.save = save;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 260),
            MaximumSize = new Vector2(650, 600),
        };
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public override void Draw()
    {
        ImGui.TextColored(this.configuration.AccentColor, "Appearance");
        ImGui.TextDisabled("Personalise Recipe Helper's status and interface colours.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var changed = false;
        changed |= DrawColor("Title bar", ref this.configuration.TitleBarColor);
        changed |= DrawColor("Window background", ref this.configuration.WindowBackgroundColor);
        changed |= DrawColor("Main text", ref this.configuration.TextColor);
        changed |= DrawColor("Interface accent", ref this.configuration.AccentColor);
        changed |= DrawColor("Button colour", ref this.configuration.ButtonColor);
        changed |= DrawColor("Sufficient row", ref this.configuration.EnoughRowColor);
        changed |= DrawColor("Success text", ref this.configuration.SuccessTextColor);
        changed |= DrawColor("Missing/error text", ref this.configuration.MissingTextColor);
        changed |= DrawColor("Warning text", ref this.configuration.WarningTextColor);
        changed |= DrawColor("Ready to craft button", ref this.configuration.ReadyButtonColor);
        changed |= DrawColor("Editable card background", ref this.configuration.InputCardColor);
        changed |= ImGui.Checkbox(
            "Use interface accent for folder headers",
            ref this.configuration.UseAccentForFolderHeaders);
        changed |= DrawColor("Folder header", ref this.configuration.FolderHeaderColor);
        changed |= DrawColor("Folder header text", ref this.configuration.FolderHeaderTextColor);

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
            if (ImGui.Combo(
                    "Saved presets",
                    ref this.selectedPresetIndex,
                    names,
                    names.Length))
            {
            }

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

        if (ImGui.Button("Reset colours"))
        {
            this.configuration.ResetColors();
            this.save();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Changes are saved automatically.");
    }

    private static bool DrawColor(string label, ref Vector4 color)
    {
        ImGui.SetNextItemWidth(180);
        return ImGui.ColorEdit4(
            label,
            ref color,
            ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf);
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
