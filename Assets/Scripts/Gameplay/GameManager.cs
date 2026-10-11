using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Configuración de Rendimiento")]
    [SerializeField] private int targetFPS = 60;
    [SerializeField] private bool disableVSync = true;

    private void Awake()
    {
        // 1. Patrón Singleton para evitar instancias duplicadas al cambiar de escena
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 2. Mantiene este GameObject vivo a través de los cambios de escena
        DontDestroyOnLoad(gameObject);

        // 3. Aplicar límite de FPS global
        ApplyFPSLimit();
    }

    public void ApplyFPSLimit()
    {
        // Desactivar V-Sync (0) es necesario para que Application.targetFrameRate tenga efecto en PC/Consolas
        if (disableVSync)
        {
            QualitySettings.vSyncCount = 0;
        }

        Application.targetFrameRate = targetFPS;
        Debug.Log($"[GameManager] Límite de FPS establecido en: {targetFPS}");
    }
}
