using System;
using System.Linq;
using DiscordCore;
using IPA;
using UnityEngine;
using BSML = BeatSaberMarkupLanguage;
using IPALogger = IPA.Logging.Logger;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace BeatPresence
{
	[Plugin(RuntimeOptions.DynamicInit)]
	public class Plugin
	{
		// TODO: If using Harmony, uncomment and change YourGitHub to the name of your GitHub account, or use the form "com.company.project.product"
		//       You must also add a reference to the Harmony assembly in the Libs folder.
		// public const string HarmonyId = "com.github.YourGitHub.BeatPresence";
		// internal static readonly HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(HarmonyId);
		
		internal static Plugin Instance { get; private set; }
		internal static IPALogger Log { get; private set; }
		internal static BeatPresenceController PluginController { get { return BeatPresenceController.Instance; } }
		internal static DiscordInstance Discord { get; private set; }

		private AudioTimeSyncController timeSyncController;
		private IPreviewBeatmapLevel levelData;
		private GameplayCoreSceneSetupData gameplaySetupData;
		private string oldState = "";

		/// <summary>
		/// Finds the first instance of a Unity object with type T, or throws if it couldn't be found.
		/// </summary>
		/// <typeparam name="T">The type of object to retrieve</typeparam>
		/// <returns>The retrieved object</returns>
		private static T FindFirstOrDefault<T>() where T : UnityEngine.Object
		{
			// yoinked from https://github.com/opl-/beatsaber-http-status/blob/a8be8e539d3b004289182dc28ad596704b004451/BeatSaberHTTPStatus/Plugin.cs#L285
			T obj = Resources.FindObjectsOfTypeAll<T>().FirstOrDefault();
			if (obj == null)
			{
				Plugin.Log.Error("Couldn't find " + typeof(T).FullName);
				throw new InvalidOperationException("Couldn't find " + typeof(T).FullName);
			}
			return obj;
		}

		public static DiscordSettings DiscordPresenceSettings = new DiscordSettings()
		{
			modId = "BeatPresence",
			modName = "Beat Presence",

			appId = 736414005190721566,

			handleInvites = false,
		};

		internal Discord.Activity currentActivity = new Discord.Activity()
		{
			Name = "Beat Saber",
			ApplicationId = DiscordPresenceSettings.appId,

			Assets = new Discord.ActivityAssets()
			{
				LargeImage = "beatsaber",
				LargeText = "Using BeatPresence by Bytewave",
			}
		};

		[Init]
		/// <summary>
		/// Called when the plugin is first loaded by IPA (either when the game starts or when the plugin is enabled if it starts disabled).
		/// [Init] methods that use a Constructor or called before regular methods like InitWithConfig.
		/// Only use [Init] with one Constructor.
		/// </summary>
		public Plugin(IPALogger logger)
		{
			Instance = this;
			Plugin.Log = logger;
			Plugin.Log?.Info("Becoming mindful.");

			DiscordPresenceSettings.modIcon = BSML.Utilities.FindSpriteInAssembly("BeatPresence.Assets.BeatSaberLogo.png");
		}

		internal void OnMenuSceneLoaded()
		{
			Plugin.Log?.Debug("Main menu loaded!");

			BS_Utils.Gameplay.Gamemode.Init();
		}

		internal void OnMenuSceneActive()
		{
			Plugin.Log?.Debug("Main menu active!");

			timeSyncController = null;
			
			currentActivity.Details = "Main Menu";
			currentActivity.State = "Selecting a song...";
			currentActivity.Timestamps.End = 0;

			currentActivity.Assets.SmallImage = "";
			currentActivity.Assets.SmallText = "";

			Discord.UpdateActivity(currentActivity);
		}

		internal void UpdateSongEndTime()
		{
			float songSpeedMul = gameplaySetupData.gameplayModifiers.songSpeedMul;
			if (gameplaySetupData.practiceSettings != null)
				songSpeedMul = gameplaySetupData.practiceSettings.songSpeedMul;
			float elapsedTime = timeSyncController.songTime / songSpeedMul;
			float remainingTime = levelData.songDuration / songSpeedMul - elapsedTime;
			long now = DateTimeOffset.Now.ToUnixTimeSeconds();
			long endTime = now + (long) remainingTime;
			
			Plugin.Log?.Debug("Updating timestamps:\n" +
			                 $"\tsongSpeedMul  = {songSpeedMul}\n" +
			                 $"\telapsedTime   = {elapsedTime}\n" +
			                 $"\tremainingTime = {remainingTime}\n" +
			                 $"\tnow           = {now}\n" +
			                 $"\tendTime       = {endTime}\n");
			
			currentActivity.Timestamps.End = endTime;
		}

		internal List<string> DecodeActiveModifiers()
		{
			var activeModifers = new List<string>();
			GameplayModifiers mods = gameplaySetupData.gameplayModifiers;

			if (mods.noFail || mods.demoNoFail)
			{
				activeModifers.Add("No Fail");
			}

			if (mods.noBombs)
			{
				activeModifers.Add("No Bombs");
			}

			if (mods.demoNoObstacles)
			{
				activeModifers.Add("No Obstacles");
			}

			if (mods.noArrows)
			{
				activeModifers.Add("No Arrows");
			}

			if (mods.songSpeed == GameplayModifiers.SongSpeed.Slower)
			{
				activeModifers.Add("Slower Song");
			}

			if (mods.instaFail)
			{
				activeModifers.Add("Insta Fail");
			}

			if (mods.energyType == GameplayModifiers.EnergyType.Battery)
			{
				activeModifers.Add("Battery Energy");
			}

			if (mods.ghostNotes)
			{
				activeModifers.Add("Ghost Notes");
			}

			if (mods.disappearingArrows)
			{
				activeModifers.Add("Disappearing Arrows");
			}

			if (mods.songSpeed == GameplayModifiers.SongSpeed.Faster)
			{
				activeModifers.Add("Faster Song");
			}

			return activeModifers;
		}

		internal void OnGameSceneActive()
		{
			Plugin.Log?.Debug("Song started!");

			timeSyncController = FindFirstOrDefault<AudioTimeSyncController>();

			BS_Utils.Gameplay.LevelData level = BS_Utils.Plugin.LevelData;
			gameplaySetupData = BS_Utils.Plugin.LevelData.GameplayCoreSceneSetupData;
			levelData = level.GameplayCoreSceneSetupData.difficultyBeatmap.level;
			BeatmapCharacteristicSO beatmapCharacteristics = gameplaySetupData.difficultyBeatmap.parentDifficultyBeatmapSet.beatmapCharacteristic;

			currentActivity.Details = levelData.songAuthorName + " - " + levelData.songName;

			string levelDifficulty = gameplaySetupData.difficultyBeatmap.difficulty.ToString();
			levelDifficulty = levelDifficulty switch
			{
				"ExpertPlus" => "Expert+",
				_ => levelDifficulty,
			};
			currentActivity.State = levelDifficulty;

			string mapType = "";
			if (beatmapCharacteristics.numberOfColors == 1)
			{
				mapType = "One Saber";
			}
			else if (beatmapCharacteristics.containsRotationEvents)
			{
				if (beatmapCharacteristics.requires360Movement)
				{
					mapType = "360";
				}
				else
				{
					mapType = "90";
				}
			}
			if (mapType != "")
			{
				currentActivity.State += $" | {mapType}";
			}

			string gamemode = level.Mode.ToString();
			if (BS_Utils.Gameplay.Gamemode.IsPartyActive)
			{
				gamemode = "Party!";
			}
			currentActivity.State += $" | {gamemode}";

			var activeMods = DecodeActiveModifiers();
			Plugin.Log?.Debug($"Active modifiers: {string.Join(", ", activeMods)}");
			if (activeMods.Any())
			{
				currentActivity.Assets.SmallImage = "plus";
				currentActivity.Assets.SmallText = $"Modifiers: {string.Join(", ", activeMods)}";
			}

			UpdateSongEndTime();
			Discord.UpdateActivity(currentActivity);
		}

		internal void OnSongPaused()
		{
			Plugin.Log?.Debug("Song paused.");

			oldState = currentActivity.State;
			currentActivity.State = $"[PAUSED] {oldState}";
			currentActivity.Timestamps.End = 0;
			Discord.UpdateActivity(currentActivity);
		}

		internal void OnSongUnpaused()
		{
			Plugin.Log?.Debug("Song resumed.");

			currentActivity.State = oldState;
			UpdateSongEndTime();
			Discord.UpdateActivity(currentActivity);
		}

		#region BSIPA Config
		//Uncomment to use BSIPA's config
		/*
        [Init]
        public void InitWithConfig(Config conf)
        {
            Configuration.PluginConfig.Instance = conf.Generated<Configuration.PluginConfig>();
            Plugin.Log?.Debug("Config loaded");
        }
        */
		#endregion


		#region Disableable

		/// <summary>
		/// Called when the plugin is enabled (including when the game starts if the plugin is enabled).
		/// </summary>
		[OnEnable]
		public void OnEnable()
		{
			//new GameObject("BeatPresenceController").AddComponent<BeatPresenceController>();
			Plugin.Log?.Info("I'm aliiiiiive!");
			Discord = DiscordManager.instance.CreateInstance(DiscordPresenceSettings);
			BS_Utils.Utilities.BSEvents.menuSceneLoaded += OnMenuSceneLoaded;
			BS_Utils.Utilities.BSEvents.menuSceneActive += OnMenuSceneActive;
			BS_Utils.Utilities.BSEvents.gameSceneActive += OnGameSceneActive;
			BS_Utils.Utilities.BSEvents.songPaused += OnSongPaused;
			BS_Utils.Utilities.BSEvents.songUnpaused += OnSongUnpaused;
			//ApplyHarmonyPatches();
		}
		
		/// <summary>
		/// Called when the plugin is disabled and on Beat Saber quit. It is important to clean up any Harmony patches, GameObjects, and Monobehaviours here.
		/// The game should be left in a state as if the plugin was never started.
		/// Methods marked [OnDisable] must return void or Task.
		/// </summary>
		[OnDisable]
		public void OnDisable()
		{
			if (PluginController != null)
				GameObject.Destroy(PluginController);
			Discord?.DestroyInstance();
			BS_Utils.Utilities.BSEvents.menuSceneActive -= OnMenuSceneActive;
			BS_Utils.Utilities.BSEvents.gameSceneActive -= OnGameSceneActive;
			BS_Utils.Utilities.BSEvents.gameSceneActive -= OnGameSceneActive;
			BS_Utils.Utilities.BSEvents.songPaused -= OnSongPaused;
			BS_Utils.Utilities.BSEvents.songUnpaused -= OnSongUnpaused;
			//RemoveHarmonyPatches();
			Plugin.Log?.Info("See ya' around!");
		}

		/*
        /// <summary>
        /// Called when the plugin is disabled and on Beat Saber quit.
        /// Return Task for when the plugin needs to do some long-running, asynchronous work to disable.
        /// [OnDisable] methods that return Task are called after all [OnDisable] methods that return void.
        /// </summary>
        [OnDisable]
        public async Task OnDisableAsync()
        {
            await LongRunningUnloadTask().ConfigureAwait(false);
        }
        */
		#endregion

		// Uncomment the methods in this section if using Harmony
		#region Harmony
		/*
        /// <summary>
        /// Attempts to apply all the Harmony patches in this assembly.
        /// </summary>
        internal static void ApplyHarmonyPatches()
        {
            try
            {
                Plugin.Log?.Debug("Applying Harmony patches.");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("Error applying Harmony patches: " + ex.Message);
                Plugin.Log?.Debug(ex);
            }
        }

        /// <summary>
        /// Attempts to remove all the Harmony patches that used our HarmonyId.
        /// </summary>
        internal static void RemoveHarmonyPatches()
        {
            try
            {
                // Removes all patches with this HarmonyId
                harmony.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                Plugin.Log?.Error("Error removing Harmony patches: " + ex.Message);
                Plugin.Log?.Debug(ex);
            }
        }
        */
		#endregion
	}
}
