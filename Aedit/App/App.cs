﻿using Au;
using Au.Controls;
using Au.Types;
using Au.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

static class App
{
	public const string AppName = "Aedit";
	public static string UserGuid;
	internal static AOutputServer OutputServer;
	public static AppSettings Settings;
	public static MainWindow Wnd;
	public static AuMenuCommands Commands;
	public static FilesModel Model;
	public static RunningTasks Tasks;

#if TRACE
	static App() {
		APerf.First();
		ATimer.After(1, _ => APerf.NW());
	}
#endif

	[STAThread]
	static void Main(string[] args) {
		//#if !DEBUG
		AProcess.CultureIsInvariant = true;
		//#endif
		AssertListener_.Setup();

#if true
		AppDomain.CurrentDomain.UnhandledException += (ad, e) => AOutput.Write(e.ExceptionObject);
		AOutput.QM2.UseQM2 = true;
		AOutput.Clear();
		//ADebug.PrintLoadedAssemblies(true, true);
#else
		AppDomain.CurrentDomain.UnhandledException += (ad, e) => ADialog.ShowError("Exception", e.ExceptionObject.ToString());
#endif

		AFolders.ThisAppDocuments = (FolderPath)(AFolders.Documents + "Aedit");

		if (CommandLine.OnProgramStarted(args)) return;

		//OutputServer = new AOutputServer(true) { NoNewline = true }; //TODO
		OutputServer = new AOutputServer(false) { NoNewline = true };
		OutputServer.Start();

		Api.SetErrorMode(Api.GetErrorMode() | Api.SEM_FAILCRITICALERRORS); //disable some error message boxes, eg when removable media not found; MSDN recommends too.
		Api.SetSearchPathMode(Api.BASE_SEARCH_PATH_ENABLE_SAFE_SEARCHMODE); //let SearchPath search in current directory after system directories

		Settings = AppSettings.Load();
		UserGuid = Settings.user; if (UserGuid == null) Settings.user = UserGuid = Guid.NewGuid().ToString();

		//AppDomain.CurrentDomain.AssemblyLoad += (object sender, AssemblyLoadEventArgs e) => {
		//	//AOutput.Write(e.LoadedAssembly.GetName().Name);
		//	;
		//};

		AOutput.Write(CommandLine.MsgWnd);
		Tasks = new RunningTasks();
		//return;

		//FilesModel.LoadWorkspace(CommandLine.WorkspaceDirectory);
		//CommandLine.OnProgramLoaded();
		//Loaded = EProgramState.LoadedWorkspace;
		//Model.RunStartupScripts();

		//ATimer.Every(1000, t => _TimerProc(t));
		////note: timer can make Process Hacker/Explorer show CPU usage, even if we do nothing. Eg 0.02 if 250, 0.01 if 500, <0.01 if 1000.
		////Timer1s += () => AOutput.Write("1 s");
		////Timer1sOr025s += () => AOutput.Write("0.25 s");

		//TrayIcon.Update_();

		//if (!App.Settings.runHidden /*|| CommandLine.StartVisible*/ || TrayIcon.WaitForShow_()) {
		//	AOutput.Write("-- loading UI --");
		//	_LoadUI();
		//}

		//OutputServer.Stop();

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
		var app = new WpfApp();
		app.InitializeComponent(); //FUTURE: remove if not used. Adds 2 MB (10->12) when running hidden at startup.
		new MainWindow();
		app.DispatcherUnhandledException += (_, e) => {
			e.Handled = 1 == ADialog.ShowError("Exception", e.Exception.ToStringWithoutStack(), "1 Continue|2 Exit", DFlags.Wider, Wnd, e.Exception.ToString());
		};
		app.Run(Wnd);
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

	static void _TimerProc(ATimer t) {
		Timer1sOr025s?.Invoke();
		bool needFast = Wnd.IsVisible;
		if (needFast != (s_timerCounter > 0)) t.Every(needFast ? 250 : 1000);
		if (needFast) {
			Timer025sWhenVisible?.Invoke();
			s_timerCounter++;
			if (MousePosChangedWhenProgramVisible != null) {
				var p = AMouse.XY;
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

	static bool _RestartAsAdmin(string[] args) {
		if (Debugger.IsAttached) return false; //very fast
		try {
			bool isAuHomePC = Api.GetEnvironmentVariable("Au.Home<PC>", null, 0) > 0;
			//int pid = 
			WinTaskScheduler.RunTask("Au",
				isAuHomePC ? "_Au.Editor" : "Au.Editor", //run Q:\app\Au\_\Au.CL.exe or <installed path>\Au.CL.exe
				true, args);
			//Api.AllowSetForegroundWindow(pid); //fails and has no sense, because it's Au.CL.exe running as SYSTEM
		}
		catch (Exception ex) { //probably this program is not installed (no scheduled task)
			AOutput.QM2.Write(ex);
			return false;
		}
		return true;
	}

	public static class TrayIcon
	{
		static AIcon[] _icons;
		static bool _disabled, _running;
		static AWnd _wNotify;

		const int c_msgBreakMessageLoop = Api.WM_APP;
		const int c_msgNotify = Api.WM_APP + 1;
		static int s_msgTaskbarCreated;

		internal static void Update_() {
			if (_icons == null) {
				_icons = new AIcon[3];

				s_msgTaskbarCreated = AWnd.More.RegisterMessage("TaskbarCreated", uacEnable: true);

				AWnd.More.RegisterWindowClass("Aedit.TrayNotify", _WndProc);
				_wNotify = AWnd.More.CreateWindow("Aedit.TrayNotify", null, WS.POPUP, WS2.NOACTIVATE);
				//not message-only, because must receive s_msgTaskbarCreated and also used for context menu

				AProcess.Exit += (_, _) => {
					var d = new Api.NOTIFYICONDATA(_wNotify);
					Api.Shell_NotifyIcon(Api.NIM_DELETE, d);
				};

				_Add();
			} else {
				var d = new Api.NOTIFYICONDATA(_wNotify, Api.NIF_ICON) { hIcon = _GetIcon() };
				bool ok = Api.Shell_NotifyIcon(Api.NIM_MODIFY, d);
				ADebug.PrintIf(!ok, ALastError.Message);
			}
		}

		static void _Add() {
			var d = new Api.NOTIFYICONDATA(_wNotify, Api.NIF_MESSAGE | Api.NIF_ICON | Api.NIF_TIP /*| Api.NIF_SHOWTIP*/) { //need NIF_SHOWTIP if called NIM_SETVERSION(NOTIFYICON_VERSION_4)
				uCallbackMessage = c_msgNotify,
				hIcon = _GetIcon(),
				szTip = App.AppName
			};
			if (Api.Shell_NotifyIcon(Api.NIM_ADD, d)) {
				//d.uFlags = 0;
				//d.uVersion = Api.NOTIFYICON_VERSION_4;
				//Api.Shell_NotifyIcon(Api.NIM_SETVERSION, d);

				//ATimer.After(2000, _ => Update(TrayIconState.Disabled));
				//ATimer.After(3000, _ => Update(TrayIconState.Running));
				//ATimer.After(4000, _ => Update(TrayIconState.Normal));
			} else {
				var ec = ALastError.Code;
				if (ec == 0) return; //probably when "TaskbarCreated" message received when taskbar DPI changed. How to know? wParam and lParam 0.
				ADebug.Print(ALastError.Message);
			}
		}

		static AIcon _GetIcon() {
			int i = _running ? 2 : (_disabled ? 1 : 0);
			ref AIcon icon = ref _icons[i];
			if (icon.Is0) Api.LoadIconMetric(AProcess.ExeModuleHandle, Api.IDI_APPLICATION + i, 0, out icon);
			return icon;
			//Windows 10 on DPI change automatically displays correct non-scaled icon if it is from native icon group resource.
			//	I guess then it calls GetIconInfoEx to get module/resource and extracts new icon from same resource.
		}

		static void _Notified(LPARAM wParam, LPARAM lParam) {
			int msg = AMath.LoWord(lParam);
			//if (msg != Api.WM_MOUSEMOVE) AWnd.More.PrintMsg(default, msg, 0, 0);
			switch (msg) {
			case Api.WM_LBUTTONUP:
				_ShowWindow();
				break;
			case Api.WM_RBUTTONUP:
				_ContextMenu();
				break;
			case Api.WM_MBUTTONDOWN:

				break;
			}
		}

		static LPARAM _WndProc(AWnd w, int message, LPARAM wParam, LPARAM lParam) {
			//AWnd.More.PrintMsg(w, message, wParam, lParam);
			if (message == c_msgNotify) _Notified(wParam, lParam);
			else if (message == s_msgTaskbarCreated) _Add(); //when explorer restarted or taskbar DPI changed
			else if (message == Api.WM_DESTROY) _Exit();

			return Api.DefWindowProc(w, message, wParam, lParam);
		}

		internal static bool WaitForShow_() {
			while (Api.GetMessage(out var m) > 0) {
				if (m.hwnd == default && m.message == c_msgBreakMessageLoop) return m.wParam;
				Api.TranslateMessage(m);
				Api.DispatchMessage(m);
			}
			return false;
		}

		static void _ContextMenu() {
			//Don't use AContextMenu. Slow, adds +24 MB (6->30), don't work keys etc.
			using var m = new ClassicPopupMenu_();
			m.Add(1, "End green task\tSleep");
			m.Add(2, "Disable triggers\tM-click");
			m.Separator();
			m.Add(10, "Exit");

			var wa = AWnd.Active; //probably taskbar
			_wNotify.ActivateLL();
			int r = m.Show(_wNotify);
			switch (r) {
			case 1:
				break;
			case 2:
				break;
			case 10:
				_Exit();
				break;
			}
			if (r != 10) { //sometimes does not exit because of ActivateLL
				wa.ActivateLL();
				//var d = new Api.NOTIFYICONDATA(_wNotify); Api.Shell_NotifyIcon(Api.NIM_SETFOCUS, d); //rejected: looks weird when none of tested tray icons do it; none even simply activate taskbar.
			}
		}

		static void _ShowWindow() {
			var w = App.Wnd;
			if (w != null) {
				w.Show();
				w.Activate();
			} else {
				Api.PostMessage(default, c_msgBreakMessageLoop, 1, 0);
			}
		}

		static void _Exit() {
			if (App.Wnd != null) {
				Application.Current.Shutdown();
			} else {
				Api.PostMessage(default, c_msgBreakMessageLoop, 0, 0);
			}
		}

		public static bool Disabled {
			get => _disabled;
			set { if (value == _disabled) return; _disabled = value; if(!_running) Update_(); }
		}

		public static bool Running {
			get => _running;
			set { if (value == _running) return; _running = value; Update_(); }
		}
	}
}

//enum TrayIcon

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

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
partial class WpfApp : Application
{
}
