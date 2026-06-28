using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action save;

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
        changed |= DrawColor("Sufficient row", ref this.configuration.EnoughRowColor);
        changed |= DrawColor("Success text", ref this.configuration.SuccessTextColor);
        changed |= DrawColor("Missing/error text", ref this.configuration.MissingTextColor);
        changed |= DrawColor("Warning text", ref this.configuration.WarningTextColor);
        changed |= DrawColor("Ready to craft button", ref this.configuration.ReadyButtonColor);

        ImGui.Spacing();
        ImGui.TextColored(this.configuration.AccentColor, "Missing Items Overlay");
        changed |= ImGui.Checkbox(
            "Use transparent overlay background",
            ref this.configuration.UseTransparentOverlayBackground);
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

        ImGui.Spacing();
        ImGui.TextColored(this.configuration.AccentColor, "Craft Time Estimate");
        ImGui.TextDisabled("Adjust this to match the average duration of your Artisan crafts.");
        ImGui.SetNextItemWidth(180);
        changed |= ImGui.SliderInt(
            "Seconds per craft",
            ref this.configuration.EstimatedSecondsPerCraft,
            5,
            120);

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
}
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class SettingsWindow : Window
{
    private readonly Configuration configuration;
    private readonly Action save;

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
        changed |= DrawColor("Sufficient row", ref this.configuration.EnoughRowColor);
        changed |= DrawColor("Success text", ref this.configuration.SuccessTextColor);
        changed |= DrawColor("Missing/error text", ref this.configuration.MissingTextColor);
        changed |= DrawColor("Warning text", ref this.configuration.WarningTextColor);
        changed |= DrawColor("Ready to craft button", ref this.configuration.ReadyButtonColor);

        ImGui.Spacing();
        ImGui.TextColored(this.configuration.AccentColor, "Missing Items Overlay");
        changed |= ImGui.Checkbox(
            "Use transparent overlay background",
            ref this.configuration.UseTransparentOverlayBackground);
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
}
