using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Terraria;

namespace JustEnoughMods.Core;

public class Pack
{
	public string Name = "Pack";
	public const string TreeIcon = "@tree";
	public string Icon = TreeIcon;    // имя предмета (ItemID.Search) или @tree
	public bool Favorite;
	public double PlaySeconds;
	public List<string> Enabled = new();

	[JsonIgnore] public string Dir;
	[JsonIgnore] public string Id => Path.GetFileName(Dir);
	[JsonIgnore] public string ModsDir => Path.Combine(Dir, "Mods");

	public string[] ModFiles() => Directory.Exists(ModsDir) ? Directory.GetFiles(ModsDir, "*.tmod") : Array.Empty<string>();
	public HashSet<string> ModNames() => ModFiles().Select(Path.GetFileNameWithoutExtension).ToHashSet();

	public void Save()
	{
		Directory.CreateDirectory(ModsDir);
		File.WriteAllText(Path.Combine(Dir, "pack.json"), JsonConvert.SerializeObject(this, Formatting.Indented));
	}

	public void AddModFile(string sourcePath, string modName, bool enabled)
	{
		Directory.CreateDirectory(ModsDir);
		string dest = Path.Combine(ModsDir, modName + ".tmod");
		if (Path.GetFullPath(sourcePath) != Path.GetFullPath(dest))
			PackStore.SafeCopy(sourcePath, dest);
		if (enabled && !Enabled.Contains(modName))
			Enabled.Add(modName);
	}
}

public class StoreState
{
	public List<string> Active = new();
	/// <summary>Когда менялся набор активных сборок. Моды, скачанные позже, показываются как новые.</summary>
	public DateTime ActivatedAt = DateTime.MinValue;
	/// <summary>После перезагрузки вернуться в меню сборок.</summary>
	public bool ReturnToPacks;
	public bool MigratedTml;
}

public static class PackStore
{
	public const string SelfName = "JustEnoughMods";

	public static string Root => Path.Combine(Main.SavePath, "JustEnoughMods");
	public static string PacksDir => Path.Combine(Root, "Packs");
	/// <summary>Активные моды. Имя папки — то, что tML пишет внизу меню, поэтому оно служебное и не показывается (надпись рисуем сами).</summary>
	public static string ActiveRoot => Path.Combine(Root, "active");

	/// <summary>Папка, из которой tML сейчас берёт моды сборок (или null).</summary>
	public static string CurrentActiveDir => Tml.ModPackActive is string d && d.StartsWith(ActiveRoot) ? d : null;

	/// <summary>
	/// Старые папки активных модов. Загруженные .tmod заняты игрой (под Windows/Wine их не удалить),
	/// поэтому каждая пересборка — новая папка, а старые удаляются, когда освободятся.
	/// </summary>
	public static void CleanupOldActiveDirs()
	{
		foreach (string dir in Directory.Exists(ActiveRoot) ? Directory.GetDirectories(ActiveRoot) : Array.Empty<string>()) {
			if (dir == Tml.ModPackActive)
				continue;
			try { Directory.Delete(dir, true); } catch { }
		}
		string legacy = Path.Combine(Root, "_active");
		if (Directory.Exists(legacy) && legacy != Tml.ModPackActive)
			try { Directory.Delete(legacy, true); } catch { }
	}
	private static string StatePath => Path.Combine(Root, "state.json");

	public static StoreState State { get; private set; } = new();
	public static List<Pack> Packs { get; private set; } = new();

	public static string ActiveNames => string.Join(", ", ActivePacks.Select(p => p.Name));

	public static IEnumerable<Pack> ActivePacks => Packs.Where(p => State.Active.Contains(p.Id));
	public static bool IsActive(Pack p) => State.Active.Contains(p.Id);

	public static void LoadAll()
	{
		Directory.CreateDirectory(PacksDir);
		State = File.Exists(StatePath) ? JsonConvert.DeserializeObject<StoreState>(File.ReadAllText(StatePath)) ?? new() : new();
		Packs = new();
		foreach (string dir in Directory.GetDirectories(PacksDir)) {
			if (File.Exists(Path.Combine(dir, DeleteMarker))) {
				try { Directory.Delete(dir, true); } catch { }
				continue;
			}
			string json = Path.Combine(dir, "pack.json");
			if (!File.Exists(json))
				continue;
			try {
				var p = JsonConvert.DeserializeObject<Pack>(File.ReadAllText(json));
				p.Dir = dir;
				if (p.Icon == Pack.TreeIcon && p.Name != "Default") { // иконку не выбирали — случайный предмет
					p.Icon = RandomItemIcon();
					p.Save();
				}
				Packs.Add(p);
			}
			catch (Exception e) {
				JemMod.Instance?.Logger.Warn($"Битый pack.json: {json}", e);
			}
		}
		State.Active.RemoveAll(id => Packs.All(p => p.Id != id));
	}

