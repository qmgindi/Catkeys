﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using System.Drawing;
//using System.Linq;

using Catkeys;
using static Catkeys.NoClass;
using Util = Catkeys.Util;
using Catkeys.Winapi;

namespace Catkeys.Tasks
{
	static class Program
	{
		static Wnd _wMain;
#pragma warning disable 649

		//static Program()
		//{
		//}

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		//[LoaderOptimization(LoaderOptimization.MultiDomain)] //makes new AppDomain creation faster by 10 ms. But then does not unload assemblies of unloaded domains (uses many more MB of memory).
		[LoaderOptimization(LoaderOptimization.MultiDomainHost)] //makes new AppDomain creation faster, and unloads non-GAC assemblies, eg frees many MB of compiler memory.
		static void Main(string[] args)
		{
			//if(args.Length >= 1 && args[0] == "/domain") {
			//	Show.TaskDialog("domain");
			//	return;
			//}

			bool measureStartupTime = args.Length >= 1 && args[0] == "/perf";
			if(measureStartupTime) Perf.Next(); //Perf.Write(); //startup time: 20 ms when ngen-compiled, else >26 ms. After PC restart ~150 ms (Win10, SSD).

			//single process instance
			bool isOwner;
			var mutex = new Mutex(true, "{8AE35071-A3F5-423A-8F67-7C3AC0FFC8E3}", out isOwner);
			if(!isOwner) {
				if(args.Length > 0) {
					var w = Wnd.FindRaw("CatkeysTasks");
					w.SendS(Api.WM_SETTEXT, 0x8000, string.Join("\x19", args));
				}
				return;
			}

			//Application.EnableVisualStyles(); //730 mcs
			//Application.SetCompatibleTextRenderingDefault(false); //70 mcs
			Api.SetErrorMode(Api.SEM_FAILCRITICALERRORS); //disable some error message boxes, eg when removable media not found; MSDN recommends too.
			Api.SetSearchPathMode(Api.BASE_SEARCH_PATH_ENABLE_SAFE_SEARCHMODE); //let SearchPath search in current directory after system directories

			Api.ChangeWindowMessageFilter(Api.WM_SETTEXT, 1);
			Api.ChangeWindowMessageFilter(Api.WM_COPYDATA, 1);

#if true
			Test.TestInNewAppDomain();
			//Test.TestInThisAppDomain();

			mutex.ReleaseMutex(); //also keeps mutex alive
#else
			Output.Clear();

			try {
				Wnd.Misc.WndClass.InterDomainRegister("CatkeysTasks", WndProcAppsManager);
				_wMain = Wnd.Misc.WndClass.InterDomainCreateWindow(0, "CatkeysTasks",
					//null, Api.WS_POPUP, 0, 0, 0, 0, Wnd.Misc.SpecHwnd.Message);
					"Catkeys Tasks", Api.WS_OVERLAPPEDWINDOW | Api.WS_VISIBLE, 400, 300, 400, 200, Wnd0);

				Perf.First();
				//_TrayIcon(true); //13 ms
				Time.SetTimer(1, true, t=>_TrayIcon(true)); //note: shows tray icon with a delay if we have a work in this thread at startup
				//new Thread(() => { _TrayIcon(true); }).Start(); //cannot use NotifyIcon in a temporary thread

				if(measureStartupTime) Perf.NW();

				ProcessCommandLine(args);

				//message loop (not Application.Run() because then loads slower etc)
				Api.MSG m;
				while(Api.GetMessage(out m, Wnd0, 0, 0) > 0) { Api.DispatchMessage(ref m); }
			}
			finally {
				if(!_compilerWindow.Is0) _compilerWindow.Send(Api.WM_CLOSE);
				_TrayIcon(false);
				mutex.ReleaseMutex(); //also keeps mutex alive
			}
#endif
		}

#if true //15-16 ms
		//This is less code, but we cannot use a temporary thread to make startup faster, because NotifyIcon creates a window.
		//But now we use timer to make startup faster.

