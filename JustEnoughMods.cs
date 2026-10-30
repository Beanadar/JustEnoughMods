using System;
using JustEnoughMods.Core;
using JustEnoughMods.UI;
using Terraria;
using Terraria.ModLoader;

namespace JustEnoughMods;

public class JemMod : Mod
{
	public static JemMod Instance { get; private set; }

	private static UIPackMenu _menu;
	public static UIPackMenu Menu => _menu ??= new UIPackMenu();

	public override void Load()
	{
		Instance = this;
		if (Main.dedServ)
			return;

		PackStore.LoadAll();
		PackStore.CleanupOldActiveDirs();
		Hooks.Apply();
		Main.instance.Exiting += OnExiting;

		if (PackStore.State.ReturnToPacks) {
			PackStore.State.ReturnToPacks = false;
			PackStore.SaveState();
			Tml.AddOnSuccessfulLoad(() => Main.menuMode = Tml.ModPacksMenuID);
		}
	}

	public override void PostSetupContent()
	{
		if (Main.dedServ)
			return;
		try {
			PackStore.MigrateAndEnsureDefault();
			PackStore.SyncFromLoadedMods();
		}
		catch (Exception e) {
			Logger.Error("Failed to sync packs", e);
		}
		Hooks.ApplyTexts();
		Terraria.Localization.LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
	}

	private static void OnLanguageChanged(Terraria.Localization.LanguageManager _) => Hooks.ApplyTexts();

	private static void OnExiting(object sender, EventArgs e) => PackStore.FlushActiveIfDirty();

	public override void Unload()
	{
		if (!Main.dedServ) {
			Main.instance.Exiting -= OnExiting;
			Terraria.Localization.LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
			PackStore.FlushActiveIfDirty(); // mods are unloaded; the next load picks up the new active folder
		}
		Hooks.Unapply();
		_menu = null;
		Instance = null;
	}
}

/// <summary>Play time of the active packs.</summary>
public class PlayTimeSystem : ModSystem
{
	private DateTime? _start;

	public override void OnWorldLoad() => _start = DateTime.Now;

	public override void OnWorldUnload()
	{
		if (_start == null)
			return;
		double seconds = (DateTime.Now - _start.Value).TotalSeconds;
		_start = null;
		foreach (var pack in PackStore.ActivePacks) {
			pack.PlaySeconds += seconds;
			pack.Save();
		}
	}
}