	public static void SaveState()
	{
		Directory.CreateDirectory(Root);
		File.WriteAllText(StatePath, JsonConvert.SerializeObject(State, Formatting.Indented));
	}

	public static Pack Create(string name)
	{
		string id = MakeId(name);
		var p = new Pack { Name = name, Dir = Path.Combine(PacksDir, id), Icon = RandomItemIcon() };
		p.Save();
		Packs.Add(p);
		return p;
	}

	/// <summary>Случайный ванильный предмет — иконка, пока игрок не выбрал свою.</summary>
	public static string RandomItemIcon()
	{
		var rnd = new Random();
		for (int tries = 0; tries < 100; tries++) {
			int id = rnd.Next(1, Terraria.ID.ItemID.Count);
			if (!Terraria.ID.ItemID.Sets.Deprecated[id])
				return Terraria.ID.ItemID.Search.GetName(id);
		}
		return Pack.TreeIcon;
	}

	private static string MakeId(string name)
	{
		var sb = new StringBuilder();
		foreach (char c in name)
			sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
		string baseId = sb.Length > 0 ? sb.ToString() : "pack";
		string id = baseId;
		for (int i = 2; Directory.Exists(Path.Combine(PacksDir, id)); i++)
			id = baseId + "_" + i;
		return id;
	}

	/// <summary>Есть изменения (вкл/выкл, удаление активной, обновление), которые применятся перезагрузкой при выходе из меню.</summary>
	public static bool Pending;

	private const string DeleteMarker = "DELETED";

	public static void Delete(Pack p)
	{
		Packs.Remove(p);
		if (State.Active.Remove(p.Id))
			Pending = true;
		try {
			Directory.Delete(p.Dir, true);
		}
		catch {
			// файлы активной сборки заняты игрой — удалим при следующем запуске
			File.WriteAllText(Path.Combine(p.Dir, DeleteMarker), "");
		}
	}

	// ---------- версия .tmod без загрузки мода ----------

	public static Version ReadTmodVersion(string path)
	{
		try {
			using var r = new BinaryReader(File.OpenRead(path));
			r.ReadBytes(4);       // "TMOD"
			r.ReadString();       // версия tML
			r.ReadBytes(20 + 256 + 4); // хэш, подпись, длина данных
			r.ReadString();       // имя
			return Version.Parse(r.ReadString());
		}
		catch {
			return new Version(0, 0);
		}
	}

	// ---------- активация ----------

