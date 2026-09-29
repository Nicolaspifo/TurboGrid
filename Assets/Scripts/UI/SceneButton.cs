using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Boton simple de navegacion entre pantallas que NO usan red
    /// (Inicio -> Instrucciones -> Sala). Si "quit" esta activo, cierra la app.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SceneButton : MonoBehaviour
    {
        [SerializeField] private string sceneName;
        [SerializeField] private bool quit;

        public void Configure(string targetScene, bool quitApp)
        {
            sceneName = targetScene;
            quit = quitApp;
        }

        private void Awake()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Apple no permite botones de "Salir" en apps de iOS.
            if (quit) { gameObject.SetActive(false); return; }
#endif
            GetComponent<Button>().onClick.AddListener(Go);
        }

        private void Go()
        {
            if (quit)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;
            }

            if (!string.IsNullOrEmpty(sceneName))
                SceneManager.LoadScene(sceneName);
        }
    }
}