		static NotifyIcon _trayIcon;

		static void _TrayIcon(bool add)
		{
			if(add) {
				_trayIcon = new NotifyIcon();
				//_trayIcon.Icon = Properties.Resources.trayIcon; //28 ms first time
				_trayIcon.Icon = Icon.FromHandle(Icons.GetAppIconHandle((int)Icons.ShellSize.SysSmall));
				_trayIcon.Text = "Catkeys Tasks";
				_trayIcon.MouseClick += _trayIcon_MouseClick;
				_trayIcon.Visible = true;
				//Perf.NW();
			} else if(_trayIcon!= null) {
				_trayIcon.Visible = false;
				_trayIcon.Dispose();
				_trayIcon = null;
            }
		}

		private static void _trayIcon_MouseClick(object sender, MouseEventArgs e)
		{
			switch(e.Button) {
			case MouseButtons.Left:
				_wMain.CloseRawAsync();
                break;
			case MouseButtons.Right:
				_TrayIcon_Menu();
                break;
            }
		}
#else //12-14 ms
		static void _TrayIcon(bool add)
		{
			//Perf.Next();
			var x = new Api.NOTIFYICONDATA(); x.cbSize = Api.SizeOf(x);
			x.hWnd = _wMain;
			x.uID = 1;
			if(add) {
				x.uFlags = Api.NIF_ICON | Api.NIF_MESSAGE | Api.NIF_TIP;
				//x.hIcon = Properties.Resources.trayIcon.Handle; //27-28 ms (later in this appdomain - 0.25 ms)
				x.hIcon = Icons.GetAppIconHandle((int)Icons.ShellSize.SysSmall);
				x.uCallbackMessage = Api.WM_USER + 3;
				x.szTip = "Catkeys Tasks";
				//Perf.Next();
				Api.Shell_NotifyIcon(Api.NIM_ADD, ref x);
				//Perf.NW();
			} else {
				Api.Shell_NotifyIcon(Api.NIM_DELETE, ref x);
			}
		}

		static void _TrayIcon_Click(uint lParam)
		{
			switch(lParam) {
			case Api.WM_LBUTTONUP:
				_wMain.Destroy();
				//_wMain.Post(Api.WM_CLOSE);
				break;
			case Api.WM_RBUTTONUP:
				_TrayIcon_Menu();
				break;
			case Api.WM_MBUTTONUP:
				break;
			case Api.WM_MOUSEMOVE:
				break;
			}
		}

		static readonly uint _msgTaskbarCreated = Api.RegisterWindowMessage("TaskbarCreated");

		static bool _WndProcTrayIcon(uint msg, LPARAM wParam, LPARAM lParam)
		{
			switch(msg) {
			case Api.WM_USER + 3:
				if(wParam == 1) _TrayIcon_Click(lParam);
				return true;
			default:
				if(msg == _msgTaskbarCreated) { _TrayIcon(true); return true; }
				break;
			}
			return false;
		}
#endif

		static void _TrayIcon_Menu()
		{
			var m = new CatMenu();
			m["one"] = o => Out("one");

			m.Show();
		}

		unsafe static LPARAM WndProcAppsManager(Wnd hWnd, uint msg, LPARAM wParam, LPARAM lParam)
		{
			//if(_WndProcTrayIcon(msg, wParam, lParam)) return 0;

			switch(msg) {
			//case WM.DESTROY:
			//	Out("destroy");
			//	break;
			case Api.WM_SETTEXT:
				return _OnSetText(wParam, lParam);
			case Api.WM_COPYDATA:
				_OnCopyData((Wnd)wParam, (Api.COPYDATASTRUCT*)lParam);
				break;
			default:
				break;
			}

			LPARAM R = Api.DefWindowProc(hWnd, msg, wParam, lParam);

			switch(msg) {
			case Api.WM_NCDESTROY:
				Api.PostQuitMessage(0);
				break;
			}
			return R;

			//tested: .NET class NativeWindow. Its purpose is different (to wrap/subclass an existing class).
		}

