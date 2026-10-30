using Terraria.Localization;

namespace JustEnoughMods.Core;

public static class Loc
{
	public static bool Ru => Language.ActiveCulture.LegacyId == (int)GameCulture.CultureName.Russian;
}
