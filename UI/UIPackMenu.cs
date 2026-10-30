using System;
using Microsoft.Xna.Framework;
using JustEnoughMods.Core;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.UI;

namespace JustEnoughMods.UI;

/// <summary>Меню «Сборки модов» вместо стандартного. Разметка как у выбора персонажа.</summary>
public class UIPackMenu : UIState
{
	public static readonly Color PanelColor = new Color(33, 43, 79) * 0.8f;
	public static readonly Color ButtonColor = new Color(63, 82, 151) * 0.7f;
	public static readonly Color ButtonHoverColor = new Color(73, 94, 171);

	private UIList _list;
	private UIText _status;
	private DateTime _statusUntil;

	public override void OnInitialize()
	{
		var root = new UIElement {
			Width = { Percent = 0.8f },
			Top = { Pixels = 220 }, Height = { Pixels = -220, Percent = 1f },
			HAlign = 0.5f
		};
		root.MaxWidth.Set(650f, 0f); // как у меню персонажей
		Append(root);

		var panel = new UIPanel {
			Width = { Percent = 1f }, Height = { Pixels = -145, Percent = 1f },
			BackgroundColor = PanelColor
		};
		root.Append(panel);

		_list = new UIList { Width = { Pixels = -25, Percent = 1f }, Height = { Percent = 1f }, ListPadding = 5 };
		panel.Append(_list);
		var scroll = new UIScrollbar { Height = { Percent = 1f }, HAlign = 1f };
		scroll.SetView(100f, 1000f);
		_list.SetScrollbar(scroll);
		panel.Append(scroll);

		var header = new UITextPanel<string>(Loc.Ru ? "Сборки модов" : "Mod Packs", 0.8f, true) {
			HAlign = 0.5f, Top = { Pixels = -40 }, BackgroundColor = new Color(73, 94, 171)
		};
		header.SetPadding(15);
		root.Append(header);

		_status = new UIText("", 0.9f) { Top = { Pixels = -132, Percent = 1f }, HAlign = 0.5f };
		root.Append(_status);

		var back = MakeButton(Loc.Ru ? "Назад" : "Back", 0f);
		back.OnLeftClick += (_, _) => Back();
		root.Append(back);

		var create = MakeButton(Loc.Ru ? "Новый" : "New", 1f);
		create.OnLeftClick += (_, _) => {
			SoundEngine.PlaySound(SoundID.MenuOpen);
			Main.MenuUI.SetState(new UINewPackDialog(this));
		};
		root.Append(create);
	}

	public static UITextPanel<string> MakeButton(string text, float hAlign)
	{
		var b = new UITextPanel<string>(text, 0.7f, true) {
			Width = { Pixels = -10, Percent = 0.5f }, Height = { Pixels = 50 },
			VAlign = 1f, HAlign = hAlign, Top = { Pixels = -45 }
		};
		Hover(b);
		return b;
	}

	public static void Hover(UIPanel b)
	{
		b.BackgroundColor = ButtonColor;
		b.OnMouseOver += (_, el) => { SoundEngine.PlaySound(SoundID.MenuTick); ((UIPanel)el).BackgroundColor = ButtonHoverColor; };
		b.OnMouseOut += (_, el) => ((UIPanel)el).BackgroundColor = ButtonColor;
	}

	public override void OnActivate()
	{
		if (!PackStore.Pending) // иначе потеряем несохранённые переключения
			PackStore.LoadAll();
		Refresh();
	}

	public void Refresh()
	{
		if (_list == null)
			return;
		_list.Clear();
		foreach (var p in PackStore.Packs)
			_list.Add(new UIPackItem(p, this));
		_list.UpdateOrder();
		_list.Recalculate();
	}

	public void SetStatus(string text, bool error = false)
	{
		_status.SetText(text);
		_status.TextColor = error ? new Color(255, 120, 100) : Color.White;
		_statusUntil = DateTime.Now.AddSeconds(5);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (_statusUntil != default && DateTime.Now > _statusUntil) {
			_status.SetText("");
			_statusUntil = default;
		}
	}

	// ---------- действия карточек ----------

	public void AskRename(Pack pack) => AskText(Loc.Ru ? "Название сборки" : "Pack name", pack.Name, name => {
		pack.Name = name;
		pack.Save();
	});

