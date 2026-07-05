using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace DalamudRecipeHelper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 16;

    public List<SavedRecipePlan> SavedRecipePlans = [];

    public List<string> SavedPlanFolders = [];

    public Vector4 EnoughRowColor = Defaults.EnoughRow;

    public Vector4 SuccessTextColor = Defaults.SuccessText;

    public Vector4 MissingTextColor = Defaults.MissingText;

    public Vector4 WarningTextColor = Defaults.WarningText;

    public Vector4 AccentColor = Defaults.Accent;

    public Vector4 AccentTextColor = Defaults.AccentText;

    public Vector4 ButtonColor = Defaults.Button;

    public Vector4 ButtonTextColor = Defaults.ButtonText;

    public Vector4 TitleBarColor = Defaults.TitleBar;

    public Vector4 WindowBackgroundColor = Defaults.WindowBackground;

    public Vector4 TextColor = Defaults.Text;

    public Vector4 InputCardColor = Defaults.InputCard;

    public Vector4 FolderHeaderColor = Defaults.FolderHeader;

    public Vector4 SectionHeaderTextColor = Defaults.SectionHeaderText;

    public Vector4 FolderHeaderTextColor = Defaults.FolderHeaderText;

    public Vector4 SubfolderHeaderColor = Defaults.SubfolderHeader;

    public Vector4 SubfolderHeaderTextColor = Defaults.SubfolderHeaderText;

    public Vector4 SavedPlanTextColor = Defaults.SavedPlanText;

    public bool UseTransparentOverlayBackground;

    public float OverlayBackgroundOpacity = 0.72f;

    public bool ShowVendoredItemsInOverlay = true;

    public float SearchPaneWidth = 180f;

    public int EstimatedSecondsPerCraft = 30;

    public bool ShowObtainedRawMaterials = true;

    public bool UseAccentForFolderHeaders = true;

    public int MainWindowScalePercent = 100;

    public int TextScalePercent = 100;

    public bool HasSavedArtisanPopupPosition;

    public float ArtisanPopupPositionX;

    public float ArtisanPopupPositionY;

    public List<ThemePreset> ThemePresets = [];

    public void ResetColors()
    {
        this.EnoughRowColor = Defaults.EnoughRow;
        this.SuccessTextColor = Defaults.SuccessText;
        this.MissingTextColor = Defaults.MissingText;
        this.WarningTextColor = Defaults.WarningText;
        this.AccentColor = Defaults.Accent;
        this.AccentTextColor = Defaults.AccentText;
        this.ButtonColor = Defaults.Button;
        this.ButtonTextColor = Defaults.ButtonText;
        this.TitleBarColor = Defaults.TitleBar;
        this.WindowBackgroundColor = Defaults.WindowBackground;
        this.TextColor = Defaults.Text;
        this.InputCardColor = Defaults.InputCard;
        this.FolderHeaderColor = Defaults.FolderHeader;
        this.SectionHeaderTextColor = Defaults.SectionHeaderText;
        this.FolderHeaderTextColor = Defaults.FolderHeaderText;
        this.SubfolderHeaderColor = Defaults.SubfolderHeader;
        this.SubfolderHeaderTextColor = Defaults.SubfolderHeaderText;
        this.SavedPlanTextColor = Defaults.SavedPlanText;
    }

    private static class Defaults
    {
        public static readonly Vector4 EnoughRow = new(0.15f, 0.45f, 0.2f, 0.35f);
        public static readonly Vector4 SuccessText = new(0.45f, 0.9f, 0.55f, 1f);
        public static readonly Vector4 MissingText = new(1f, 0.55f, 0.45f, 1f);
        public static readonly Vector4 WarningText = new(1f, 0.65f, 0.35f, 1f);
        public static readonly Vector4 Accent = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 AccentText = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 Button = new(0.26f, 0.58f, 0.88f, 1f);
        public static readonly Vector4 ButtonText = new(0.97f, 0.98f, 0.99f, 1f);
        public static readonly Vector4 TitleBar = new(0.10f, 0.24f, 0.38f, 1f);
        public static readonly Vector4 WindowBackground = new(0.055f, 0.065f, 0.08f, 0.98f);
        public static readonly Vector4 Text = new(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Vector4 InputCard = new(0.22f, 0.28f, 0.36f, 0.88f);
        public static readonly Vector4 FolderHeader = new(0.42f, 0.52f, 0.74f, 0.92f);
        public static readonly Vector4 SectionHeaderText = new(0.98f, 0.98f, 0.99f, 1f);
        public static readonly Vector4 FolderHeaderText = new(0.98f, 0.98f, 0.99f, 1f);
        public static readonly Vector4 SubfolderHeader = new(0.34f, 0.42f, 0.60f, 0.88f);
        public static readonly Vector4 SubfolderHeaderText = new(0.95f, 0.96f, 0.98f, 1f);
        public static readonly Vector4 SavedPlanText = new(0.94f, 0.95f, 0.97f, 1f);
    }
}

public sealed class ThemePreset
{
    public string Name { get; set; } = string.Empty;

    public Vector4 EnoughRowColor { get; set; }

    public Vector4 SuccessTextColor { get; set; }

    public Vector4 MissingTextColor { get; set; }

    public Vector4 WarningTextColor { get; set; }

    public Vector4 AccentColor { get; set; }

    public Vector4 AccentTextColor { get; set; }

    public Vector4 ButtonColor { get; set; }

    public Vector4 ButtonTextColor { get; set; }

    public Vector4 TitleBarColor { get; set; }

    public Vector4 WindowBackgroundColor { get; set; }

    public Vector4 TextColor { get; set; }

    public Vector4 InputCardColor { get; set; }

    public Vector4 FolderHeaderColor { get; set; }

    public Vector4 SectionHeaderTextColor { get; set; }

    public Vector4 FolderHeaderTextColor { get; set; }

    public Vector4 SubfolderHeaderColor { get; set; }

    public Vector4 SubfolderHeaderTextColor { get; set; }

    public Vector4 SavedPlanTextColor { get; set; }
}
