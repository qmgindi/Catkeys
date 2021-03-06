﻿using Au;
using Au.Controls;
using Au.Types;
using Au.More;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

static class App
{
	//public const string AppName = "...";//TODO: also change in eg Au postbuild, now "$(SolutionDir)Other\Programs\nircmd.exe" win close etitle Aedit
	public const string AppName = "Aedit";
	public static string UserGuid;
	internal static print.Server PrintServer;
	public static AppSettings Settings;

	/// <summary>Main window</summary>
	public static MainWindow Wmain;

	/// <summary>Handle of main window (<b>Wmain</b>).</summary>
	public static wnd Hwnd;

	public static KMenuCommands Commands;
	public static FilesModel Model;
	public static RunningTasks Tasks;

#if TRACE
	static App() {
		perf.first();
		timerm.after(1, _ => perf.nw());
		print.qm2.use = true;
		print.clear();
		print.redirectConsoleOutput = true;
	}
#endif

	[STAThread]
	static void Main(string[] args) {
		if (args.Length > 0) {
			switch (args[0]) {
			case "/dd":
				UacDragDrop.NonAdminProcess.MainDD(args);
				return;
				//case "/si":
				//	SetupHelpers.Install();
				//	return;
				//case "/su":
				//	SetupHelpers.Uninstall();
				//	return;
			}
		}

		//restart as admin if started as non-admin on admin user account
		if (args.Length > 0 && args[0] == "/n") {
			args = args.RemoveAt(0);
		} else if (uacInfo.ofThisProcess.Elevation == UacElevation.Limited) {
#if !DEBUG
			if(_RestartAsAdmin(args)) return;
#endif
		}
		//speed with restarting is the same as when runs as non-admin. The fastest is when started as admin. Because faster when runs as admin.

		_Main(args);
	}

