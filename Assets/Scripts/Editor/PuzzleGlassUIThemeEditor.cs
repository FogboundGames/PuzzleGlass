using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    /// <summary>
    /// Violet Theme UI paketindeki sprite'ları otomatik olarak Assets/Resources/PuzzleGlassUITheme.asset
    /// içine bağlar. Hem domain reload sonrası hem Tools menüsünden çalıştırılabilir.
    /// </summary>
    [InitializeOnLoad]
    public static class PuzzleGlassUIThemeEditor
    {
        private const string AssetPath = "Assets/Resources/PuzzleGlassUITheme.asset";
        private const string Root = "Assets/Violet Theme Ui/";
        private const string WhiteUiRoot = "Assets/Resources/UI/";

        static PuzzleGlassUIThemeEditor()
        {
            EditorApplication.delayCall += EnsureThemeAssetIfMissingOrIncomplete;
        }

        private static void EnsureThemeAssetIfMissingOrIncomplete()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PuzzleGlassUITheme>(AssetPath);
            if (existing != null
                && existing.HasCoreHudSprites
                && existing.iconHandClick != null
                && existing.whitePanelViolet != null
                && IsWhiteBorderSprite(existing.buttonViolet)
                && IsWhiteBorderSprite(existing.smallSquareViolet))
            {
                return;
            }
            RebuildThemeAsset(logResult: false);
        }

        private static bool IsWhiteBorderSprite(Sprite s)
        {
            if (s == null) return false;
            string p = AssetDatabase.GetAssetPath(s);
            return !string.IsNullOrEmpty(p) && p.StartsWith(WhiteUiRoot);
        }

        [MenuItem("Tools/PuzzleGlass/UI/Violet UI Temasını Güncelle", false, 41)]
        public static void RebuildThemeMenu()
        {
            RebuildThemeAsset(logResult: true);
        }

        public static PuzzleGlassUITheme RebuildThemeAsset(bool logResult)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var theme = AssetDatabase.LoadAssetAtPath<PuzzleGlassUITheme>(AssetPath);
            bool created = false;
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<PuzzleGlassUITheme>();
                AssetDatabase.CreateAsset(theme, AssetPath);
                created = true;
            }

            bool dirty = created;

            dirty |= Assign(ref theme.buttonViolet,       WhiteUiRoot + "Button Violet.png");
            dirty |= Assign(ref theme.buttonBlue,         WhiteUiRoot + "Button Blue.png");
            dirty |= Assign(ref theme.buttonGreen,        WhiteUiRoot + "Button Green.png");
            dirty |= Assign(ref theme.buttonRed,          WhiteUiRoot + "Button Red.png");
            dirty |= Assign(ref theme.smallSquareViolet,  WhiteUiRoot + "Small Square Button Violet.png");
            dirty |= Assign(ref theme.smallSquareBlue,    WhiteUiRoot + "Small Square Button Blue.png");
            dirty |= Assign(ref theme.roundedWhite80,     Root + "Buttons/Misc/80x80 Background.png");
            dirty |= Assign(ref theme.menuDarkPanel,      Root + "Buttons/Misc/Menu Background.png");
            dirty |= Assign(ref theme.whitePanelViolet,   Root + "Panels/White Panel Style 2 Violet.png");
            dirty |= Assign(ref theme.darkPanelViolet,    Root + "Panels/Dark Panels Style 2 Violet.png");

            dirty |= Assign(ref theme.iconSoundOn,        Root + "White Icons/White Sound On.png");
            dirty |= Assign(ref theme.iconSoundOff,       Root + "White Icons/White Sound Off 2.png");
            dirty |= Assign(ref theme.iconRestart,        Root + "White Icons/White Return 2.png");
            dirty |= Assign(ref theme.iconLeft,           Root + "White Icons/White Left.png");
            dirty |= Assign(ref theme.iconRight,          Root + "White Icons/White Right.png");
            dirty |= Assign(ref theme.iconClose,          Root + "White Icons/White Close 2.png");
            dirty |= Assign(ref theme.iconCheck,          Root + "White Icons/White Check.png");
            dirty |= Assign(ref theme.iconHandClick,      Root + "White Icons/White Click.png");
            dirty |= Assign(ref theme.iconSnow,           Root + "White Icons/White Snow.png");
            dirty |= Assign(ref theme.iconLock,           Root + "White Icons/White Lock.png");
            dirty |= Assign(ref theme.iconKey,            Root + "White Icons/White Key.png");
            dirty |= Assign(ref theme.iconGiftBox,        Root + "White Icons/White Gift.png");
            dirty |= Assign(ref theme.iconTrophy,         Root + "White Icons/White Trophy.png");
            dirty |= Assign(ref theme.iconStar,           Root + "White Icons/White Star.png");
            dirty |= Assign(ref theme.iconStarHollow,     Root + "White Icons/White Star Hollow.png");
            dirty |= Assign(ref theme.iconWarning,        Root + "White Icons/White Warning 2.png");
            dirty |= Assign(ref theme.iconInfo,           Root + "White Icons/White Info 1.png");

            dirty |= Assign(ref theme.coloredTrophy,      Root + "Colored Icons/Trophy.png");
            dirty |= Assign(ref theme.coloredStar,        Root + "Colored Icons/Star.png");
            dirty |= Assign(ref theme.coloredGoldLock,    Root + "Colored Icons/Gold Lock.png");
            dirty |= Assign(ref theme.coloredGoldKey,     Root + "Colored Icons/Gold Key.png");
            dirty |= Assign(ref theme.coloredBox,         Root + "Colored Icons/Box.png");

            if (dirty)
            {
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
            }

            if (logResult)
            {
                Debug.Log($"[PuzzleGlassUITheme] Güncellendi: {AssetPath} (CoreReady={theme.HasCoreHudSprites})");
            }

            return theme;
        }

        private static bool Assign(ref Sprite target, string assetPath)
        {
            Sprite loaded = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (loaded == null)
            {
                loaded = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().FirstOrDefault();
            }
            if (loaded != null && target != loaded)
            {
                target = loaded;
                return true;
            }
            return false;
        }
    }
}
