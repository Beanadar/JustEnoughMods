using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria.ModLoader;
using Terraria.UI;

namespace JustEnoughMods.Core;

public static class Hooks
{
	private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

	/// <summary>Временно выключить фильтр (когда менеджеру нужен полный список модов).</summary>
	public static bool Bypass;

	public static void Apply()
	{
		var findAllMods = Tml.ModOrganizer.GetMethod("FindAllMods", Any);
		MonoModHooks.Modify(findAllMods, FilterFoundMods);

		// Стандартное меню сборок открывают по-разному (menuMode, прямой SetState) — ловим всё в SetState.
		On_UserInterface.SetState += SwapPacksMenu;

		// Надпись «Текущая сборка модов: _active» внизу меню — служебная, прячем.
		var drawVersion = typeof(Terraria.Main).GetMethod("DrawVersionNumber", Any);
		MonoModHooks.Modify(drawVersion, HideStdModPackLabel);

		// Моды из сборки в списке модов — обычным синим, а не голубым (откуда загружен, и так видно по значку)
		var modItem = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.UI.UIModItem");
		MonoModHooks.Modify(modItem.GetMethod("SetHoverColors", Any), DefaultColorForModpackMods);

	}

	/// <summary>
	/// Подмена текстов tML. tML перечитывает локализацию после Load модов и при смене языка,
	/// поэтому вызывается из PostSetupContent и на OnLanguageChanged.
	/// </summary>
	public static void ApplyTexts()
	{
		ReplaceText("tModLoader.MenuModPackDescription", Loc.Ru ? "Переключение, создание, импорт и экспорт сборок модов." : "Switch, create, import and export mod packs.");
	}

	public static void Unapply()
	{
		foreach (var (text, old) in _replacedTexts)
			SetText(text, old);
		_replacedTexts.Clear();
	}

	// ---------- надпись внизу меню ----------

	// `if (ModOrganizer.ModPackActive != null) { ... }` → условие всегда ложно
	private static void HideStdModPackLabel(ILContext il)
	{
		var c = new ILCursor(il);
		if (!c.TryGotoNext(MoveType.After, i => i.MatchLdsfld(Tml.ModOrganizer, "ModPackActive"))) {
			JemMod.Instance.Logger.Warn("Не найдена стандартная надпись о сборке");
			return;
		}
		c.Emit(OpCodes.Pop);
		c.Emit(OpCodes.Ldnull);
	}

	private static void DefaultColorForModpackMods(ILContext il)
	{
		var c = new ILCursor(il);
		while (c.TryGotoNext(MoveType.Before, i => i.MatchRet())) {
			c.Emit(OpCodes.Ldarg_0);
			c.Emit(OpCodes.Ldarg_1);
			c.Emit(OpCodes.Call, typeof(Hooks).GetMethod(nameof(FixModItemColor), Any));
			c.Index++;
		}
	}

	private static FieldInfo _modItemMod;

	public static void FixModItemColor(object item, bool hovered)
	{
		_modItemMod ??= item.GetType().GetField("_mod", Any);
		object localMod = _modItemMod?.GetValue(item);
		if (localMod == null || Tml.Describe(localMod).Location != "Modpack")
			return;
		((Terraria.GameContent.UI.Elements.UIPanel)item).BackgroundColor =
			hovered ? Terraria.ModLoader.UI.UICommon.DefaultUIBlueMouseOver : Terraria.ModLoader.UI.UICommon.DefaultUIBlue;
	}

	// ---------- подмена текстов tML ----------

	private static readonly List<(Terraria.Localization.LocalizedText, string)> _replacedTexts = new();

	private static void ReplaceText(string key, string value)
	{
		var text = Terraria.Localization.Language.GetText(key);
		if (!_replacedTexts.Exists(r => r.Item1 == text))
			_replacedTexts.Add((text, text.Value));
		SetText(text, value);
	}

	private static void SetText(Terraria.Localization.LocalizedText text, string value)
		=> typeof(Terraria.Localization.LocalizedText).GetMethod("SetValue", Any)?.Invoke(text, new object[] { value });

	private static readonly Type StdPacksMenuType = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.UI.UIModPacks");

	private static void SwapPacksMenu(On_UserInterface.orig_SetState orig, UserInterface self, UIState state)
	{
		if (state != null && state.GetType() == StdPacksMenuType)
			state = JemMod.Menu;
		orig(self, state);
	}

	// ModOrganizer.FindAllMods: `return AllFoundMods = mods.ToArray();` → фильтруем массив после ToArray.
	private static void FilterFoundMods(ILContext il)
	{
		var c = new ILCursor(il);
		c.Index = c.Instrs.Count - 1;
		if (!c.TryGotoPrev(MoveType.After, i => i.MatchCallOrCallvirt(out var m) && m.Name == "ToArray"))
			throw new Exception("JustEnoughMods: не нашла ToArray в FindAllMods");
		c.Emit(OpCodes.Call, typeof(Hooks).GetMethod(nameof(Filter), Any));
		c.Emit(OpCodes.Castclass, Tml.LocalMod.MakeArrayType());
	}

	public static object Filter(object found)
	{
		if (Bypass)
			return found;
		try {
			var visible = PackStore.VisibleNames();
			if (visible == null)
				return found;

			var since = PackStore.State.ActivatedAt;
			var keep = new List<object>();
			foreach (object m in (Array)found) {
				var d = Tml.Describe(m);
				if (d.Location == "Modpack" || visible.Contains(d.Name) || d.LastModified > since)
					keep.Add(m);
			}
			var result = Array.CreateInstance(Tml.LocalMod, keep.Count);
			for (int i = 0; i < keep.Count; i++)
				result.SetValue(keep[i], i);
			return result;
		}
		catch (Exception e) {
			JemMod.Instance?.Logger.Error("Фильтр модов упал, показываю всё", e);
			return found;
		}
	}

	public static UIState GetPacksMenu() => JemMod.Menu;

	/// <summary>Куда возвращаться из меню сборок — туда же, куда вернулось бы стандартное.</summary>
	public static UIState PreviousOfStandardPacksMenu()
	{
		object std = Tml.Interface.GetField("modPacksMenu", Any)?.GetValue(null);
		return std?.GetType().GetProperty("PreviousUIState", Any)?.GetValue(std) as UIState;
	}
}
