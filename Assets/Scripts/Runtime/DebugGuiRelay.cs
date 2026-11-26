using System;
using UnityEngine;

namespace PuzzleGlass
{
    /// <summary>
    /// Hata ayıklama OnGUI'lerini taşıyan küçük yardımcı. Bir MonoBehaviour'da OnGUI metodu bulunması, içi hemen
    /// return etse bile Unity'nin her karede IMGUI olaylarını (Layout + Repaint) işlemesine yol açar (telefonda ~0,3 ms).
    /// Bu yüzden asıl çizim kodu sahibinde normal bir metottur; bu bileşen yalnızca gösterim açıkken etkin olur.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DebugGuiRelay : MonoBehaviour
    {
        public Action draw;

        private void OnGUI()
        {
            draw?.Invoke();
        }

        /// <summary>Gösterim durumuna göre yardımcıyı oluşturur/etkinleştirir ya da kapatır. Durum değişmediyse hiçbir şey yapmaz.</summary>
        public static void Sync(GameObject host, ref DebugGuiRelay relay, bool show, Action drawIfCreated)
        {
            if (show)
            {
                if (relay == null)
                {
                    relay = host.AddComponent<DebugGuiRelay>();
                    relay.hideFlags = HideFlags.DontSave;
                    relay.draw = drawIfCreated;
                }
                if (!relay.enabled) relay.enabled = true;
            }
            else if (relay != null && relay.enabled)
            {
                relay.enabled = false;
            }
        }
    }
}
