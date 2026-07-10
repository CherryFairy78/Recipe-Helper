using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public static class WindowTheme
{
    private const int PushedColorCount = 13;
    private const float ButtonHoverAmount = 0.18f;
    private const float ButtonActiveAmount = -0.04f;

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
            Adjust(accent, -0.06f, 0.90f));
        ImGui.PushStyleColor(
            ImGuiCol.Header,
            Adjust(accent, -0.04f, 0.82f));
        ImGui.PushStyleColor(
            ImGuiCol.HeaderHovered,
            Adjust(accent, 0.04f, 0.94f));
        ImGui.PushStyleColor(
            ImGuiCol.HeaderActive,
            Adjust(accent, -0.08f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Adjust(title, -0.04f, 1f));
    }

    public static void ApplyTextScale(Configuration configuration, bool includeMainWindowScale = false)
    {
        var scale = GetTextScale(configuration);
        if (includeMainWindowScale)
            scale *= GetMainInterfaceScale(configuration);

        ImGui.SetWindowFontScale(scale);
    }

    public static int GetMainWindowScalePercent(Configuration configuration) =>
        Math.Clamp(configuration.MainWindowScalePercent, 60, 100);

    public static float GetMainInterfaceScale(Configuration configuration) =>
        GetMainWindowScalePercent(configuration) / 100f;

    public static int GetTextScalePercent(Configuration configuration) =>
        Math.Clamp(configuration.TextScalePercent, 80, 150);

    public static float GetTextScale(Configuration configuration) =>
        GetTextScalePercent(configuration) / 100f;

    public static Vector4 GetTooltipDetailTextColor(Configuration configuration)
    {
        var popupBackground = Adjust(configuration.WindowBackgroundColor, 0.025f, 0.98f);
        var luminance = (popupBackground.X * 0.2126f) +
                        (popupBackground.Y * 0.7152f) +
                        (popupBackground.Z * 0.0722f);
        var contrastTarget = luminance >= 0.55f
            ? new Vector4(0.10f, 0.20f, 0.34f, 1f)
            : new Vector4(0.88f, 0.95f, 1f, 1f);
        var accentBase = Blend(configuration.AccentTextColor, configuration.TextColor, 0.15f);
        return Blend(accentBase, contrastTarget, 0.40f);
    }

    public static Vector4 GetTooltipLabelTextColor(Configuration configuration)
    {
        var popupBackground = Adjust(configuration.WindowBackgroundColor, 0.025f, 0.98f);
        var luminance = (popupBackground.X * 0.2126f) +
                        (popupBackground.Y * 0.7152f) +
                        (popupBackground.Z * 0.0722f);
        var contrastTarget = luminance >= 0.55f
            ? new Vector4(0.12f, 0.18f, 0.26f, 1f)
            : new Vector4(0.94f, 0.97f, 1f, 1f);
        return Blend(configuration.TextColor, contrastTarget, 0.35f);
    }

    public static void Pop() => ImGui.PopStyleColor(PushedColorCount);

    public static void PushButtonStyle(Configuration configuration, float scale = 1f)
    {
        var buttonColor = configuration.ButtonColor;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f * scale, 4f * scale));
        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(buttonColor, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Adjust(buttonColor, ButtonHoverAmount, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Adjust(buttonColor, ButtonActiveAmount, 1f));
    }

    public static void PopButtonStyle()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    public static bool ShadowedButton(string label, Vector2 size = default)
    {
        DrawButtonShadow(label, size);
        ImGui.PushStyleColor(ImGuiCol.Text, GetButtonTextColor());
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor();
        return clicked;
    }

    public static void PushInputCardStyle(Configuration configuration)
    {
        ImGui.PushStyleColor(ImGuiCol.FrameBg, configuration.InputCardColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Adjust(configuration.InputCardColor, 0.06f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Adjust(configuration.InputCardColor, 0.10f, 1f));
    }

    public static void PopInputCardStyle() => ImGui.PopStyleColor(3);

    private static Vector4 Adjust(Vector4 color, float amount, float alpha) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            alpha);

    private static Vector4 Blend(Vector4 from, Vector4 to, float amount) =>
        new(
            from.X + ((to.X - from.X) * amount),
            from.Y + ((to.Y - from.Y) * amount),
            from.Z + ((to.Z - from.Z) * amount),
            from.W + ((to.W - from.W) * amount));

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static void DrawButtonShadow(string label, Vector2 size)
    {
        var style = ImGui.GetStyle();
        var visibleLabel = label.Split("##", StringSplitOptions.None)[0];
        var textSize = ImGui.CalcTextSize(visibleLabel);
        var resolvedSize = new Vector2(
            size.X > 0f ? size.X : textSize.X + (style.FramePadding.X * 2f),
            size.Y > 0f ? size.Y : textSize.Y + (style.FramePadding.Y * 2f));
        var position = ImGui.GetCursorScreenPos();
        var mousePos = ImGui.GetMousePos();
        var isPressed = ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
                        mousePos.X >= position.X &&
                        mousePos.X <= position.X + resolvedSize.X &&
                        mousePos.Y >= position.Y &&
                        mousePos.Y <= position.Y + resolvedSize.Y;
        if (isPressed)
            return;

        var offset = new Vector2(Math.Max(1f, style.FrameRounding * 0.08f), Math.Max(1f, style.FrameRounding * 0.22f));
        var shadowMin = position + offset;
        var shadowMax = shadowMin + resolvedSize;
        ImGui.GetWindowDrawList().AddRectFilled(
            shadowMin,
            shadowMax,
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.12f)),
            Math.Max(2f, style.FrameRounding));
    }

    private static Vector4 GetButtonTextColor()
    {
        var config = Plugin.PluginInterface.GetPluginConfig() as Configuration;
        return config?.ButtonTextColor ?? new Vector4(0.97f, 0.98f, 0.99f, 1f);
    }
}
