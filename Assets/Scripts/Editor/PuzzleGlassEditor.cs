using UnityEditor;
using UnityEngine;

namespace PuzzleGlass.Editor
{
    [CustomEditor(typeof(PuzzleGlassGameManager))]
    public class PuzzleGlassGameManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PuzzleGlassGameManager manager = (PuzzleGlassGameManager)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Interactive Game Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUI.backgroundColor = new Color(0.25f, 0.65f, 1.0f);
            if (GUILayout.Button("⚡ Feed 1 Cube (Spacebar)", GUILayout.Height(36)))
            {
                if (Application.isPlaying)
                {
                    manager.FeedOneCube();
                }
                else
                {
                    Debug.LogWarning("[PuzzleGlass] Lütfen küp yedirme animasyonunu test etmek için Play moduna geçin.");
                }
            }

            string autoText = (manager.cubeDispenser != null && manager.cubeDispenser.isAutoFeeding) 
                ? "⏹ Otomatik Yedirmeyi Durdur" 
                : "▶ Otomatik Yedirmeyi Başlat (A)";

            GUI.backgroundColor = (manager.cubeDispenser != null && manager.cubeDispenser.isAutoFeeding) 
                ? new Color(1.0f, 0.4f, 0.4f) 
                : new Color(0.3f, 0.85f, 0.45f);

            if (GUILayout.Button(autoText, GUILayout.Height(32)))
            {
                if (Application.isPlaying)
                {
                    manager.ToggleAutoFeed();
                }
                else
                {
                    Debug.LogWarning("[PuzzleGlass] Lütfen Play modunda test edin.");
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Senaryo Testleri", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("1. Referans Çizim (Mavi 10 + Sarı 20)", GUILayout.Height(28)))
            {
                manager.SetupReferenceScenario();
            }

            if (GUILayout.Button("2. Aynı Renk Testi (2x Mavi - İzole Kuyruk)", GUILayout.Height(28)))
            {
                manager.SetupSameColorScenario();
            }

            if (GUILayout.Button("3. 4 Renk Varyasyonu (Mavi, Sarı, Kırmızı, Yeşil)", GUILayout.Height(28)))
            {
                manager.SetupFourColorsScenario();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Sahneyi Sıfırla / Yeniden İnşa Et", GUILayout.Height(26)))
            {
                PuzzleGlassSceneSetup.BuildCompleteScene();
            }

            if (GUILayout.Button("Prefab ve Materyalleri Diske Kaydet", GUILayout.Height(26)))
            {
                PuzzleGlassPrefabGenerator.GenerateAllAssets();
            }
        }
    }

    [CustomEditor(typeof(MonsterCharacter))]
    public class MonsterCharacterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MonsterCharacter monster = (MonsterCharacter)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Yaratık Testleri", EditorStyles.boldLabel);

            if (GUILayout.Button("Yaratık Görünümünü Yeniden Oluştur", GUILayout.Height(28)))
            {
                monster.RebuildMonsterVisuals();
            }

            if (Application.isPlaying)
            {
                if (GUILayout.Button("Küp Yutma Animasyonu Testi (1 Küp)", GUILayout.Height(28)))
                {
                    monster.FeedCube();
                }
            }
        }
    }

    [CustomEditor(typeof(ShapedGlassContainer))]
    public class ShapedGlassContainerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ShapedGlassContainer container = (ShapedGlassContainer)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Cam Şişe & Küp Kontrolleri", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUI.backgroundColor = new Color(0.25f, 0.75f, 1.0f);
            if (GUILayout.Button("🍾 Cam Şişeyi Yeniden İnşa Et", GUILayout.Height(36)))
            {
                container.RebuildContainer();
                EditorUtility.SetDirty(container);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndVertical();
        }
    }
}