		unsafe static int _OnSetText(int wParam, LPARAM lParam)
		{
			string s;
			switch(wParam) {
			case 0x8000: //new process passed command line to this process
				s = Marshal.PtrToStringUni(lParam);
				ProcessCommandLine(s.Split('\x19'));
				break;
			case 1:
				s = Marshal.PtrToStringUni(lParam);
				ProcessCommandLine(new string[] { s });
				break;
			}
			return 0;
		}

		unsafe static int _OnCopyData(Wnd hwndSender, Api.COPYDATASTRUCT* c)
		{
			Out((uint)c->dwData);
			string s;
			switch((uint)c->dwData) {
			case 4:
				s = Marshal.PtrToStringUni(c->lpData, c->cbData / 2);
				Out(s);
				break;
			}
			return 0;
		}

		static void ProcessCommandLine(string[] a)
		{
			if(a == null || a.Length == 0) return;
			string csFile = null;
			int i = 0;
			try {
				for(; i < a.Length; i++) {
					string s = a[i];
					//Out(s);
					if(s[0] == '/' || s[0] == '-') {
						switch(s.Substring(1)) {
						case "run":
							break;
						}
					} else {
						csFile = s;
					}
				}
			}
			catch {
				Out($"CatkeysTasks: Failed to parse command line at '{a[i < a.Length ? i : a.Length - 1]}'.");
				return;
			}

			if(csFile != null) {
				//CompileScript(csFile);
				Task.Run(() => CompileScript(csFile));
			}
		}

		static void CompileScript(string csFile)
		{
			Perf.First();
			string outDir = Folders.LocalAppData + @"Catkeys\ScriptDll\";
			if(!Files.DirectoryExists(outDir)) Directory.CreateDirectory(outDir);
			string outFile = outDir + Path.GetFileNameWithoutExtension(csFile) + ".exe";
			//OutList(csFile, dllFile);

			if(!_compilerWindow.IsValid) {
				IntPtr ev = Api.CreateEvent(Zero, false, false, null);
				var thr = new Thread(_CompilerAppDomainThread);
				thr.Start(ev);
				Api.WaitForSingleObject(ev, ~0U);
				Api.CloseHandle(ev);
				_compilerWindow = (Wnd)(IntPtr)_compilerDomain.GetData("hwndCompiler");
			}

			_compilerDomain.SetData("cs", csFile);
			_compilerDomain.SetData("out", outFile);
			Perf.Next(); //20 ms first time, then <100 mcs
			int R = _compilerWindow.Send(Api.WM_USER, 0, 0);
			Out(R);
			if(R != 0) return;

#if false
			//Show.TaskDialog("start");
			int i, n = 1;
			Thread[] at = new Thread[n];
			for(i = 0; i < n; i++) {
				//Show.TaskDialog("domain", i);
				//WaitMS(5);
				at[i] = new Thread(() =>
				  {
					  var ad = AppDomain.CreateDomain("ad" + i);
					  ad.ExecuteAssembly(outFile);
					  AppDomain.Unload(ad);
				  });
				at[i].Start();
				//GC.Collect();
			}
			for(i = 0; i < n; i++) {
				at[i].Join();
			}

			//GC.Collect();
			//Show.TaskDialog("fin");
#endif
		}

		static AppDomain _compilerDomain;
		static Wnd _compilerWindow;

		static void _CompilerAppDomainThread(object ev)
		{
			_compilerDomain = AppDomain.CreateDomain("Compiler");
			_compilerDomain.SetData("eventInited", ev);
			_compilerDomain.ExecuteAssembly(Folders.App + "Catkeys.Compiler.exe");
			AppDomain.Unload(_compilerDomain);
		}
	}
}
