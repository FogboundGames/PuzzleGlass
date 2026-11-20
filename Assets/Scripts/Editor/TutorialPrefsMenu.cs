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
            PlayerPrefs.Save();
            Debug.Log("PuzzleGlass: tutorial kartları sıfırlandı (buz, kilit & anahtar, içi dolu kutu). Levele tekrar girince yeniden gösterilir.");
        }
    }
}
