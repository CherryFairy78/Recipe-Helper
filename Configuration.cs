using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace DalamudRecipeHelper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 10;

    public List<SavedRecipePlan> SavedRecipePlans = [];

    public Vector4 EnoughRowColor = Defaults.EnoughRow;

    public Vector4 SuccessTextColor = Defaults.SuccessText;

    public Vector4 MissingTextColor = Defaults.MissingText;

    public Vector4 WarningTextColor = Defaults.WarningText;

    public Vector4 ReadyButtonColor = Defaults.ReadyButton;

    public Vector4 AccentColor = Defaults.Accent;

    public Vector4 ButtonColor = Defaults.Button;

    public Vector4 TitleBarColor = Defaults.TitleBar;

    public Vector4 WindowBackgroundColor = Defaults.WindowBackground;

    public Vector4 TextColor = Defaults.Text;

    public Vector4 InputCardColor = Defaults.InputCard;

    public Vector4 FolderHeaderColor = Defaults.FolderHeader;

    public Vector4 FolderHeaderTextColor = Defaults.FolderHeaderText;

    public bool UseTransparentOverlayBackground;

    public float OverlayBackgroundOpacity = 0.72f;

    public bool ShowVendoredItemsInOverlay = true;

    public float SearchPaneWidth = 180f;

    public int EstimatedSecondsPerCraft = 30;

    public bool ShowObtainedRawMaterials = true;

    public bool UseAccentForFolderHeaders = true;

    public int MainWindowScalePercent = 100;

    public int TextScalePercent = 100;

    public List<ThemePreset> ThemePresets = [];

    public void ResetColors()
    {
        this.EnoughRowColor = Defaults.EnoughRow;
        this.SuccessTextColor = Defaults.SuccessText;
        this.MissingTextColor = Defaults.MissingText;
        this.WarningTextColor = Defaults.WarningText;
        this.ReadyButtonColor = Defaults.ReadyButton;
        this.AccentColor = Defaults.Accent;
        this.ButtonColor = Defaults.Button;
        this.TitleBarColor = Defaults.TitleBar;
        this.WindowBackgroundColor = Defaults.WindowBackground;
        this.TextColor = Defaults.Text;
        this.InputCardColor = Defaults.InputCard;
        this.FolderHeaderColor = Defaults.FolderHeader;
        this.FolderHeaderTextColor = Defaults.FolderHeaderText;
    }

    private static class Defaults
    {
        public static readonly Vector4 EnoughRow = new(0.15f, 0.45f, 0.2f, 0.35f);
        public static readonly Vector4 SuccessText = new(0.45f, 0.9f, 0.55f, 1f);
        public static readonly Vector4 MissingText = new(1f, 0.55f, 0.45f, 1f);
        public static readonly Vector4 WarningText = new(1f, 0.65f, 0.35f, 1f);
        public static readonly Vector4 ReadyButton = new(0.18f, 0.5f, 0.28f, 1f);
        public static readonly Vector4 Accent = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 Button = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 TitleBar = new(0.10f, 0.24f, 0.38f, 1f);
        public static readonly Vector4 WindowBackground = new(0.055f, 0.065f, 0.08f, 0.98f);
        public static readonly Vector4 Text = new(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Vector4 InputCard = new(0.22f, 0.28f, 0.36f, 0.88f);
        public static readonly Vector4 FolderHeader = new(0.42f, 0.52f, 0.74f, 0.92f);
        public static readonly Vector4 FolderHeaderText = new(0.98f, 0.98f, 0.99f, 1f);
    }
}

public sealed class ThemePreset
{
    public string Name { get; set; } = string.Empty;

    public Vector4 EnoughRowColor { get; set; }

    public Vector4 SuccessTextColor { get; set; }

    public Vector4 MissingTextColor { get; set; }

    public Vector4 WarningTextColor { get; set; }

    public Vector4 ReadyButtonColor { get; set; }

    public Vector4 AccentColor { get; set; }

    public Vector4 ButtonColor { get; set; }

    public Vector4 TitleBarColor { get; set; }

    public Vector4 WindowBackgroundColor { get; set; }

    public Vector4 TextColor { get; set; }

    public Vector4 InputCardColor { get; set; }

    public Vector4 FolderHeaderColor { get; set; }

    public Vector4 FolderHeaderTextColor { get; set; }
}
