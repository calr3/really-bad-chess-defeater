// Minimal stand-ins for the Unity/game objects getRandomBoard touches, so the generator runs
// outside Unity with the same random number sequence.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Random
    {
        public static int seed { set { UnityRandomReference.InitState(value); } }
        public static float value => UnityRandomReference.value;
        public static float Range(float a, float b) => UnityRandomReference.Range(a, b);
        public static int Range(int a, int b) => UnityRandomReference.Range(a, b);
    }
    public static class Mathf
    {
        public static int FloorToInt(float f) => (int)Math.Floor(f);
    }
    public static class Debug
    {
        public static void Log(object o) { }
    }
}

public partial class cgChessBoardScript
{
    public sealed class TextShim { public string text = "ranked"; }
    public sealed class TutorialShim { public void setupTutorialIndicator() { } }
    public sealed class SliderShim { public int curDifficulty; }

    /// <summary>"ranked", "freeplay", "daily", "weekly" or "versus".</summary>
    public TextShim modeName = new TextShim();
    public TutorialShim tutorialManager = new TutorialShim();
    public SliderShim freeplayDifficultySlider = new SliderShim();
    public int currentlyLoadedSeed;

    /// <summary>Generate the board a ranked game at <paramref name="rank"/> would start from.</summary>
    public static cgBoard RankedBoard(int seed, int rank)
    {
        var s = new cgChessBoardScript();
        SaveData.Values["playerRank"] = rank;
        return s.getRandomBoard(seed, rank, 0);
    }
}

public static class SaveData
{
    public static readonly Dictionary<string, int> Values = new();
    public static int GetInt(string k, int d) => Values.TryGetValue(k, out var v) ? v : d;
    public static void SetInt(string k, int v) => Values[k] = v;
    public static void SaveAsync() { }
}
