using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public static class WindowTheme
{
    private const int PushedColorCount = 12;

    public static void Push(Configuration configuration)
    {
        var title = configuration.TitleBarColor;
        var background = configuration.WindowBackgroundColor;
        var text = configuration.TextColor;
        var accent = configuration.AccentColor;

        ImGui.PushStyleColor(ImGuiCol.Text, text);
        ImGui.PushStyleColor(
            ImGuiCol.TextDisabled,
            new Vector4(text.X, text.Y, text.Z, Math.Min(text.W, 0.62f)));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, background);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Adjust(background, 0.015f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Adjust(background, 0.025f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Adjust(title, -0.06f, title.W));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, title);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Adjust(title, -0.10f, 0.82f));
        ImGui.PushStyleColor(
            ImGuiCol.TableHeaderBg,
            new Vector4(accent.X, accent.Y, accent.Z, 0.22f));
        ImGui.PushStyleColor(
            ImGuiCol.Header,
            new Vector4(accent.X, accent.Y, accent.Z, 0.30f));
        ImGui.PushStyleColor(
            ImGuiCol.HeaderHovered,
            new Vector4(accent.X, accent.Y, accent.Z, 0.48f));
        ImGui.PushStyleColor(
            ImGuiCol.HeaderActive,
            new Vector4(accent.X, accent.Y, accent.Z, 0.62f));
    }

    public static void Pop() => ImGui.PopStyleColor(PushedColorCount);

    private static Vector4 Adjust(Vector4 color, float amount, float alpha) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            alpha);
}
