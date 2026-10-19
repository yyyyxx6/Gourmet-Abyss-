using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.Modules.Editor
{
    [Serializable]
    public sealed class DecorationBrushEntry
    {
        public GameObject prefab;
        public bool enabled = true;
        public float weight = 1;
    }

    [Serializable]
    public sealed class DecorationBrushSettings
    {
        public List<DecorationBrushEntry> entries = new List<DecorationBrushEntry>();
        public float radius = 2;
        public float density = 1;
        public float minSpacing = .75f;
        public void Sanitize()
        {
            radius = FiniteClamp(radius, .1f, 30, 2);
            density = FiniteClamp(density, .05f, 10, 1);
            minSpacing = FiniteClamp(minSpacing, .1f, 20, .75f);
            if (entries == null) entries = new List<DecorationBrushEntry>();
            entries.RemoveAll(e => e == null);
            foreach (var entry in entries) entry.weight = FiniteClamp(entry.weight, .01f, 100, 1);
        }
        static float FiniteClamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        public DecorationBrushSettings Copy() => JsonUtility.FromJson<DecorationBrushSettings>(JsonUtility.ToJson(this));
    }

    public sealed class DecorationBrushPreset : ScriptableObject
    {
        public DecorationBrushSettings settings = new DecorationBrushSettings();
    }

    [FilePath("UserSettings/DecorationBrush.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class DecorationBrushSession : ScriptableSingleton<DecorationBrushSession>
    {
        public DecorationBrushSettings settings = new DecorationBrushSettings();
        public string targetId;
        public string presetPath;
        public void Store() => Save(true);
    }
}
