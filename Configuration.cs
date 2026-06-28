using System.Numerics;
using Dalamud.Configuration;

namespace DalamudRecipeHelper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public Vector4 EnoughRowColor = Defaults.EnoughRow;

    public Vector4 SuccessTextColor = Defaults.SuccessText;

    public Vector4 MissingTextColor = Defaults.MissingText;

    public Vector4 WarningTextColor = Defaults.WarningText;

    public Vector4 ReadyButtonColor = Defaults.ReadyButton;

    public Vector4 AccentColor = Defaults.Accent;

    public Vector4 TitleBarColor = Defaults.TitleBar;

    public Vector4 WindowBackgroundColor = Defaults.WindowBackground;

    public Vector4 TextColor = Defaults.Text;

    public bool UseTransparentOverlayBackground;

    public float OverlayBackgroundOpacity = 0.72f;

    public void ResetColors()
    {
        this.EnoughRowColor = Defaults.EnoughRow;
        this.SuccessTextColor = Defaults.SuccessText;
        this.MissingTextColor = Defaults.MissingText;
        this.WarningTextColor = Defaults.WarningText;
        this.ReadyButtonColor = Defaults.ReadyButton;
        this.AccentColor = Defaults.Accent;
        this.TitleBarColor = Defaults.TitleBar;
        this.WindowBackgroundColor = Defaults.WindowBackground;
        this.TextColor = Defaults.Text;
    }

    private static class Defaults
    {
        public static readonly Vector4 EnoughRow = new(0.15f, 0.45f, 0.2f, 0.35f);
        public static readonly Vector4 SuccessText = new(0.45f, 0.9f, 0.55f, 1f);
        public static readonly Vector4 MissingText = new(1f, 0.55f, 0.45f, 1f);
        public static readonly Vector4 WarningText = new(1f, 0.65f, 0.35f, 1f);
        public static readonly Vector4 ReadyButton = new(0.18f, 0.5f, 0.28f, 1f);
        public static readonly Vector4 Accent = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 TitleBar = new(0.10f, 0.24f, 0.38f, 1f);
        public static readonly Vector4 WindowBackground = new(0.055f, 0.065f, 0.08f, 0.98f);
        public static readonly Vector4 Text = new(0.94f, 0.95f, 0.97f, 1f);
    }
}
