using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Mm6ViewerCatalog", menuName = "MM6/Viewer Catalog")]
public sealed class Mm6ViewerCatalog : ScriptableObject
{
    public MapEntry[] maps = Array.Empty<MapEntry>();

    [Serializable]
    public sealed class MapEntry
    {
        public string mapName;
        public string mapType;
        public string sceneName;
        public string scenePath;
    }
}
