using System;
using Microsoft.Xna.Framework;
using JustEnoughMods.Core;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace JustEnoughMods.UI;

/// <summary>«Новый»: маленькое окно с выбором — создать или импортировать.</summary>
public class UINewPackDialog : UIState
{
	private readonly UIPackMenu _menu;

	public UINewPackDialog(UIPackMenu menu) => _menu = menu;

	public override void OnInitialize()
	{
		var root = new UIElement {
			Width = { Pixels = 400 }, Height = { Pixels = 260 },
			HAlign = 0.5f, VAlign = 0.5f
		};
		Append(root);

		var panel = new UIPanel {
			Width = { Percent = 1f }, Height = { Pixels = 136 },
			BackgroundColor = UIPackMenu.PanelColor
		};
		root.Append(panel);

		var header = new UITextPanel<string>(Loc.Ru ? "Новая сборка" : "New pack", 0.8f, true) {
			HAlign = 0.5f, Top = { Pixels = -35 }, BackgroundColor = new Color(73, 94, 171)
		};
		header.SetPadding(15);
		root.Append(header);

		AddOption(panel, Loc.Ru ? "Создать" : "Create", 8, () => _menu.CreateNew());
		AddOption(panel, Loc.Ru ? "Импортировать из файла" : "Import from file", 60, () => _menu.ImportFromFile());

		var back = new UITextPanel<string>(Loc.Ru ? "Назад" : "Back", 0.7f, true) {
			Width = { Percent = 1f }, Height = { Pixels = 50 }, Top = { Pixels = 151 }
		};
		UIPackMenu.Hover(back);
		back.OnLeftClick += (_, _) => {
			SoundEngine.PlaySound(SoundID.MenuClose);
			Main.menuMode = Tml.ModPacksMenuID;
		};
		root.Append(back);
	}

	private static void AddOption(UIElement parent, string text, float top, Action click)
	{
		var b = new UITextPanel<string>(text, 0.9f) {
			Width = { Percent = 1f }, Height = { Pixels = 44 }, Top = { Pixels = top } // отступы равные, с учётом шапки
		};
		UIPackMenu.Hover(b);
		b.OnLeftClick += (_, _) => { SoundEngine.PlaySound(SoundID.MenuOpen); click(); };
		parent.Append(b);
	}
}

/// <summary>Подтверждение удаления — как у персонажа: «Удалить X?», Да / Нет.</summary>
public class UIConfirmDelete : UIState
{
	private readonly string _name;
	private readonly Action _onYes;

	public UIConfirmDelete(string name, Action onYes)
	{
		_name = name;
		_onYes = onYes;
	}

	public override void OnInitialize()
	{
		var title = new UIText((Loc.Ru ? "Удалить " : "Delete ") + _name + "?", 1f, true) { HAlign = 0.5f, Top = { Pixels = 305 } };
		Append(title);
		Append(new MenuLink(Lang.menu[104].Value, 400, () => _onYes()));
		Append(new MenuLink(Lang.menu[105].Value, 495, () => {
			SoundEngine.PlaySound(SoundID.MenuClose);
			Main.menuMode = Tml.ModPacksMenuID;
		}));
	}
}

/// <summary>Пункт как в ванильном меню: серый, при наведении светлеет и чуть растёт.</summary>
public class MenuLink : UIText
{
	private bool _hover, _shownHover;

	public MenuLink(string text, float top, Action click) : base(text, 1f, true)
	{
		HAlign = 0.5f;
		Top.Set(top, 0f);
		TextColor = new Color(150, 150, 150);
		OnMouseOver += (_, _) => { _hover = true; SoundEngine.PlaySound(SoundID.MenuTick); };
		OnMouseOut += (_, _) => _hover = false;
		OnLeftClick += (_, _) => click();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (_hover == _shownHover)
			return;
		_shownHover = _hover;
		TextColor = _hover ? new Color(255, 240, 200) : new Color(150, 150, 150);
		SetText(Text, _hover ? 1.08f : 1f, true);
	}
}

/// <summary>Выход из меню сборок с изменениями: «нужно перезагрузить моды», ОК / Отмена.</summary>
public class UIConfirmReload : UIState
{
	public override void OnInitialize()
	{
		Append(new UIText(Loc.Ru ? "Перезагрузить моды?" : "Reload mods?", 1f, true) { HAlign = 0.5f, Top = { Pixels = 305 } });
		Append(new UIText(Loc.Ru ? "Изменения вступят в силу после перезагрузки" : "Changes take effect after reloading", 1f) { HAlign = 0.5f, Top = { Pixels = 360 } });
		Append(new MenuLink(Loc.Ru ? "ОК" : "OK", 410, () => {
			SoundEngine.PlaySound(SoundID.MenuOpen);
			PackStore.ApplyAndReload(returnToPacks: false);
		}));
		Append(new MenuLink(Loc.Ru ? "Отмена" : "Cancel", 505, () => {
			SoundEngine.PlaySound(SoundID.MenuClose);
			Main.menuMode = Tml.ModPacksMenuID;
		}));
	}
}