	/// <summary>Собирает _active из активных сборок (при совпадениях — более новая версия) и включает её в tML. Возвращает моды для enabled.json.</summary>
	private static HashSet<string> BuildActive()
	{
		var chosen = new Dictionary<string, (string path, Version ver)>();
		var enabled = new HashSet<string> { SelfName };
		foreach (var pack in ActivePacks) {
			foreach (string file in pack.ModFiles()) {
				string name = Path.GetFileNameWithoutExtension(file);
				var ver = ReadTmodVersion(file);
				if (!chosen.TryGetValue(name, out var cur) || ver > cur.ver)
					chosen[name] = (file, ver);
			}
			enabled.UnionWith(pack.Enabled);
		}
		enabled.RemoveWhere(n => n != SelfName && !chosen.ContainsKey(n));

		string dir = Path.Combine(ActiveRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
		if (chosen.Count > 0) {
			Directory.CreateDirectory(dir);
			foreach (var (name, (path, _)) in chosen)
				LinkOrCopy(path, Path.Combine(dir, name + ".tmod"));
		}

		Tml.ModPackActive = chosen.Count > 0 ? dir : null;
		Main.SaveSettings();
		State.ActivatedAt = DateTime.Now;
		return enabled;
	}

	/// <summary>
	/// _active надо пересобрать. Делается только между загрузками модов (перезагрузка, выход из игры):
	/// если подменить файлы посреди сессии, tML видит другие пути и перед одиночной игрой требует перезагрузку.
	/// </summary>
	public static bool ActiveDirty;

	public static void FlushActiveIfDirty()
	{
		if (!ActiveDirty)
			return;
		ActiveDirty = false;
		try {
			BuildActive();
			SaveState();
		}
		catch (Exception e) {
			JemMod.Instance?.Logger.Error("Не удалось пересобрать активные моды", e);
		}
	}

	/// <summary>Применяет активные сборки и перезагружает моды.</summary>
	public static void ApplyAndReload(bool returnToPacks = true)
	{
		Pending = false;
		ActiveDirty = false;
		var enabled = BuildActive();
		var em = Tml.EnabledMods;
		em.Clear();
		em.UnionWith(enabled);
		Tml.SaveEnabledMods();

		State.ReturnToPacks = returnToPacks;
		SaveState();
		Tml.Reload();
	}

	/// <summary>
	/// После загрузки: сборки tML → сборки JEM (один раз), и если модов вне сборок включено — сборка Default из них.
	/// </summary>
	public static void MigrateAndEnsureDefault()
	{
		if (!State.MigratedTml) {
			foreach (string dir in ImportCandidatesTml()) {
				string name = Path.GetFileName(dir);
				if (Packs.Any(p => p.Name == name))
					continue;
				var (pack, missing) = ImportTml(dir);
				JemMod.Instance.Logger.Info($"Сборка tML «{name}» перенесена" + (missing.Count > 0 ? $", нет на диске: {string.Join(", ", missing)}" : ""));
			}
			State.MigratedTml = true;
			SaveState();
		}

		if (ActivePacks.Any()) {
			// _active потерялась или пустая (например, после сбоя) — пересобрать, применится при следующем запуске
			var expected = ActivePacks.SelectMany(p => p.ModNames()).ToHashSet();
			string cur = CurrentActiveDir;
			var present = cur != null && Directory.Exists(cur) ? Directory.GetFiles(cur, "*.tmod").Select(Path.GetFileNameWithoutExtension).ToHashSet() : new HashSet<string>();
			if (!expected.SetEquals(present))
				ActiveDirty = true;
			return;
		}
		bool anyMods = Terraria.ModLoader.ModLoader.Mods.Any(m => m.Name != "ModLoader" && m.Name != SelfName);
		if (!anyMods)
			return;

		var def = Packs.FirstOrDefault(p => p.Name == "Default") ?? Create("Default");
		def.Icon = Pack.TreeIcon;
		foreach (var mod in Terraria.ModLoader.ModLoader.Mods) {
			if (mod.Name == "ModLoader" || mod.Name == SelfName)
				continue;
			string path = Tml.ModFilePath(mod);
			if (path != null && File.Exists(path))
				def.AddModFile(path, mod.Name, true);
		}
		def.Save();
		State.Active.Add(def.Id);
		SaveState();
		ActiveDirty = true;
		JemMod.Instance.Logger.Info("Создана сборка Default из включённых модов");
	}

	/// <summary>Копия через временный файл: загруженный мод может читать старый файл, его нельзя перезаписывать на месте.</summary>
	public static void SafeCopy(string src, string dest)
	{
		string tmp = dest + ".pmtmp";
		File.Copy(src, tmp, true);
		File.Move(tmp, dest, true);
	}

	[System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateHardLinkW(string newFile, string existingFile, IntPtr security);

	[System.Runtime.InteropServices.DllImport("libc", EntryPoint = "link", SetLastError = true)]
	private static extern int UnixLink(string oldPath, string newPath);

	/// <summary>Жёсткая ссылка (мгновенно, без лишнего места), иначе копия. Результат всегда проверяется: под Wine ссылки бывают «успешными», но пустыми.</summary>
	private static void LinkOrCopy(string src, string dest)
	{
		long size = new FileInfo(src).Length;
		bool Ok() => File.Exists(dest) && new FileInfo(dest).Length == size;
		try {
			if (OperatingSystem.IsWindows())
				CreateHardLinkW(dest, src, IntPtr.Zero);
			else
				UnixLink(src, dest);
		}
		catch {
		}
		if (Ok())
			return;
		if (File.Exists(dest))
			File.Delete(dest);
		File.Copy(src, dest, true);
	}

	/// <summary>
	/// После загрузки модов: переносит состояние галочек из стандартного меню в активные сборки
	/// и копирует в них новые включённые моды.
	/// </summary>
	public static void SyncFromLoadedMods()
	{
		var active = ActivePacks.ToList();
		if (active.Count == 0)
			return;

		var enabled = Tml.EnabledMods;
		var inActive = new HashSet<string>();
		foreach (var pack in active) {
			var names = pack.ModNames();
			inActive.UnionWith(names);
			pack.Enabled = names.Where(enabled.Contains).OrderBy(n => n).ToList();
		}

		foreach (var mod in Terraria.ModLoader.ModLoader.Mods) {
			if (mod.Name == "ModLoader" || mod.Name == SelfName || inActive.Contains(mod.Name))
				continue;
			string path = Tml.ModFilePath(mod);
			if (path == null || !File.Exists(path))
				continue;
			foreach (var pack in active)
				pack.AddModFile(path, mod.Name, true);
			ActiveDirty = true;
			JemMod.Instance.Logger.Info($"{mod.Name} добавлен в активные сборки");
		}

		foreach (var pack in active)
			pack.Save();
	}

	/// <summary>Какие моды показывать в стандартном списке (null — показывать всё).</summary>
	public static HashSet<string> VisibleNames()
	{
		var active = ActivePacks.ToList();
		if (active.Count == 0)
			return null;
		var set = new HashSet<string> { SelfName };
		foreach (var p in active)
			set.UnionWith(p.ModNames());
		return set;
	}

	// ---------- обновление ----------

	/// <summary>Подтягивает в сборку более новые версии её модов, которые уже лежат на диске (Workshop и т.п.). Возвращает список обновлённых.</summary>
	public static List<string> UpdateFromDisk(Pack pack)
	{
		var updated = new List<string>();
		var all = Tml.FindAllModsRaw();
		foreach (string file in pack.ModFiles()) {
			string name = Path.GetFileNameWithoutExtension(file);
			var cur = ReadTmodVersion(file);
			var best = all.Where(m => m.Name == name && m.Location != "Modpack" && m.Path != null)
				.OrderByDescending(m => m.Version).FirstOrDefault();
			if (best != null && best.Version > cur) {
				SafeCopy(best.Path, file);
				updated.Add($"{name} {cur} → {best.Version}");
			}
		}
		return updated;
	}

	// ---------- импорт / экспорт ----------

	public static void Export(Pack pack, string zipPath)
	{
		pack.Save();
		string tmp = zipPath + ".tmp";
		if (File.Exists(tmp))
			File.Delete(tmp);
		ZipFile.CreateFromDirectory(pack.Dir, tmp);
		File.Move(tmp, zipPath, true);
	}

	public static string SafeFileName(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');
		return name;
	}

	public static Pack ImportZip(string zip)
	{
		string id = MakeId(Path.GetFileNameWithoutExtension(zip));
		string dir = Path.Combine(PacksDir, id);
		ZipFile.ExtractToDirectory(zip, dir);
		Pack p;
		string json = Path.Combine(dir, "pack.json");
		p = File.Exists(json) ? JsonConvert.DeserializeObject<Pack>(File.ReadAllText(json)) : new Pack { Name = id };
		p.Dir = dir;
		p.PlaySeconds = 0;
		if (!File.Exists(json))
			p.Enabled = p.ModNames().ToList();
		p.Save();
		Packs.Add(p);
		return p;
	}

	public static string TmlPacksDir => Path.Combine(Main.SavePath, "Mods", "ModPacks");

	public static IEnumerable<string> ImportCandidatesTml()
		=> Directory.Exists(TmlPacksDir)
			? Directory.GetDirectories(TmlPacksDir).Where(d => File.Exists(Path.Combine(d, "Mods", "enabled.json")))
			: Enumerable.Empty<string>();

	/// <summary>Импорт сборки из стандартного меню tML. Возвращает сборку и список модов, которых нет на диске.</summary>
	public static (Pack pack, List<string> missing) ImportTml(string tmlPackDir)
	{
		string modsDir = Path.Combine(tmlPackDir, "Mods");
		var names = JsonConvert.DeserializeObject<string[]>(File.ReadAllText(Path.Combine(modsDir, "enabled.json"))) ?? Array.Empty<string>();
		var all = Tml.FindAllModsRaw();
		var pack = Create(Path.GetFileName(tmlPackDir));
		var missing = new List<string>();
		foreach (string name in names) {
			if (name == SelfName)
				continue;
			string frozen = Path.Combine(modsDir, name + ".tmod");
			string src = File.Exists(frozen)
				? frozen
				: all.Where(m => m.Name == name && m.Path != null).OrderByDescending(m => m.Version).FirstOrDefault()?.Path;
			if (src == null) {
				missing.Add(name);
				continue;
			}
			pack.AddModFile(src, name, true);
		}
		pack.Save();
		return (pack, missing);
	}
}
