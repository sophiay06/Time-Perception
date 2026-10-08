
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class ExperimentalVideoProgressBar : MonoBehaviour
{
    public enum Condition { Fast, Normal, Slow }

    [Header("References")]
    public VideoPlayer videoPlayer;
    public GameObject progressBarPanel;
    public Image progressBarFill;

    [Header("Video Library")]
    public List<VideoClip> videoClips = new List<VideoClip>();

    [HideInInspector]
    public int selectedVideoIndex = 0;

    [Header("Current Condition")]
    public Condition condition = Condition.Normal;

    [Header("Progress Bar Speed")]
    [Min(1f)] public float fastSpeed = 1.5f;
    [Range(0.01f, 1f)] public float slowSpeed = 0.7f;

    [Header("Appearance Settings")]
    [Min(0)] public int appearanceCount = 5;
    [Min(0.1f)] public float visibleDuration = 3f;
    [Min(0f)] public float hideBeforeEnd = 60f;

    [Header("Fixed Random Seeds")]
    public int fastSeed = 12345;
    public int normalSeed = 23456;
    public int slowSeed = 34567;

    [Header("Debug")]
    public bool printSchedule = true;

    private readonly List<double> appearanceTimes =
        new List<double>();

    private double videoDuration;
    private bool initialized;
    private bool videoEnded;
    private bool progressVisible;
    private int currentAppearanceIndex = -1;

    void Awake()
    {
        if (progressBarPanel != null)
            progressBarPanel.SetActive(false);

        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = false;
            videoPlayer.playbackSpeed = 1f;

            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoEnded;
        }
    }

    void Start()
    {
        RestartExperiment();
    }

    public void SetCondition(Condition newCondition)
    {
        condition = newCondition;

        if (Application.isPlaying)
            RestartExperiment();
    }

    public void RestartExperiment()
    {
        if (videoPlayer == null ||
            progressBarPanel == null ||
            progressBarFill == null)
        {
            Debug.LogError("Missing progress bar references.");
            return;
        }

        if (videoClips.Count == 0)
        {
            Debug.LogError("Add Video Clips in Inspector.");
            return;
        }

        selectedVideoIndex = Mathf.Clamp(
            selectedVideoIndex, 0, videoClips.Count - 1);

        VideoClip selectedClip = videoClips[selectedVideoIndex];

        if (selectedClip == null)
        {
            Debug.LogError("Selected video slot is empty.");
            return;
        }

        initialized = false;
        videoEnded = false;
        progressVisible = false;
        currentAppearanceIndex = -1;

        appearanceTimes.Clear();

        progressBarPanel.SetActive(false);
        progressBarFill.fillAmount = 0f;

        videoPlayer.Stop();

        // Select video BEFORE preparing.
        videoPlayer.source = VideoSource.VideoClip;
        videoPlayer.clip = selectedClip;
        videoPlayer.playbackSpeed = 1f;
        videoPlayer.isLooping = false;

        Debug.Log("Selected video: " + selectedClip.name);

        videoPlayer.Prepare();
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        if (vp.clip == null) return;

        videoDuration = vp.length;

        if (double.IsNaN(videoDuration) ||
            double.IsInfinity(videoDuration) ||
            videoDuration <= 0)
        {
            Debug.LogError("Invalid video duration.");
            return;
        }

        GenerateAppearanceSchedule();

        initialized = true;
        videoEnded = false;

        vp.playbackSpeed = 1f;
        vp.Play();

        Debug.Log(
            "Condition: " + condition +
            " | Video: " + vp.clip.name +
            " | Duration: " + videoDuration.ToString("F2") +
            " seconds");
    }

    private int GetConditionSeed()
    {
        switch (condition)
        {
            case Condition.Fast: return fastSeed;
            case Condition.Slow: return slowSeed;
            default: return normalSeed;
        }
    }

    private float GetProgressSpeed()
    {
        switch (condition)
        {
            case Condition.Fast:
                return Mathf.Max(1f, fastSpeed);

            case Condition.Slow:
                return Mathf.Clamp(slowSpeed, 0.01f, 1f);

            default:
                return 1f;
        }
    }

    private void GenerateAppearanceSchedule()
    {
        appearanceTimes.Clear();

        if (appearanceCount <= 0)
            return;

        double allowedStart = 1.0;
        double allowedEnd = videoDuration - hideBeforeEnd;
        double availableDuration = allowedEnd - allowedStart;

        if (availableDuration <= 0)
        {
            Debug.LogWarning(
                "Video too short for progress bar appearances.");
            return;
        }

        double segmentLength =
            availableDuration / appearanceCount;

        if (segmentLength < visibleDuration)
        {
            Debug.LogError(
                "Not enough time for the requested appearances. " +
                "Reduce appearanceCount or visibleDuration.");
            return;
        }

        // Condition-specific deterministic randomness.
        System.Random rng =
            new System.Random(GetConditionSeed());

        for (int i = 0; i < appearanceCount; i++)
        {
            double segmentStart =
                allowedStart + i * segmentLength;

            double segmentEnd =
                segmentStart + segmentLength;

            double latestStart =
                segmentEnd - visibleDuration;

            double appearanceTime =
                segmentStart +
                rng.NextDouble() * (latestStart - segmentStart);

            appearanceTimes.Add(appearanceTime);

            if (printSchedule)
            {
                Debug.Log(
                    condition +
                    " | Appearance " + (i + 1) +
                    " | Start: " +
                    appearanceTime.ToString("F2") + "s" +
                    " | End: " +
                    (appearanceTime + visibleDuration)
                    .ToString("F2") + "s");
            }
        }
    }

    void Update()
    {
        if (!initialized || videoEnded)
            return;

        if (!videoPlayer.isPlaying)
        {
            SetProgressVisibility(false);
            return;
        }

        double actualTime = videoPlayer.time;

        // The video runs at 1x.
        // Only displayed progress is manipulated.
        float actualProgress = Mathf.Clamp01(
            (float)(actualTime / videoDuration));

        float speed = GetProgressSpeed();

        float displayedProgress =
            Mathf.Clamp01(actualProgress * speed);

        progressBarFill.fillAmount = displayedProgress;

        // Never show the bar in the last 60 seconds.
        if (actualTime >= videoDuration - hideBeforeEnd)
        {
            SetProgressVisibility(false);
            return;
        }

        bool shouldShow = false;

        for (int i = 0; i < appearanceTimes.Count; i++)
        {
            double start = appearanceTimes[i];
            double end = start + visibleDuration;

            if (actualTime >= start && actualTime < end)
            {
                shouldShow = true;

                if (currentAppearanceIndex != i)
                {
                    currentAppearanceIndex = i;

                    Debug.Log(
                        condition +
                        " | Video: " + videoPlayer.clip.name +
                        " | Appearance #" + (i + 1) +
                        " | Actual: " +
                        actualTime.ToString("F2") + "s" +
                        " | Displayed: " +
                        (displayedProgress * 100f)
                        .ToString("F1") + "%");
                }

                break;
            }
        }

        SetProgressVisibility(shouldShow);
    }

    private void SetProgressVisibility(bool visible)
    {
        if (progressVisible == visible)
            return;

        progressVisible = visible;
        progressBarPanel.SetActive(visible);
    }

    private void OnVideoEnded(VideoPlayer vp)
    {
        videoEnded = true;
        initialized = false;
        SetProgressVisibility(false);

        Debug.Log(
            "Video completed: " + vp.clip.name +
            " | Condition: " + condition);
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoEnded;
        }
    }
}

