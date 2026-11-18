using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Anahtar rozetinin dinlenme boyutu/açısı. Kilitli bloğa dokununca oynayan ipucu animasyonu her seferinde
    /// buradan başlar; arka arkaya dokunuşlar rozeti kalıcı büyütmez.
    /// </summary>
    public class KeyHintRest : MonoBehaviour
    {
        public Vector3 scale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
    }
}
