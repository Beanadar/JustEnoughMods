using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using JustEnoughMods.Core;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.UI;
using Terraria.UI;

namespace JustEnoughMods.UI;

/// <summary>Карточка сборки — по мотивам карточки персонажа.</summary>
public class UIPackItem : UIPanel
{
	public readonly Pack Pack;
	private readonly UIPackMenu _menu;
	private readonly bool _active;
	private string _tooltip;
	private readonly UIImageButton _deleteButton;
	private readonly UIPackIcon _icon;
	private readonly string _countText, _timeText;

	private static Asset<Texture2D> Vanilla(string name) => Main.Assets.Request<Texture2D>("Images/UI/" + name, AssetRequestMode.ImmediateLoad);
	private static Asset<Texture2D> Own(string name) => ModContent.Request<Texture2D>("JustEnoughMods/Assets/" + name, AssetRequestMode.ImmediateLoad); // размер нужен сразу для разметки

	public UIPackItem(Pack pack, UIPackMenu menu)
	{
		Pack = pack;
		_menu = menu;
		_active = PackStore.IsActive(pack);

		Width.Set(0, 1f);
		Height.Set(96, 0f);
		SetPadding(6);
		// Как у персонажа: подсветка при наведении, двойной клик по пустому месту — вкл/выкл
		BorderColor = new Color(89, 116, 213) * 0.7f;
		BackgroundColor = new Color(63, 82, 151) * 0.7f;
		OnMouseOver += (_, _) => { BackgroundColor = new Color(73, 94, 171); BorderColor = new Color(89, 116, 213); };
		OnMouseOut += (_, _) => { BackgroundColor = new Color(63, 82, 151) * 0.7f; BorderColor = new Color(89, 116, 213) * 0.7f; };
		OnLeftDoubleClick += (evt, el) => {
			if (evt.Target == el) // не по кнопкам и иконке
				TogglePlay();
		};

		// Иконка: клик — выбрать предмет
		_icon = new UIPackIcon(pack) { Left = { Pixels = 4 } };
		_icon.OnLeftClick += (_, _) => _menu.AskIcon(Pack);
		_icon.OnMouseOver += (_, _) => _tooltip = Loc.Ru ? "Сменить иконку" : "Change icon";
		_icon.OnMouseOut += (_, _) => _tooltip = null;
		Append(_icon);

		int total = pack.ModFiles().Length;
		int on = _active ? pack.Enabled.Count : 0;
		_countText = (Loc.Ru ? "Активно " : "Active ") + $"{on}/{total}";
		var t = TimeSpan.FromSeconds(pack.PlaySeconds);
		_timeText = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

		// Нижний ряд кнопок — под иконкой, как у персонажа
		float x = 4;
		AddButton(_active ? Own("ButtonStop") : Vanilla("ButtonPlay"), ref x, _active ? (Loc.Ru ? "Отключить" : "Disable") : (Loc.Ru ? "Включить" : "Enable"), TogglePlay);
		AddButton(Vanilla(pack.Favorite ? "ButtonFavoriteActive" : "ButtonFavoriteInactive"), ref x,
			pack.Favorite ? (Loc.Ru ? "Убрать из избранного" : "Unfavorite") : (Loc.Ru ? "В избранное" : "Favorite"), ToggleFavorite);
		AddButton(Vanilla("ButtonRename"), ref x, Loc.Ru ? "Переименовать" : "Rename", () => _menu.AskRename(Pack));
		AddButton(Own("ButtonExport"), ref x, Loc.Ru ? "Экспортировать как файл" : "Export as file", () => _menu.Export(Pack));

		// Справа: обновить сверху, удалить снизу
		var update = new UIImageButton(Own("ButtonUpdate")) { HAlign = 1f, Top = { Pixels = -2 } }; // по строке названия
		update.SetVisibility(1f, 0.6f);
		update.OnLeftClick += (_, _) => _menu.Update(Pack);
		update.OnMouseOver += (_, _) => _tooltip = Loc.Ru ? "Обновить моды" : "Update mods";
		update.OnMouseOut += (_, _) => _tooltip = null;
		Append(update);

		_deleteButton = new UIImageButton(Vanilla("ButtonDelete")) { HAlign = 1f, VAlign = 1f };
		_deleteButton.SetVisibility(1f, 0.6f);
		_deleteButton.OnLeftClick += (_, _) => _menu.AskDelete(Pack);
		_deleteButton.OnMouseOver += (_, _) => _tooltip = Loc.Ru ? "Удалить" : "Delete";
		_deleteButton.OnMouseOut += (_, _) => _tooltip = null;
		Append(_deleteButton);
	}

	private void AddButton(Asset<Texture2D> tex, ref float x, string tooltip, Action click)
	{
		var b = new UIImageButton(tex) { Left = { Pixels = x }, VAlign = 1f };
		b.SetVisibility(1f, 0.6f);
		b.OnLeftClick += (_, _) => click();
		b.OnMouseOver += (_, _) => _tooltip = tooltip;
		b.OnMouseOut += (_, _) => _tooltip = null;
		Append(b);
		x += 24;
	}