	public void AskIcon(Pack pack) => AskText(Loc.Ru ? "Предмет: название или ID" : "Item: name or ID", "", text => {
		int id = FindItem(text);
		if (id <= 0) {
			SetStatus(Loc.Ru ? "Предмет не найден" : "Item not found", true);
			return;
		}
		pack.Icon = ItemID.Search.GetName(id);
		pack.Save();
	});

	private static int FindItem(string text)
	{
		text = text.Trim();
		if (int.TryParse(text, out int num))
			return num > 0 && num < Terraria.ModLoader.ItemLoader.ItemCount ? num : 0;
		if (ItemID.Search.TryGetId(text, out int byInternal))
			return byInternal;
		int contains = 0;
		for (int i = 1; i < Terraria.ModLoader.ItemLoader.ItemCount; i++) {
			string name = Lang.GetItemNameValue(i);
			if (string.IsNullOrEmpty(name))
				continue;
			if (name.Equals(text, StringComparison.CurrentCultureIgnoreCase))
				return i;
			if (contains == 0 && name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
				contains = i;
		}
		return contains;
	}

	public void Export(Pack pack)
	{
		HostShell.SaveZipAsync(Loc.Ru ? "Экспорт сборки" : "Export pack", PackStore.SafeFileName(pack.Name) + ".zip", path => {
			if (path == null)
				return;
			SetStatus(Loc.Ru ? "Экспорт…" : "Exporting…");
			System.Threading.Tasks.Task.Run(() => { // сотни МБ — не держим игру
				try {
					PackStore.Export(pack, path);
					Main.QueueMainThreadAction(() => SetStatus(Loc.Ru ? "Сборка экспортирована" : "Pack exported"));
				}
				catch (Exception e) {
					JemMod.Instance.Logger.Error("Экспорт " + path, e);
					Main.QueueMainThreadAction(() => SetStatus(Loc.Ru ? "Ошибка экспорта" : "Export failed", true));
				}
			});
		});
	}

	public void Update(Pack pack)
	{
		var updated = PackStore.UpdateFromDisk(pack);
		if (updated.Count == 0) {
			SetStatus(Loc.Ru ? "Обновлений нет" : "No updates");
			return;
		}
		SetStatus((Loc.Ru ? "Обновлено модов: " : "Mods updated: ") + updated.Count);
		if (PackStore.IsActive(pack))
			PackStore.Pending = true;
	}

	public void AskDelete(Pack pack)
	{
		SoundEngine.PlaySound(SoundID.MenuOpen);
		Main.MenuUI.SetState(new UIConfirmDelete(pack.Name, () => {
			PackStore.Delete(pack);
			Main.menuMode = Tml.ModPacksMenuID;
		}));
	}

	public void ImportFromFile()
	{
		HostShell.PickZipAsync(Loc.Ru ? "Импорт сборки" : "Import pack", path => {
			Main.menuMode = Tml.ModPacksMenuID;
			if (path == null)
				return;
			try {
				var pack = PackStore.ImportZip(path);
				SetStatus((Loc.Ru ? "Импортировано: " : "Imported: ") + pack.Name);
			}
			catch (Exception e) {
				JemMod.Instance.Logger.Error("Импорт " + path, e);
				SetStatus(Loc.Ru ? "Ошибка импорта" : "Import failed", true);
			}
		});
	}

	public void CreateNew() => AskText(Loc.Ru ? "Название сборки" : "Pack name", "", name => PackStore.Create(name));

	// ---------- ввод текста ----------

	private static void AskText(string label, string start, Action<string> submit)
	{
		Main.MenuUI.SetState(new UIVirtualKeyboard(label, start, text => {
			text = text?.Trim();
			if (!string.IsNullOrEmpty(text))
				submit(text);
			Main.menuMode = Tml.ModPacksMenuID;
		}, () => Main.menuMode = Tml.ModPacksMenuID));
		Main.menuMode = 888;
	}

	private void Back()
	{
		if (PackStore.Pending) {
			SoundEngine.PlaySound(SoundID.MenuOpen);
			Main.MenuUI.SetState(new UIConfirmReload());
			return;
		}
		Leave();
	}

	public static void Leave()
	{
		SoundEngine.PlaySound(SoundID.MenuClose);
		var prev = Hooks.PreviousOfStandardPacksMenu();
		if (prev != null) {
			Main.MenuUI.SetState(prev);
			Main.menuMode = 888;
		}
		else
			Main.menuMode = 0;
	}
}
