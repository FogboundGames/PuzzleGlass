using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    /// <summary>
    /// Test için: yeni mekanik tanıtım kartlarının "görüldü" kayıtlarını siler. Kartlar ve level geçişindeki
    /// "NEW: ..." etiketi bir sonraki girişte yeniden çıkar. Oyun modundayken de çalışır.
    /// </summary>
    public static class TutorialPrefsMenu
    {
        [MenuItem("Tools/PuzzleGlass/🔄 Tutorial Kartlarını Sıfırla", false, 40)]
        public static void ResetTutorialFlags()
        {
            PlayerPrefs.DeleteKey(TutorialOverlay.PrefIce);
            PlayerPrefs.DeleteKey(TutorialOverlay.PrefLock);
            PlayerPrefs.DeleteKey(TutorialOverlay.PrefFilledBox);
            PlayerPrefs.DeleteKey(TutorialOverlay.PrefSlotWarning);
            PlayerPrefs.Save();
            Debug.Log("PuzzleGlass: tutorial kayıtları sıfırlandı (buz, kilit & anahtar, içi dolu kutu, slot uyarısı). Levele tekrar girince yeniden gösterilir.");
        }

        [MenuItem("Tools/PuzzleGlass/🔄 Level İlerlemesini Sıfırla", false, 41)]
        public static void ResetLevelProgress()
        {
            PlayerPrefs.DeleteKey(PuzzleGlassGameManager.SavedLevelPrefKey);
            PlayerPrefs.Save();
            Debug.Log("PuzzleGlass: level ilerlemesi sıfırlandı. Oyun Level 1'den başlar.");
        }
    }
}
