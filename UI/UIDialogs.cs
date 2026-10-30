using System;
using Microsoft.Xna.Framework;
using JustEnoughMods.Core;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.UI;

namespace JustEnoughMods.UI;

/// <summary>"New": small dialog to create or import a pack.</summary>
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

		var header = new UITextPanel<string>(Loc.T("NewPack"), 0.8f, true) {
			HAlign = 0.5f, Top = { Pixels = -35 }, BackgroundColor = new Color(73, 94, 171)
		};
		header.SetPadding(15);
		root.Append(header);

		AddOption(panel, Loc.T("Create"), 8, () => _menu.CreateNew());
		AddOption(panel, Loc.T("ImportFromFile"), 60, () => _menu.ImportFromFile());

		var back = new UITextPanel<string>(Loc.T("Back"), 0.7f, true) {
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
			Width = { Percent = 1f }, Height = { Pixels = 44 }, Top = { Pixels = top } // even spacing, accounting for the header
		};
		UIPackMenu.Hover(b);
		b.OnLeftClick += (_, _) => { SoundEngine.PlaySound(SoundID.MenuOpen); click(); };
		parent.Append(b);
	}
}

/// <summary>Delete confirmation, like the character one: "Delete X?", Yes / No.</summary>
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
		var title = new UIText(Loc.T("DeleteConfirm", _name), 1f, true) { HAlign = 0.5f, Top = { Pixels = 305 } };
		Append(title);
		Append(new MenuLink(Lang.menu[104].Value, 400, () => _onYes()));
		Append(new MenuLink(Lang.menu[105].Value, 495, () => {
			SoundEngine.PlaySound(SoundID.MenuClose);
			Main.menuMode = Tml.ModPacksMenuID;
		}));
	}
}

/// <summary>Vanilla-style menu entry: gray, brightens and grows slightly on hover.</summary>
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

/// <summary>Leaving the packs menu with changes: reload prompt, OK / Cancel.</summary>
public class UIConfirmReload : UIState
{
	public override void OnInitialize()
	{
		Append(new UIText(Loc.T("ReloadMods"), 1f, true) { HAlign = 0.5f, Top = { Pixels = 305 } });
		Append(new UIText(Loc.T("ChangesTakeEffectAfterReloading"), 1f) { HAlign = 0.5f, Top = { Pixels = 360 } });
		Append(new MenuLink(Loc.T("Ok"), 410, () => {
			SoundEngine.PlaySound(SoundID.MenuOpen);
			PackStore.ApplyAndReload(returnToPacks: false);
		}));
		Append(new MenuLink(Loc.T("Cancel"), 505, () => {
			SoundEngine.PlaySound(SoundID.MenuClose);
			Main.menuMode = Tml.ModPacksMenuID;
		}));
	}
}
