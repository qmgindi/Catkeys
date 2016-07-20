﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;

using System.Reflection;
//using System.Linq;

using Catkeys;
using static Catkeys.NoClass;
using Util = Catkeys.Util;
using static Catkeys.Util.NoClass;
using Catkeys.Winapi;
using Auto = Catkeys.Automation;

namespace Catkeys.Util
{
	[DebuggerStepThrough]
	public static class NoClass
	{
	}

	[DebuggerStepThrough]
	public class NativeSharedMemory
	{
		protected IntPtr _hmap, _mem;
		//or could use SafeMemoryMappedViewHandle. Not tested.

		/// <summary>
		/// Pointer to the base of the shared memory.
		/// </summary>
		public IntPtr mem { get { return _mem; } }

		/// <summary>
		/// Creates shared memory of specified size. Opens if already exists.
		/// Calls Api.CreateFileMapping() and Api.MapViewOfFile().
		/// </summary>
		/// <param name="name"></param>
		/// <param name="size"></param>
		/// <exception cref="Win32Exception">When fails.</exception>
		public NativeSharedMemory(string name, uint size)
		{
			_hmap = Api.CreateFileMapping((IntPtr)(~0), Zero, 4, 0, size, name);
			if(_hmap != Zero) {
				_mem = Api.MapViewOfFile(_hmap, 0x000F001F, 0, 0, 0);
				//if(_mem && nbZero) Zero(_mem, nbZero); //don't zero all, because it maps memory for all possibly unused pages. Tested: although MSDN says that the memory is zero, it is not true.
			}
			if(_mem == Zero) throw new Win32Exception();
			//TODO: option to use SECURITY_ATTRIBUTES to allow low IL processes open the memory.
		}

		/// <summary>
		/// Opens shared memory.
		/// Calls Api.OpenFileMapping() and Api.MapViewOfFile().
		/// </summary>
		/// <param name="name"></param>
		/// <exception cref="Win32Exception">When fails, eg the memory does not exist.</exception>
		public NativeSharedMemory(string name)
		{
			_hmap = Api.OpenFileMapping(0x000F001F, false, name);
			if(_hmap != Zero) {
				_mem = Api.MapViewOfFile(_hmap, 0x000F001F, 0, 0, 0);
			}
			if(_mem == Zero) throw new Win32Exception();
		}

		~NativeSharedMemory() { Close(); }

		public void Close()
		{
			if(_mem != Zero) { Api.UnmapViewOfFile(_mem); _mem = Zero; }
			if(_hmap != Zero) { Api.CloseHandle(_hmap); _hmap = Zero; }
		}
	}

	internal unsafe struct LibSharedMemory
	{
		public Perf.Inst perf;

		static NativeSharedMemory _sm = new NativeSharedMemory("Catkeys_SM_0x10000", 0x10000);

		public static LibSharedMemory* Ptr { get { return (LibSharedMemory*)_sm.mem; } }
	}

	[DebuggerStepThrough]
	public static class Misc
	{
		/// <summary>
		/// Gets the entry assembly of current appdomain.
		/// Normally instead can be used Assembly.GetEntryAssembly(), but it fails if appdomain launched through DoCallBack.
		/// </summary>
		public static Assembly AppdomainAssembly
		{
			get
			{
				if(_appdomainAssembly == null) {
					var asm = Assembly.GetEntryAssembly(); //fails if this domain launched through DoCallBack
					if(asm == null) asm = AppDomain.CurrentDomain.GetAssemblies()[1]; //[0] is mscorlib, 1 should be our assembly
					_appdomainAssembly = asm;
				}
				return _appdomainAssembly;
			}
		}
		static Assembly _appdomainAssembly;

		public static IntPtr GetModuleHandleOf(Type t)
		{
			return t == null ? Zero : Marshal.GetHINSTANCE(t.Module);

			//Tested these to get caller's module without Type parameter:
			//This is dirty/dangerous and 50 times slower: [MethodImpl(MethodImplOptions.NoInlining)] ... return Marshal.GetHINSTANCE(new StackFrame(1).GetMethod().DeclaringType.Module);
			//This is dirty/dangerous, does not support multi-module assemblies and 12 times slower: [MethodImpl(MethodImplOptions.NoInlining)] ... return Marshal.GetHINSTANCE(Assembly.GetCallingAssembly().GetLoadedModules()[0]);
			//This is dirty/dangerous/untested and 12 times slower: [MethodImpl(MethodImplOptions.AggressiveInlining)] ... return Marshal.GetHINSTANCE(MethodBase.GetCurrentMethod().DeclaringType.Module);
		}