	private void TogglePlay()
	{
		SoundEngine.PlaySound(SoundID.MenuOpen);
		if (_active)
			PackStore.State.Active.Remove(Pack.Id);
		else
			PackStore.State.Active.Add(Pack.Id);
		PackStore.Pending = true; // применится при выходе из меню
		_menu.Refresh();
	}

	private void ToggleFavorite()
	{
		SoundEngine.PlaySound(SoundID.MenuTick);
		Pack.Favorite = !Pack.Favorite;
		Pack.Save();
		_menu.Refresh();
	}

	public override int CompareTo(object obj)
	{
		if (obj is not UIPackItem other)
			return base.CompareTo(obj);
		if (_active != other._active)
			return _active ? -1 : 1;
		if (Pack.Favorite != other.Pack.Favorite)
			return Pack.Favorite ? -1 : 1;
		return string.Compare(Pack.Name, other.Pack.Name, StringComparison.CurrentCultureIgnoreCase);
	}

	// Как UICharacterListItem: имя, разделитель, плашки InnerPanelBackground
	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		base.DrawSelf(spriteBatch);
		var inner = GetInnerDimensions();
		var icon = _icon.GetDimensions();
		float left = icon.X + icon.Width;
		float right = GetDimensions().X + GetDimensions().Width;

		Utils.DrawBorderString(spriteBatch, Pack.Name, new Vector2(left + 6f, icon.Y - 2f), Color.White);
		spriteBatch.Draw(Vanilla("Divider").Value, new Vector2(left, inner.Y + 21f), null, Color.White, 0f, Vector2.Zero,
			new Vector2((right - left) / 8f, 1f), SpriteEffects.None, 0f);

		var pos = new Vector2(left + 6f, inner.Y + 29f);
		DrawInfoPanel(spriteBatch, pos, 200f, _countText);
		float timeWidth = 150f;
		DrawInfoPanel(spriteBatch, new Vector2(inner.X + inner.Width - timeWidth, pos.Y), timeWidth, _timeText);
	}

	private static void DrawInfoPanel(SpriteBatch sb, Vector2 pos, float width, string text)
	{
		var tex = Vanilla("InnerPanelBackground").Value;
		int h = tex.Height;
		sb.Draw(tex, pos, new Rectangle(0, 0, 8, h), Color.White);
		sb.Draw(tex, new Vector2(pos.X + 8f, pos.Y), new Rectangle(8, 0, 8, h), Color.White, 0f, Vector2.Zero, new Vector2((width - 16f) / 8f, 1f), SpriteEffects.None, 0f);
		sb.Draw(tex, new Vector2(pos.X + width - 8f, pos.Y), new Rectangle(16, 0, 8, h), Color.White);
		float w = Terraria.GameContent.FontAssets.MouseText.Value.MeasureString(text).X;
		Utils.DrawBorderString(sb, text, pos + new Vector2(width * 0.5f - w * 0.5f, 3f), Color.White);
	}

	public override void Draw(SpriteBatch spriteBatch)
	{
		base.Draw(spriteBatch);
		if (_tooltip != null)
			UICommon.TooltipMouseText(_tooltip);
	}
}

/// <summary>Иконка сборки: рамка как у персонажа, внутри предмет. @tree — дерево, как у иконки миров.</summary>
public class UIPackIcon : UIElement
{
	private readonly Pack _pack;
	private static Asset<Texture2D> Frame => Main.Assets.Request<Texture2D>("Images/UI/PlayerBackground", AssetRequestMode.ImmediateLoad);
	private static Asset<Texture2D> Tree => Main.Assets.Request<Texture2D>("Images/UI/Workshop/HubWorlds", AssetRequestMode.ImmediateLoad);

	public UIPackIcon(Pack pack)
	{
		_pack = pack;
		var f = Frame.Value;
		Width.Set(f.Width, 0f);
		Height.Set(f.Height, 0f);
	}

	public static int ResolveItem(string icon)
		=> !string.IsNullOrEmpty(icon) && icon != Pack.TreeIcon && ItemID.Search.TryGetId(icon, out int id) ? id : 0;

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var dims = GetDimensions();
		var pos = dims.Position();

		if (_pack.Icon == Pack.TreeIcon || ResolveItem(_pack.Icon) <= 0) {
			var tree = Tree.Value;
			spriteBatch.Draw(tree, dims.Center(), null, Color.White, 0f, tree.Size() / 2f, 1f, SpriteEffects.None, 0f);
			return;
		}

		spriteBatch.Draw(Frame.Value, pos, Color.White);
		int id = ResolveItem(_pack.Icon);
		Main.instance.LoadItem(id);
		var tex = TextureAssets.Item[id].Value;
		var frame = Main.itemAnimations[id] != null ? Main.itemAnimations[id].GetFrame(tex) : tex.Frame();
		float scale = Math.Min(1f, 40f / Math.Max(frame.Width, frame.Height));
		spriteBatch.Draw(tex, dims.Center(), frame, Color.White, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
	}
}