	static void _Main(string[] args) {
		//#if !DEBUG
		process.thisProcessCultureIsInvariant = true;
		//#endif
		DebugTraceListener.Setup(usePrint: true);
		folders.ThisAppDocuments = (FolderPath)(folders.Documents + "Aedit");
		Directory.SetCurrentDirectory(folders.ThisApp); //because it is c:\windows\system32 when restarted as admin

#if true
		AppDomain.CurrentDomain.UnhandledException += (ad, e) => print.it(e.ExceptionObject);
		//Debug_.PrintLoadedAssemblies(true, true);
#else
		AppDomain.CurrentDomain.UnhandledException += (ad, e) => dialog.showError("Exception", e.ExceptionObject.ToString());
#endif

		if (CommandLine.OnProgramStarted(args)) return;

		PrintServer = new print.Server(true) { NoNewline = true };
		PrintServer.Start();

		Api.SetErrorMode(Api.GetErrorMode() | Api.SEM_FAILCRITICALERRORS); //disable some error message boxes, eg when removable media not found; MSDN recommends too.
		Api.SetSearchPathMode(Api.BASE_SEARCH_PATH_ENABLE_SAFE_SEARCHMODE); //let SearchPath search in current directory after system directories

		perf.next('o');
		Settings = AppSettings.Load(); //the slowest part, >37 ms. Loads many dlls used in JSON deserialization.
		perf.next('s');
		UserGuid = Settings.user; if (UserGuid == null) Settings.user = UserGuid = Guid.NewGuid().ToString();

		Tasks = new RunningTasks();
		perf.next('t');

		script.editor.IconNameToXaml_ = (s, what) => DIcons.GetIconString(s, what);
		FilesModel.LoadWorkspace(CommandLine.WorkspaceDirectory);
		perf.next('W');
		CommandLine.OnProgramLoaded();
		perf.next('c');
		Loaded = EProgramState.LoadedWorkspace;
		Model.RunStartupScripts();

		timerm.every(1000, t => _TimerProc(t));
		//note: timer can make Process Hacker/Explorer show CPU usage, even if we do nothing. Eg 0.02 if 250, 0.01 if 500, <0.01 if 1000.
		//Timer1s += () => print.it("1 s");
		//Timer1sOr025s += () => print.it("0.25 s");

		TrayIcon.Update_();
		perf.next('i');
		//perf.write();
		//return;

		if (!App.Settings.runHidden || CommandLine.StartVisible || TrayIcon.WaitForShow_()) {
			//print.it("-- loading UI --");
#if TRACE
			print.qm2.use = false;
#endif
			_LoadUI();
		}

		PrintServer.Stop();

		//#if TRACE
		//		//50 -> 36 -> 5 (5/40/9)
		//		Wnd = null;
		//		Commands = null;
		//		Panels = null;
		//		Toolbars = null;

		//		Task.Delay(2000).ContinueWith(_ => {
		//			GC.Collect();
		//			GC.WaitForPendingFinalizers();
		//			Api.SetProcessWorkingSetSize(Api.GetCurrentProcess(), -1, -1);
		//		});
		//		Api.MessageBox(default, "", "", 0);
		//#endif
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static void _LoadUI() {
		var app = new Aedit.WpfApp { ShutdownMode = ShutdownMode.OnMainWindowClose };
		app.InitializeComponent(); //FUTURE: remove if not used. Adds 2 MB (10->12) when running hidden at startup.
		new MainWindow();
		app.DispatcherUnhandledException += (_, e) => {
			e.Handled = 1 == dialog.showError("Exception", e.Exception.ToStringWithoutStack(), "1 Continue|2 Exit", DFlags.Wider, Wmain, e.Exception.ToString());
		};
		app.Run(Wmain);
	}

	/// <summary>
	/// Timer with 1 s period.
	/// </summary>
	public static event Action Timer1s;

	/// <summary>
	/// Timer with 1 s period when main window hidden and 0.25 s period when visible.
	/// </summary>
	public static event Action Timer1sOr025s;

	/// <summary>
	/// Timer with 0.25 s period, only when main window visible.
	/// </summary>
	public static event Action Timer025sWhenVisible;

	/// <summary>
	/// True if Timer1sOr025s period is 0.25 s (when main window visible), false if 1 s (when hidden).
	/// </summary>
	public static bool IsTimer025 => s_timerCounter > 0;
	static uint s_timerCounter;

	static void _TimerProc(timerm t) {
		Timer1sOr025s?.Invoke();
		bool needFast = Wmain?.IsVisible ?? false;
		if (needFast != (s_timerCounter > 0)) t.Every(needFast ? 250 : 1000);
		if (needFast) {
			Timer025sWhenVisible?.Invoke();
			s_timerCounter++;
			if (MousePosChangedWhenProgramVisible != null) {
				var p = mouse.xy;
				if (p != s_mousePos) {
					s_mousePos = p;
					MousePosChangedWhenProgramVisible(p);
				}
			}
		} else s_timerCounter = 0;
		if (0 == (s_timerCounter & 3)) Timer1s?.Invoke();
	}
	static POINT s_mousePos;

	/// <summary>
	/// When cursor position changed while the main window is visible.
	/// Called at max 0.25 s rate, not for each change.
	/// Cursor can be in any window. Does not depend on UAC.
	/// Receives cursor position in screen.
	/// </summary>
	public static event Action<POINT> MousePosChangedWhenProgramVisible;

	public static EProgramState Loaded;

	/// <summary>
	/// Gets Keyboard.FocusedElement. If null, and a HwndHost-ed control is focused, returns the HwndHost.
	/// Slow if HwndHost-ed control.
	/// </summary>
	public static FrameworkElement FocusedElement {
		get {
			var v = System.Windows.Input.Keyboard.FocusedElement;
			if (v != null) return v as FrameworkElement;
			return wnd.Internal_.ToWpfElement(Api.GetFocus());
		}
	}

	static bool _RestartAsAdmin(string[] args) {
		if (Debugger.IsAttached) return false; //very fast
		try {
			bool isAuHomePC = Api.EnvironmentVariableExists("Au.Home<PC>") && !folders.ThisAppBS.Starts(@"C:\Program Files", true);
			//int pid = 
			WinTaskScheduler.RunTask("Au",
				isAuHomePC ? "_Aedit" : "Aedit", //run Q:\app\Au\_\Au.CL.exe or <installed path>\Au.CL.exe
				true, args);
			//Api.AllowSetForegroundWindow(pid); //fails and has no sense, because it's Au.CL.exe running as SYSTEM
		}
		catch (Exception ex) { //probably this program is not installed (no scheduled task)
			print.qm2.write(ex);
			return false;
		}
		return true;
	}

	internal static class TrayIcon
	{
		static IntPtr[] _icons;
		static bool _disabled;
		static wnd _wNotify;

		const int c_msgBreakMessageLoop = Api.WM_APP;
		const int c_msgNotify = Api.WM_APP + 1;
		static int s_msgTaskbarCreated;

		internal static void Update_() {
			if (_icons == null) {
				_icons = new IntPtr[2];

				s_msgTaskbarCreated = WndUtil.RegisterMessage("TaskbarCreated", uacEnable: true);

				WndUtil.RegisterWindowClass("Aedit.TrayNotify", _WndProc);
				_wNotify = WndUtil.CreateWindow("Aedit.TrayNotify", null, WS.POPUP, WSE.NOACTIVATE);
				//not message-only, because must receive s_msgTaskbarCreated and also used for context menu

				process.thisProcessExit += _ => {
					var d = new Api.NOTIFYICONDATA(_wNotify);
					Api.Shell_NotifyIcon(Api.NIM_DELETE, d);
				};

				_Add(false);
			} else {
				var d = new Api.NOTIFYICONDATA(_wNotify, Api.NIF_ICON) { hIcon = _GetIcon() };
				bool ok = Api.Shell_NotifyIcon(Api.NIM_MODIFY, d);
				Debug_.PrintIf(!ok, lastError.message);
			}
		}

		static void _Add(bool restore) {
			var d = new Api.NOTIFYICONDATA(_wNotify, Api.NIF_MESSAGE | Api.NIF_ICON | Api.NIF_TIP /*| Api.NIF_SHOWTIP*/) { //need NIF_SHOWTIP if called NIM_SETVERSION(NOTIFYICON_VERSION_4)
				uCallbackMessage = c_msgNotify,
				hIcon = _GetIcon(),
				szTip = App.AppName
			};
			if (Api.Shell_NotifyIcon(Api.NIM_ADD, d)) {
				//d.uFlags = 0;
				//d.uVersion = Api.NOTIFYICON_VERSION_4;
				//Api.Shell_NotifyIcon(Api.NIM_SETVERSION, d);

				//timerm.after(2000, _ => Update(TrayIconState.Disabled));
				//timerm.after(3000, _ => Update(TrayIconState.Running));
				//timerm.after(4000, _ => Update(TrayIconState.Normal));
			} else if(!restore) { //restore when "TaskbarCreated" message received. It is also received when taskbar DPI changed.
				Debug_.Print(lastError.message);
			}
		}

		static IntPtr _GetIcon() {
			int i = _disabled ? 1 : 0;
			ref IntPtr icon = ref _icons[i];
			if (icon == default) Api.LoadIconMetric(Api.GetModuleHandle(null), Api.IDI_APPLICATION + i, 0, out icon);
			return icon;
			//Windows 10 on DPI change automatically displays correct non-scaled icon if it is from native icon group resource.
			//	I guess then it calls GetIconInfoEx to get module/resource and extracts new icon from same resource.
		}

		static void _Notified(nint wParam, nint lParam) {
			int msg = Math2.LoWord(lParam);
			//if (msg != Api.WM_MOUSEMOVE) WndUtil.PrintMsg(default, msg, 0, 0);
			switch (msg) {
			case Api.WM_LBUTTONUP:
				_ShowWindow();
				break;
			case Api.WM_RBUTTONUP:
				_ContextMenu();
				break;
			case Api.WM_MBUTTONDOWN:
				TriggersAndToolbars.DisableTriggers(null);
				break;
			}
		}

		static nint _WndProc(wnd w, int m, nint wParam, nint lParam) {
			//WndUtil.PrintMsg(w, m, wParam, lParam);
			if (m == c_msgNotify) _Notified(wParam, lParam);
			else if (m == s_msgTaskbarCreated) _Add(true); //when explorer restarted or taskbar DPI changed
			else if (m == Api.WM_DESTROY) _Exit();
			else if (m == Api.WM_DISPLAYCHANGE) {
				Tasks.OnWM_DISPLAYCHANGE();
			}

			return Api.DefWindowProc(w, m, wParam, lParam);
		}

		internal static bool WaitForShow_() {
			while (Api.GetMessage(out var m) > 0) {
				if (m.hwnd == default && m.message == c_msgBreakMessageLoop) return 0 != m.wParam;
				Api.TranslateMessage(m);
				Api.DispatchMessage(m);
			}
			return false;
		}

		static void _ContextMenu() {
			var m = new popupMenu();
			m.AddCheck("Disable triggers\tM-click", check: _disabled, _ => TriggersAndToolbars.DisableTriggers(null));
			m.Separator();
			m.Add("Exit", _ => _Exit());
			m.Show(MSFlags.AlignBottom | MSFlags.AlignCenterH);
		}

		static void _ShowWindow() {
			var w = Wmain;
			if (w != null) {
				w.Show();
				Hwnd.ActivateL();
			} else {
				Api.PostMessage(default, c_msgBreakMessageLoop, 1, 0);
			}
		}

		static void _Exit() {
			if (App.Wmain != null) {
				Application.Current.Shutdown();
			} else {
				Api.PostMessage(default, c_msgBreakMessageLoop, 0, 0);
			}
		}

		public static bool Disabled {
			get => _disabled;
			set { if (value == _disabled) return; _disabled = value; Update_(); }
		}
	}
}

enum EProgramState
{
	/// <summary>
	/// Before the first workspace fully loaded.
	/// </summary>
	Loading,

	/// <summary>
	/// When fully loaded first workspace etc and created main form handle.
	/// Main form invisible; control handles not created.
	/// </summary>
	LoadedWorkspace,

	/// <summary>
	/// Control handles created.
	/// Main form is either visible now or was visible and now hidden.
	/// </summary>
	LoadedUI,

	/// <summary>
	/// Executing OnFormClosed of main form.
	/// Unloading workspace; stopping everything.
	/// </summary>
	Unloading,

	/// <summary>
	/// After OnFormClosed of main form.
	/// Workspace unloaded; everything stopped.
	/// </summary>
	Unloaded,
}

enum ERegisteredHotkeyId
{
	QuickCapture = 1,
}

namespace Aedit
{
	partial class WpfApp : Application
	{
	}
}