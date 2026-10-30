using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace JustEnoughMods.Core;

/// <summary>Системные окна: открыть папку, выбрать файл. Работает на Windows, Linux, macOS и под Wine/Proton.</summary>
public static class HostShell
{
	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetModuleHandleA(string name);

	[DllImport("kernel32", CharSet = CharSet.Ansi)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	private static bool? _isWine;

	public static bool IsWine {
		get {
			if (_isWine == null) {
				try {
					_isWine = OperatingSystem.IsWindows() && GetProcAddress(GetModuleHandleA("ntdll.dll"), "wine_get_version") != IntPtr.Zero;
				}
				catch {
					_isWine = false;
				}
			}
			return _isWine.Value;
		}
	}

	private static string FileUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

	public static void OpenFolder(string path)
	{
		Directory.CreateDirectory(path);
		try {
			if (IsWine)
				Run("winebrowser", FileUri(path)); // Wine передаёт в xdg-open хоста
			else if (OperatingSystem.IsWindows())
				Run("explorer.exe", $"\"{path}\"");
			else if (OperatingSystem.IsMacOS())
				Run("open", $"\"{path}\"");
			else
				Run("xdg-open", $"\"{path}\"");
		}
		catch (Exception e) {
			JemMod.Instance?.Logger.Warn("Не открылась папка " + path, e);
		}
	}

	private static void Run(string file, string args)
		=> Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })?.Dispose();

	/// <summary>Системный диалог открытия .zip. null — отмена. Блокирует поток.</summary>
	public static string PickZip(string title) => FileDialog(title, null);

	/// <summary>Системный диалог «Сохранить как» для .zip. null — отмена. Блокирует поток.</summary>
	public static string SaveZip(string title, string defaultName) => FileDialog(title, defaultName);

	private static string FileDialog(string title, string saveName)
	{
		bool save = saveName != null;
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (OperatingSystem.IsMacOS()) {
			string script = save
				? $"POSIX path of (choose file name with prompt \\\"{title}\\\" default name \\\"{saveName}\\\")"
				: $"POSIX path of (choose file with prompt \\\"{title}\\\" of type {{\\\"zip\\\"}})";
			return Capture("osascript", $"-e \"{script}\"");
		}

		if (!OperatingSystem.IsWindows()) {
			string start = Path.Combine(home, save ? saveName : "");
			return Capture("kdialog", save
					? $"--getsavefilename \"{start}\" \"*.zip|ZIP\" --title \"{title}\""
					: $"--getopenfilename \"{home}\" \"*.zip|ZIP\" --title \"{title}\"")
				?? Capture("zenity", ZenityArgs(title, save ? saveName : null));
		}

		if (IsWine) {
			string unix = WineHostDialog(title, saveName, out bool ran);
			if (ran)
				return unix == null ? null : "Z:" + unix.Replace('/', '\\');
		}
		return WinFileDialog(title, saveName);
	}

	private static string ZenityArgs(string title, string saveName)
		=> saveName != null
			? $"--file-selection --save --confirm-overwrite --filename=\"{saveName}\" --title=\"{title}\" --file-filter=\"*.zip\""
			: $"--file-selection --title=\"{title}\" --file-filter=\"*.zip\"";

	/// <summary>
	/// Под Wine/Proton: zenity хоста через /bin/sh. Wine не может дождаться Linux-процесса и прочитать его вывод,
	/// поэтому ответ пишется во временный файл, а мы ждём файл-флаг. GTK_USE_PORTAL=1 — диалог от рабочего стола (в KDE — как в Dolphin).
	/// </summary>
	private static string WineHostDialog(string title, string saveName, out bool ran)
	{
		ran = false;
		string id = Guid.NewGuid().ToString("N");
		string outUnix = $"/tmp/jem-{id}.out", doneUnix = $"/tmp/jem-{id}.done";
		string outWin = @"Z:\tmp\jem-" + id + ".out", doneWin = @"Z:\tmp\jem-" + id + ".done";
		string cmd = $"cd \"$HOME\"; GTK_USE_PORTAL=1 zenity {ZenityArgs(title, saveName)} > {outUnix}; echo $? > {doneUnix}";
		try {
			var psi = new ProcessStartInfo(@"Z:\bin\sh") { UseShellExecute = false };
			psi.ArgumentList.Add("-c");
			psi.ArgumentList.Add(cmd);
			Process.Start(psi)?.Dispose();
		}
		catch (Exception e) {
			JemMod.Instance?.Logger.Warn("Диалог хоста не запустился", e);
			return null;
		}

		// Файл вывода появляется сразу при запуске sh; нет его за 5 секунд — sh не запустился
		var started = DateTime.Now;
		while (!File.Exists(doneWin)) {
			System.Threading.Thread.Sleep(200);
			double sec = (DateTime.Now - started).TotalSeconds;
			if (sec > 5 && !File.Exists(outWin) || sec > 1800)
				return null;
		}
		ran = true;
		try {
			string code = File.ReadAllText(doneWin).Trim();
			string result = File.Exists(outWin) ? File.ReadAllText(outWin).Trim() : "";
			JemMod.Instance?.Logger.Info($"Диалог хоста: код {code}");
			if (code == "127") { // zenity не найден
				ran = false;
				return null;
			}
			return code == "0" && result.Length > 0 ? result : null;
		}
		finally {
			try { File.Delete(doneWin); File.Delete(outWin); } catch { }
		}
	}

	private static string Capture(string file, string args) => Capture(file, args, out _);

	private static string Capture(string file, string args, out bool started)
	{
		started = false;
		try {
			using var p = Process.Start(new ProcessStartInfo(file, args) {
				UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
			});
			if (p == null)
				return null;
			started = true;
			string output = p.StandardOutput.ReadToEnd().Trim();
			p.WaitForExit();
			return p.ExitCode == 0 && output.Length > 0 ? output : null;
		}
		catch {
			return null;
		}
	}

	// ---------- Windows / Wine: GetOpenFileName ----------

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct OpenFileName
	{
		public int lStructSize;
		public IntPtr hwndOwner;
		public IntPtr hInstance;
		public string lpstrFilter;
		public string lpstrCustomFilter;
		public int nMaxCustFilter;
		public int nFilterIndex;
		public IntPtr lpstrFile;
		public int nMaxFile;
		public string lpstrFileTitle;
		public int nMaxFileTitle;
		public string lpstrInitialDir;
		public string lpstrTitle;
		public int Flags;
		public short nFileOffset;
		public short nFileExtension;
		public string lpstrDefExt;
		public IntPtr lCustData;
		public IntPtr lpfnHook;
		public string lpTemplateName;
		public IntPtr pvReserved;
		public int dwReserved;
		public int FlagsEx;
	}

	[DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

	[DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool GetSaveFileNameW(ref OpenFileName ofn);

	private static string WinFileDialog(string title, string saveName)
	{
		const int max = 4096;
		IntPtr buffer = Marshal.AllocHGlobal(max * 2);
		try {
			Marshal.WriteInt16(buffer, 0);
			if (saveName != null) {
				var chars = (saveName + "\0").ToCharArray();
				Marshal.Copy(chars, 0, buffer, Math.Min(chars.Length, max));
			}
			var ofn = new OpenFileName {
				lStructSize = Marshal.SizeOf<OpenFileName>(),
				lpstrFilter = "ZIP\0*.zip\0\0",
				lpstrFile = buffer,
				nMaxFile = max,
				lpstrTitle = title,
				lpstrDefExt = "zip",
				Flags = saveName != null
					? 0x00080000 | 0x00000002 | 0x00000800 // EXPLORER | OVERWRITEPROMPT | PATHMUSTEXIST
					: 0x00080000 | 0x00001000 | 0x00000800 // EXPLORER | FILEMUSTEXIST | PATHMUSTEXIST
			};
			bool ok = saveName != null ? GetSaveFileNameW(ref ofn) : GetOpenFileNameW(ref ofn);
			return ok ? Marshal.PtrToStringUni(buffer) : null;
		}
		catch (Exception e) {
			JemMod.Instance?.Logger.Warn("Диалог выбора файла не открылся", e);
			return null;
		}
		finally {
			Marshal.FreeHGlobal(buffer);
		}
	}

	/// <summary>Диалог в фоне, результат — в главном потоке.</summary>
	public static void PickZipAsync(string title, Action<string> onPicked) => RunAsync(() => PickZip(title), onPicked);

	public static void SaveZipAsync(string title, string defaultName, Action<string> onPicked) => RunAsync(() => SaveZip(title, defaultName), onPicked);

	private static void RunAsync(Func<string> dialog, Action<string> onPicked)
	{
		var thread = new System.Threading.Thread(() => {
			string path = dialog();
			Terraria.Main.QueueMainThreadAction(() => onPicked(path));
		}) { IsBackground = true };
		if (OperatingSystem.IsWindows())
			thread.SetApartmentState(System.Threading.ApartmentState.STA); // системным диалогам Windows нужен STA
		thread.Start();
	}
}
