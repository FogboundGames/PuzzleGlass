using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Violet Theme UI paketinden oyunun pastel lavanta/indigo temasına uygun seçilmiş sprite'ları
    /// tek noktada tutan tema varlığı. Resources/PuzzleGlassUITheme üzerinden HUD ve TutorialOverlay yükler.
    /// </summary>
    [CreateAssetMenu(fileName = "PuzzleGlassUITheme", menuName = "PuzzleGlass/UI Theme")]
    public class PuzzleGlassUITheme : ScriptableObject
    {
        public const string ResourcePath = "PuzzleGlassUITheme";

        [Header("Buttons & Panels (9-Sliced)")]
        public Sprite buttonViolet;
        public Sprite buttonBlue;
        public Sprite buttonGreen;
        public Sprite buttonRed;
        public Sprite smallSquareViolet;
        public Sprite smallSquareBlue;
        public Sprite roundedWhite80;
        public Sprite menuDarkPanel;
        public Sprite whitePanelViolet;
        public Sprite darkPanelViolet;

        [Header("White Icons")]
        public Sprite iconSoundOn;
        public Sprite iconSoundOff;
        public Sprite iconRestart;
        public Sprite iconLeft;
        public Sprite iconRight;
        public Sprite iconClose;
        public Sprite iconCheck;
        public Sprite iconHandClick;
        public Sprite iconSnow;
        public Sprite iconLock;
        public Sprite iconKey;
        public Sprite iconGiftBox;
        public Sprite iconTrophy;
        public Sprite iconStar;
        public Sprite iconStarHollow;
        public Sprite iconWarning;
        public Sprite iconInfo;

        [Header("Colored Icons")]
        public Sprite coloredTrophy;
        public Sprite coloredStar;
        public Sprite coloredGoldLock;
        public Sprite coloredGoldKey;
        public Sprite coloredBox;

        private static PuzzleGlassUITheme _cached;

        public static PuzzleGlassUITheme Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<PuzzleGlassUITheme>(ResourcePath);
            }
            return _cached;
        }

        public bool HasCoreHudSprites =>
            buttonViolet != null && smallSquareViolet != null && iconSoundOn != null && iconRestart != null;
    }
}
