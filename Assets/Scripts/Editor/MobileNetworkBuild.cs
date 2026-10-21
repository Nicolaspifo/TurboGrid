using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace TurboGrid.EditorTools
{
    /// <summary>
    /// Permisos de red local que Unity no expone en Player Settings. Se aplican
    /// solos en cada build (por eso no se pierden al hacer Build and Run):
    ///  - iOS: texto del permiso "Red local" (NSLocalNetworkUsageDescription).
    ///  - Android: permiso para recibir broadcast con el Wi-Fi en ahorro (MulticastLock).
    /// </summary>
    public class MobileNetworkBuild : IPostGenerateGradleAndroidProject
    {
        private const string LocalNetworkText =
            "Turbo Grid usa la red Wi-Fi local para encontrar y unirse a las partidas de tus amigos.";

        public int callbackOrder => 100;

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;
            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription", LocalNetworkText);
            plist.WriteToFile(plistPath);
            Debug.Log("[TurboGrid] iOS: permiso de Red local agregado al Info.plist.");
#endif
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) return;
            string xml = File.ReadAllText(manifest);
            bool changed = false;
            foreach (var perm in new[]
                     {
                         "android.permission.CHANGE_WIFI_MULTICAST_STATE",
                         "android.permission.ACCESS_WIFI_STATE",
                         "android.permission.ACCESS_NETWORK_STATE",
                     })
            {
                if (xml.Contains(perm)) continue;
                int idx = xml.IndexOf("<application", System.StringComparison.Ordinal);
                if (idx < 0) return;
                xml = xml.Insert(idx, $"<uses-permission android:name=\"{perm}\" />\n  ");
                changed = true;
            }
            if (changed)
            {
                File.WriteAllText(manifest, xml);
                Debug.Log("[TurboGrid] Android: permisos de Wi-Fi/multicast agregados al manifest.");
            }
        }
    }
}
