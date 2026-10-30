using Terraria.Localization;

namespace JustEnoughMods.Core;

/// <summary>UI strings from Localization/*.hjson (Mods.JustEnoughMods.UI.*).</summary>
public static class Loc
{
	public static string T(string key, params object[] args) => Language.GetTextValue("Mods.JustEnoughMods.UI." + key, args);
}
