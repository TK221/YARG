using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Audio.BASS;
using YARG.Core.Logging;
using YARG.Core.Audio;
using YARG.Core.Input;
using YARG.Helpers;
using YARG.Input;
using YARG.Integration;
using YARG.Localization;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Playlists;
using YARG.Replays;
using YARG.Scores;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG
{
    public enum SceneIndex
    {
        Persistent,
        Menu,
        Gameplay,
        Calibration,
        Score
    }

    [DefaultExecutionOrder(-5000)]
    public class GlobalVariables : MonoSingleton<GlobalVariables>
    {
        public List<YargPlayer> Players { get; private set; }

        public static bool OfflineMode    { get; private set; }
        public static bool VerboseReplays { get; private set; }

        public static string PersistentDataPathOverride { get; private set; }

        public static PersistentState State = PersistentState.Default;

        public SceneIndex CurrentScene { get; private set; } = SceneIndex.Persistent;

        public string CurrentVersion { get; private set; } = "v0.15";

        private float _nextLocalizationUpdate;
        private const float LOCALIZATION_UPDATE_INTERVAL = 1800f;

        private const float VOLUME_SHORTCUT_STEP = 0.05f;
        private const float VOLUME_SHORTCUT_COOLDOWN = 0.12f;
        private const float VOLUME_SHORTCUT_SAVE_DELAY = 1f;

        private float _nextVolumeShortcutTime;

        protected override void SingletonAwake()
        {
            CurrentVersion = LoadVersion();
            YargLogger.LogFormatInfo("YARG {0}", CurrentVersion);

            // Command line arguments

            if (CommandLineArgs.Offline)
            {
                OfflineMode = true;
                YargLogger.LogInfo("Playing in offline mode");
            }

            if (CommandLineArgs.VerboseReplays)
            {
                VerboseReplays = true;
                YargLogger.LogInfo("Verbose replays enabled");
            }

            if (!string.IsNullOrEmpty(CommandLineArgs.DownloadLocation))
            {
                PathHelper.SetPathsFromDownloadLocation(CommandLineArgs.DownloadLocation);
            }

            // TODO: Actually respect the PersistentDataPath arg

            // Initialize important classes

            ReplayContainer.Init();
            ScoreContainer.Init();
            PlaylistContainer.Initialize();
            CustomContentManager.Initialize();
            LocalizationManager.Initialize(CommandLineArgs.Language);

            _nextLocalizationUpdate = Time.realtimeSinceStartup + LOCALIZATION_UPDATE_INTERVAL + UnityEngine.Random.Range(-30f, 30f);

            int profileCount = PlayerContainer.LoadProfiles();
            YargLogger.LogFormatInfo("Loaded {0} profiles", profileCount);

            int savedCount = PlayerContainer.SaveProfiles(false);
            YargLogger.LogFormatInfo("Saved {0} profiles", savedCount);

            GlobalAudioHandler.Initialize<BassAudioManager>();

            Players = new List<YargPlayer>();

            // Set alpha fading (on the tracks) to on
            // (this is mostly for the editor, but just in case)
            Shader.SetGlobalFloat("_IsFading", 1f);
        }

        private void Start()
        {
            SettingsManager.LoadSettings();
            InputManager.Initialize();
            InputManager.MenuInput += OnMenuInput;

            LoadScene(SceneIndex.Menu);
        }

#if UNITY_EDITOR

        // For respecting the editor's mute button
        private bool _previousMute;

        private void Update()
        {
            bool muted = UnityEditor.EditorUtility.audioMasterMute;
            if (muted != _previousMute)
            {
                GlobalAudioHandler.SetMasterVolume(muted ? 0 : SettingsManager.Settings.MasterMusicVolume.Value);
                _previousMute = muted;
            }

            if (CurrentScene != SceneIndex.Gameplay && Time.realtimeSinceStartup > _nextLocalizationUpdate)
            {
                LocalizationManager.LoadUpdates();
                _nextLocalizationUpdate = Time.realtimeSinceStartup + LOCALIZATION_UPDATE_INTERVAL + UnityEngine.Random.Range(-30f, 30f);
                YargLogger.LogFormatDebug("Updating localization at {0}, next update at {1}", Time.realtimeSinceStartup, _nextLocalizationUpdate);
            }
        }

#endif

        protected override void SingletonDestroy()
        {
            InputManager.MenuInput -= OnMenuInput;
            SettingsManager.SaveSettings();
            PlayerContainer.SaveProfiles();
            PlaylistContainer.SaveAll();
            CustomContentManager.SaveAll();

            ReplayContainer.Destroy();
            ScoreContainer.Destroy();
            InputManager.Destroy();
            PlayerContainer.Destroy();
            GlobalAudioHandler.Close();

#if UNITY_EDITOR
            // Set alpha fading (on the tracks) to off
            Shader.SetGlobalFloat("_IsFading", 0f);
#endif
        }

        private void OnMenuInput(YargPlayer _, ref GameInput input)
        {
            if (!input.Button || Time.unscaledTime < _nextVolumeShortcutTime)
            {
                return;
            }

            float volume = SettingsManager.Settings.MasterMusicVolume.Value;
            switch ((MenuAction) input.Action)
            {
                case MenuAction.VolumeUp:
                    volume += VOLUME_SHORTCUT_STEP;
                    break;
                case MenuAction.VolumeDown:
                    volume -= VOLUME_SHORTCUT_STEP;
                    break;
                default:
                    return;
            }

            volume = Mathf.Clamp01(volume);
            if (Mathf.Approximately(volume, SettingsManager.Settings.MasterMusicVolume.Value))
            {
                return;
            }

            SettingsManager.Settings.MasterMusicVolume.Value = volume;
            _nextVolumeShortcutTime = Time.unscaledTime + VOLUME_SHORTCUT_COOLDOWN;

            CancelInvoke(nameof(SaveVolumeShortcutSettings));
            Invoke(nameof(SaveVolumeShortcutSettings), VOLUME_SHORTCUT_SAVE_DELAY);

            ToastManager.ToastInformation(
                Localize.KeyFormat("Menu.Toast.MasterVolume", Localize.Percent(volume)),
                replacementKey: "MasterVolume"
            );
        }

        private void SaveVolumeShortcutSettings()
        {
            SettingsManager.SaveSettings();
        }

        private async void LoadSceneAdditive(SceneIndex scene)
        {
            CurrentScene = scene;

            GameStateFetcher.SetSceneIndex(scene);

            await SceneManager.LoadSceneAsync((int) scene, LoadSceneMode.Additive);

            // When complete, set the newly loaded scene to the active one
            SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex((int) scene));
            Navigator.Instance.DisableMenuInputs = false;

            await Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        public void LoadScene(SceneIndex scene)
        {
            Navigator.Instance.DisableMenuInputs = true;
            // Unload the current scene and load in the new one, or just load in the new one
            if (CurrentScene != SceneIndex.Persistent)
            {
                // Unload the current scene
                var asyncOp = SceneManager.UnloadSceneAsync((int) CurrentScene);

                // The load the new scene
                asyncOp.completed += _ => LoadSceneAdditive(scene);
            }
            else
            {
                LoadSceneAdditive(scene);
            }
        }

        // Due to the preprocessor, it doesn't know that an instance variable is being used
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private string LoadVersion()
        {
#if UNITY_EDITOR
            return LoadVersionFromGit();
#elif YARG_TEST_BUILD || YARG_NIGHTLY_BUILD
            var versionFile = Resources.Load<TextAsset>("version");
            if (versionFile != null)
            {
                return versionFile.text;
            }
            else
            {
                return CurrentVersion;
            }
#else
            return CurrentVersion;
#endif
        }

        public static string LoadVersionFromGit()
        {
            var process = new Process();
            process.StartInfo.FileName = "git";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            // Branch
            process.StartInfo.Arguments = "rev-parse --abbrev-ref HEAD";
            process.Start();
            string branch = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            // Commit Count
            process.StartInfo.Arguments = "rev-list --count HEAD";
            process.Start();
            string commitCount = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            // Commit
            process.StartInfo.Arguments = "rev-parse --short HEAD";
            process.Start();
            string commit = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

#if YARG_NIGHTLY_BUILD
            return $"b{commitCount} ({commit})";
#else
            return $"{branch} b{commitCount} ({commit})";
#endif
        }

        public static string GetReleaseType()
        {
            string kind = null;
#if UNITY_EDITOR || YARG_TEST_BUILD
            kind = "dev";
#elif YARG_NIGHTLY_BUILD
            kind = "nightly";
#else
            kind = "release";
#endif
            return kind;
        }

        // Maybe there is a better place for this?
        public LocalizeText[] GetLocalizedTexts()
        {
            return FindObjectsByType<LocalizeText>(FindObjectsSortMode.None);
        }
    }
}