#if UNITY_EDITOR

[UnityEditor.CustomEditor(typeof(ExperimentalVideoProgressBar))]
public class ExperimentalVideoProgressBarEditor
    : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        ExperimentalVideoProgressBar controller =
            (ExperimentalVideoProgressBar)target;

        // Draw ordinary fields except selectedVideoIndex.
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "selectedVideoIndex");

        serializedObject.ApplyModifiedProperties();

        UnityEditor.EditorGUILayout.Space(12);

        UnityEditor.EditorGUILayout.LabelField(
            "VIDEO SELECTION",
            UnityEditor.EditorStyles.boldLabel);

        if (controller.videoClips != null &&
            controller.videoClips.Count > 0)
        {
            string[] videoNames =
                new string[controller.videoClips.Count];

            for (int i = 0; i < videoNames.Length; i++)
            {
                VideoClip clip = controller.videoClips[i];

                videoNames[i] =
                    clip != null ? clip.name : "Empty Slot " + i;
            }

            int safeIndex = Mathf.Clamp(
                controller.selectedVideoIndex,
                0,
                videoNames.Length - 1);

            int newIndex = UnityEditor.EditorGUILayout.Popup(
                "Selected Video",
                safeIndex,
                videoNames);

            if (newIndex != controller.selectedVideoIndex)
            {
                UnityEditor.Undo.RecordObject(
                    controller, "Select Experiment Video");

                controller.selectedVideoIndex = newIndex;

                UnityEditor.EditorUtility.SetDirty(controller);
            }
        }
        else
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "Add videos to the Video Clips list above.",
                UnityEditor.MessageType.Warning);
        }

        UnityEditor.EditorGUILayout.Space(12);

        UnityEditor.EditorGUILayout.LabelField(
            "EXPERIMENT CONDITION BUTTONS",
            UnityEditor.EditorStyles.boldLabel);

        UnityEditor.EditorGUILayout.BeginHorizontal();

        DrawConditionButton(
            controller,
            ExperimentalVideoProgressBar.Condition.Fast,
            "FAST");

        DrawConditionButton(
            controller,
            ExperimentalVideoProgressBar.Condition.Normal,
            "NORMAL");

        DrawConditionButton(
            controller,
            ExperimentalVideoProgressBar.Condition.Slow,
            "SLOW");

        UnityEditor.EditorGUILayout.EndHorizontal();

        UnityEditor.EditorGUILayout.Space(8);

        if (GUILayout.Button(
            "RESTART CURRENT CONDITION",
            GUILayout.Height(30)))
        {
            if (Application.isPlaying)
                controller.RestartExperiment();
            else
                Debug.Log("Enter Play Mode to start.");
        }
    }

    private void DrawConditionButton(
        ExperimentalVideoProgressBar controller,
        ExperimentalVideoProgressBar.Condition newCondition,
        string label)
    {
        bool selected =
            controller.condition == newCondition;

        Color previousColor = GUI.backgroundColor;

        GUI.backgroundColor = selected
            ? new Color(0.45f, 0.85f, 0.55f)
            : Color.white;

        if (GUILayout.Button(
            label + (selected ? " ✓" : ""),
            GUILayout.Height(38)))
        {
            UnityEditor.Undo.RecordObject(
                controller, "Change Experiment Condition");

            controller.SetCondition(newCondition);

            UnityEditor.EditorUtility.SetDirty(controller);
        }

        GUI.backgroundColor = previousColor;
    }
}
#endif
