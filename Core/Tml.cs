using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria.ModLoader;

namespace JustEnoughMods.Core;

/// <summary>Доступ к внутренностям tModLoader через рефлексию.</summary>
public static class Tml
{
	private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

	private static readonly Assembly Asm = typeof(ModLoader).Assembly;
	public static readonly Type ModOrganizer = Asm.GetType("Terraria.ModLoader.Core.ModOrganizer");
	public static readonly Type Interface = Asm.GetType("Terraria.ModLoader.UI.Interface");
	public static readonly Type LocalMod = Asm.GetType("Terraria.ModLoader.Core.LocalMod");

	public static readonly int ModPacksMenuID = ConstInt(Interface, "modPacksMenuID", 10016);
	public static readonly int ModsMenuID = ConstInt(Interface, "modsMenuID", 10000);

	private static int ConstInt(Type t, string name, int fallback)
		=> t?.GetField(name, Any)?.GetRawConstantValue() is int v ? v : fallback;

	public static string ModPackActive {
		get => (string)ModOrganizer.GetField("ModPackActive", Any).GetValue(null);
		set => ModOrganizer.GetField("ModPackActive", Any).SetValue(null, value);
	}

	public static HashSet<string> EnabledMods => (HashSet<string>)typeof(ModLoader).GetProperty("EnabledMods", Any).GetValue(null);

	public static void SaveEnabledMods() => ModOrganizer.GetMethod("SaveEnabledMods", Any).Invoke(null, null);

	public static void Reload() => typeof(ModLoader).GetMethod("Reload", Any).Invoke(null, null);

	public static void AddOnSuccessfulLoad(Action action)
	{
		var f = typeof(ModLoader).GetField("OnSuccessfulLoad", Any);
		f.SetValue(null, Delegate.Combine((Delegate)f.GetValue(null), action));
	}

	public static string ModFilePath(Mod mod)
	{
		object file = typeof(Mod).GetProperty("File", Any)?.GetValue(mod);
		return TmodPath(file);
	}

	private static string TmodPath(object tmodFile)
	{
		if (tmodFile == null)
			return null;
		var t = tmodFile.GetType();
		return (t.GetField("path", Any)?.GetValue(tmodFile) ?? t.GetProperty("path", Any)?.GetValue(tmodFile)) as string;
	}

	public record FoundMod(string Name, Version Version, string Location, string Path, DateTime LastModified);

	public static FoundMod Describe(object localMod)
	{
		var t = localMod.GetType();
		return new FoundMod(
			(string)t.GetProperty("Name", Any).GetValue(localMod),
			(Version)t.GetProperty("Version", Any).GetValue(localMod),
			t.GetField("location", Any).GetValue(localMod).ToString(),
			TmodPath(t.GetField("modFile", Any).GetValue(localMod)),
			(DateTime)t.GetField("lastModified", Any).GetValue(localMod));
	}

	/// <summary>Все моды на диске, без фильтра менеджера.</summary>
	public static List<FoundMod> FindAllModsRaw()
	{
		Hooks.Bypass = true;
		try {
			var arr = (IEnumerable)ModOrganizer.GetMethod("FindAllMods", Any).Invoke(null, null);
			return arr.Cast<object>().Select(Describe).ToList();
		}
		finally {
			Hooks.Bypass = false;
		}
	}
}
