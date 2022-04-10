﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using YamlDotNet.Serialization;
using Random = UnityEngine.Random;

namespace DarwinAwards;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class DarwinAwards : BaseUnityPlugin
{
	private const string ModName = "Darwin Awards";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "org.bepinex.plugins.darwinawards";

	private static string configDir => Paths.ConfigPath;

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	public static ConfigEntry<Vector2> deathLogAnchor = null!;
	public static ConfigEntry<int> numberOfDeaths = null!;
	public static ConfigEntry<uint> timerForDeaths = null!;

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static readonly CustomSyncedValue<string> deathTexts = new(configSync, "deathTexts", readDeathTexts());
	private static Dictionary<string, List<string>> deathTextDict = new();

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0
	}

	public void Awake()
	{
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, new ConfigDescription("If on, only server admins can change the configuration."));
		configSync.AddLockingConfigEntry(serverConfigLocked);
		numberOfDeaths = config("1 - General", "Number of deaths", 3, new ConfigDescription("Number of deaths to display at the same time. Set this to 0, to disable the death log.", new AcceptableValueRange<int>(0, 25)), false);
		timerForDeaths = config("1 - General", "Timer for deaths", 10u, new ConfigDescription("Time in seconds the deaths are displayed. 0 means no time limit."), false);
		timerForDeaths.SettingChanged += Display.CheckDeathTimes;
		deathLogAnchor = config("1 - General", "Position of the death log", new Vector2(700, 220), new ConfigDescription("Position of the death log."), false);
		deathLogAnchor.SettingChanged += Display.AnchorDeathLog;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		FileSystemWatcher deathTextsWatcher = new(configDir, "AwardCategories.yml");
		deathTextsWatcher.Created += deathTextsFileEvent;
		deathTextsWatcher.Changed += deathTextsFileEvent;
		deathTextsWatcher.Renamed += deathTextsFileEvent;
		deathTextsWatcher.Deleted += deathTextsFileEvent;
		deathTextsWatcher.IncludeSubdirectories = true;
		deathTextsWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
		deathTextsWatcher.EnableRaisingEvents = true;

		deathTexts.ValueChanged += ReadYamlFile;
		ReadYamlFile();

		Display.FillIconMap();
	}
	
	[HarmonyPatch(typeof(Game), nameof(Game.Start))]
	private class AddRPCs
	{
		private static void Postfix()
		{
			ZRoutedRpc.instance.Register<string, string>("DarwinAwards IDied", onReceivedDeath);
		}
	}

	private static void onReceivedDeath(long senderId, string category, string text)
	{
		Display.AddText(new DeathText { category = category, text = text });
	}

	private static void BroadcastDeath(DeathText text)
	{
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "DarwinAwards IDied", text.category, text.text);
	}

	private static void ReadYamlFile()
	{
		deathTextDict = new DeserializerBuilder().IgnoreFields().Build().Deserialize<Dictionary<string, List<string>>?>(deathTexts.Value) ?? new Dictionary<string, List<string>>();
	}

	private static void deathTextsFileEvent(object s, EventArgs e) => deathTexts.AssignLocalValue(readDeathTexts());

	private static string readDeathTexts()
	{
		string textPath = configDir + Path.DirectorySeparatorChar + "AwardCategories.yml";
		if (File.Exists(textPath))
		{
			return File.ReadAllText(textPath);
		}

		Debug.LogWarning($"AwardCategories.yml for Darwin Awards is missing at {textPath}");
		return "";
	}

	[HarmonyPatch(typeof(Character), nameof(Character.SetHealth))]
	private class InterceptDeath
	{
		private static void Prefix(Character __instance, float health)
		{
			if (__instance == Player.m_localPlayer && __instance.GetHealth() > 0 && health <= 0)
			{
				if (__instance.IsSwiming())
				{
					BroadcastDeath(getRandomText("death by drowning"));
					return;
				}

				if (SaveCauseOfDeath.deathHit is null)
				{
					return;
				}

				HashSet<string> deathTypes = new();

				if (SaveCauseOfDeath.deathHit.GetAttacker() != null)
				{
					if (SaveCauseOfDeath.deathHit.GetAttacker().IsBoss())
					{
						deathTypes.Add("death by boss");
					}
					else if (SaveCauseOfDeath.deathHit.GetAttacker().IsPlayer())
					{
						deathTypes.Add("death by player");
					}
					else
					{
						deathTypes.Add("death by creature");
					}
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_blunt > 0)
				{
					deathTypes.Add("death by blunt");
					deathTypes.Add("death by physical");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_pierce > 0)
				{
					deathTypes.Add("death by pierce");
					deathTypes.Add("death by physical");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_slash > 0)
				{
					deathTypes.Add("death by slash");
					deathTypes.Add("death by physical");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_fire > 0)
				{
					deathTypes.Add("death by fire");
					deathTypes.Add("death by elemental");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_frost > 0)
				{
					deathTypes.Add("death by frost");
					deathTypes.Add("death by elemental");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_lightning > 0)
				{
					deathTypes.Add("death by lightning");
					deathTypes.Add("death by elemental");
				}
				if (SaveCauseOfDeath.deathHit.m_damage.m_poison > 0)
				{
					deathTypes.Add("death by poison");
					deathTypes.Add("death by elemental");
				}
				if (SaveCauseOfDeath.deathHit.m_skill == Skills.SkillType.WoodCutting)
				{
					deathTypes.Add("death by tree");
				}
				if (SetGravityFlag.fallDamageTaken)
				{
					deathTypes.Add("death by gravity");
				}
				if (SetFreezingFlag.freezingDamageTaken)
				{
					deathTypes.Add("death by frost");
				}

				if (SaveCauseOfDeath.deathHit.GetAttacker() is { } attacker)
				{
					BroadcastDeath(getRandomEnemyText(attacker.GetHoverName(), deathTypes.ToArray()));
				}
				else
				{
					BroadcastDeath(getRandomText(deathTypes.ToArray()));
				}
			}
		}
	}

	[HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.UpdateStatusEffect))]
	public class SetFreezingFlag
	{
		public static bool freezingDamageTaken = false;

		private static void Prefix(SE_Stats __instance)
		{
			if (__instance.name == "Freezing")
			{
				freezingDamageTaken = true;
			}
		}

		private static void Finalizer() => freezingDamageTaken = false;
	}

	[HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
	public class SetGravityFlag
	{
		public static bool fallDamageTaken = false;

		private static void Prefix()
		{
			fallDamageTaken = true;
		}

		private static void Finalizer() => fallDamageTaken = false;
	}

	[HarmonyPatch(typeof(ImpactEffect), nameof(ImpactEffect.OnCollisionEnter))]
	public class SetTreeFlag
	{
		public static bool hitByTree = false;

		private static void Prefix(ImpactEffect __instance)
		{
			if (__instance.GetComponent<TreeLog>())
			{
				hitByTree = true;
			}
		}

		private static void Finalizer() => hitByTree = false;
	}

	[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
	public class HitByTree
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (SetTreeFlag.hitByTree && __instance is Player player && player.GetHealth() > 0)
			{
				hit.m_skill = Skills.SkillType.WoodCutting;
			}
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
	private class SaveCauseOfDeath
	{
		public static HitData? deathHit = null;

		private static void Prefix(HitData hit)
		{
			deathHit = hit;
		}

		private static void Finalizer() => deathHit = null;
	}

	public struct DeathText
	{
		public string category = null!;
		public string text = null!;

		public DeathText() { }
	}

	private static DeathText getRandomText(params string[] categories) => selectRandomText(listAllTexts(categories).Where(t => !t.text.Contains("{enemy}")).ToList());

	private static DeathText getRandomEnemyText(string enemy, params string[] categories)
	{
		DeathText text = selectRandomText(listAllTexts(categories).ToList());
		text.text = text.text.Replace("{enemy}", enemy);
		return text;
	}

	private static List<DeathText> listAllTexts(params string[] categories)
	{
		List<DeathText> possibleTexts = new();
		if (Random.Range(1, 4) == 1 && deathTextDict.TryGetValue("general", out List<string> generalTexts))
		{
			possibleTexts.AddRange(generalTexts.Select(t => new DeathText { category = "general", text = t }));
		}
		foreach (string s in categories)
		{
			if (deathTextDict.TryGetValue(s, out List<string> texts))
			{
				possibleTexts.AddRange(texts.Select(t => new DeathText { category = s, text = t }));
			}
		}

		return possibleTexts.Where(t => t.text != "").ToList();
	}

	private static DeathText selectRandomText(List<DeathText> possibleTexts)
	{
		DeathText text = possibleTexts.Count == 0 ? new DeathText { category = "general", text = "R.I.P. {player}" } : possibleTexts[Random.Range(0, possibleTexts.Count)];
		text.text = text.text.Replace("{player}", Player.m_localPlayer.GetHoverName());
		return text;
	}
}