		public static IntPtr GetModuleHandleOf(Assembly asm)
		{
			return asm == null ? Zero : Marshal.GetHINSTANCE(asm.GetLoadedModules()[0]);
		}

		public static IntPtr GetModuleHandleOfAppdomainEntryAssembly()
		{
			return GetModuleHandleOf(AppdomainAssembly);
		}

		public static IntPtr GetModuleHandleOfCatkeysDll()
		{
			return Marshal.GetHINSTANCE(typeof(Misc).Module);
		}

		public static IntPtr GetModuleHandleOfExe()
		{
			return Api.GetModuleHandle(null);
		}


		/// <summary>
		/// Gets native icon handle of the entry assembly of current appdomain.
		/// It is the assembly icon, not an icon from managed resources.
		/// Returns Zero if the assembly is without icon.
		/// The icon is extracted first time and then cached; don't destroy it.
		/// </summary>
		/// <param name="size">Icon size, 16 or 32.</param>
		public static IntPtr GetAppIconHandle(int size)
		{
			if(size < 24) return _GetAppIconHandle(ref _AppIcon16, true);
			return _GetAppIconHandle(ref _AppIcon32, false);
		}

		static IntPtr _AppIcon32, _AppIcon16;

		static IntPtr _GetAppIconHandle(ref IntPtr hicon, bool small = false)
		{
			if(hicon == Zero) {
				var asm = Misc.AppdomainAssembly; if(asm == null) return Zero;
				IntPtr hinst = Misc.GetModuleHandleOf(asm);
				int size = small ? 16 : 32;
				hicon = Api.LoadImage(hinst, Api.IDI_APPLICATION, Api.IMAGE_ICON, size, size, Api.LR_SHARED);
				//note:
				//This is not 100% reliable because the icon id 32512 (IDI_APPLICATION) is undocumented.
				//I could not find a .NET method to get icon directly from native resources of assembly.
				//Could use Icon.ExtractAssociatedIcon(asm.Location), but it always gets 32 icon and is several times slower.
				//Also could use PrivateExtractIcons. But it uses file path, not module handle.
				//Also could use the resource emumeration API...
				//Never mind. Anyway, we use hInstance/resId with MessageBoxIndirect (which does not support handles) etc.
				//info: MSDN says that LR_SHARED gets cached icon regardless of size, but it is not true. Caches each size separately. Tested on Win 10, 7, XP.
			}
			return hicon;
		}

		public static void MinimizeMemory()
		{
			//return;
			GC.Collect();
			Api.SetProcessWorkingSetSize(Api.GetCurrentProcess(), (UIntPtr)(~0U), (UIntPtr)(~0U));
		}

		public static unsafe int CharPtrLength(char* p)
		{
			if(p == null) return 0;
			for(int i = 0; ; i++) if(*p == '\0') return i;
		}

		public static unsafe int CharPtrLength(char* p, int nMax)
		{
			if(p == null) return 0;
			for(int i = 0; i < nMax; i++) if(*p == '\0') return i;
			return nMax;
		}

		/// <summary>
		/// Removes '&amp;' characters from string.
		/// Replaces "&amp;&amp;" to "&amp;".
		/// Returns true if s had '&amp;' characters.
		/// </summary>
		/// <remarks>
		/// Character '&amp;' is used to underline next character in displayed text of controls. Two '&amp;' are used to display single '&amp;'.
		/// Normally the underline is displayed only when using the keyboard to select dialog controls.
		/// </remarks>
		public static bool StringRemoveMnemonicUnderlineAmpersand(ref string s)
		{
			if(!Empty(s)) {
				int i = s.IndexOf('&');
				if(i >= 0) {
					i = s.IndexOf_("&&");
					if(i >= 0) s = s.Replace("&&", "\0");
					s = s.Replace("&", "");
					if(i >= 0) s = s.Replace("\0", "&");
					return true;
				}
			}
			return false;
		}
	}

	public static class Debug_
	{
#if DEBUG
		public static void OutLoadedAssemblies()
		{
			AppDomain currentDomain = AppDomain.CurrentDomain;
			Assembly[] assems = currentDomain.GetAssemblies();
			foreach(Assembly assem in assems) {
				OutList(assem.ToString(), assem.CodeBase, assem.Location);
			}
		}
#endif
	}



}
