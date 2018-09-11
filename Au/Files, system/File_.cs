﻿//#define TEST_FINDFIRSTFILEEX

using System;
using System.Collections.Generic;
using System.Text;
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
using System.Linq;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	/// <summary>
	/// File system functions.
	/// </summary>
	/// <remarks>
	/// Works with files and directories. Disk drives like @"C:\" or "C:" are directories too.
	/// Extends .NET file system classes such as <see cref="File"/> and <see cref="Directory"/>.
	/// Many functions of this class can be used instead of existing similar .NET functions that are slow, limited or unreliable.
	/// Most functions support only full path. Most of them throw <b>ArgumentException</b> if passed a filename or relative path, ie in "current directory". Using current directory is unsafe; it was relevant only in DOS era.
	/// Most functions support extended-length paths (longer than 259). Such local paths should have @"\\?\" prefix, like @"\\?\C:\...". Such network path should be like @"\\?\UNC\server\share\...". See <see cref="Path_.PrefixLongPath"/>, <see cref="Path_.PrefixLongPathIfNeed"/>. Many functions support long paths even without prefix.
	/// </remarks>
	public static partial class File_
	{
		#region attributes, exists, search, enum

		/// <summary>
		/// Adds SEM_FAILCRITICALERRORS to the error mode of this process, as MSDN recommends. Does this once in appdomain.
		/// It is to avoid unnecessary message boxes when an API tries to access an ejected CD/DVD etc.
		/// </summary>
		static void _DisableDeviceNotReadyMessageBox()
		{
			if(_disabledDeviceNotReadyMessageBox) return;
			var em = Api.GetErrorMode();
			if(0 == (em & Api.SEM_FAILCRITICALERRORS)) Api.SetErrorMode(em | Api.SEM_FAILCRITICALERRORS);
			_disabledDeviceNotReadyMessageBox = true;

			//CONSIDER: SetThreadErrorMode
		}
		static bool _disabledDeviceNotReadyMessageBox;

		/// <summary>
		/// Gets file or directory attributes, size and times.
		/// Returns false if the file/directory does not exist.
		/// Calls API <msdn>GetFileAttributesEx</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If flag UseRawPath not used, supports environment variables (see <see cref="Path_.ExpandEnvVar"/>).</param>
		/// <param name="properties">Receives properties.</param>
		/// <param name="flags"></param>
		/// <exception cref="ArgumentException">Not full path (when not used flag UseRawPath).</exception>
		/// <exception cref="AuException">The file/directory exist but failed to get its properties. Not thrown if used flag DoNotThrow.</exception>
		/// <remarks>
		/// For symbolic links etc, gets properties of the link, not of its target.
		/// You can also get most of these properties with <see cref="EnumDirectory"/>.
		/// </remarks>
		public static unsafe bool GetProperties(string path, out FileProperties properties, FAFlags flags = 0)
		{
			properties = new FileProperties();
			if(0 == (flags & FAFlags.UseRawPath)) path = Path_.LibNormalizeMinimally(path, true); //don't need LibNormalizeExpandEV, the API itself supports .. etc
			_DisableDeviceNotReadyMessageBox();
			if(!Api.GetFileAttributesEx(path, 0, out Api.WIN32_FILE_ATTRIBUTE_DATA d)) {
				if(!_GetAttributesOnError(path, flags, out _, &d)) return false;
			}
			properties.Attributes = d.dwFileAttributes;
			properties.Size = (long)d.nFileSizeHigh << 32 | d.nFileSizeLow;
			properties.LastWriteTimeUtc = DateTime.FromFileTimeUtc(d.ftLastWriteTime);
			properties.CreationTimeUtc = DateTime.FromFileTimeUtc(d.ftCreationTime);
			properties.LastAccessTimeUtc = DateTime.FromFileTimeUtc(d.ftLastAccessTime);
			return true;
		}

		/// <summary>
		/// Gets file or directory attributes.
		/// Returns false if the file/directory does not exist.
		/// Calls API <msdn>GetFileAttributes</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If flag UseRawPath not used, supports environment variables (see <see cref="Path_.ExpandEnvVar"/>).</param>
		/// <param name="attributes">Receives attributes, or 0 if failed.</param>
		/// <param name="flags"></param>
		/// <exception cref="ArgumentException">Not full path (when not used flag UseRawPath).</exception>
		/// <exception cref="AuException">Failed. Not thrown if used flag DoNotThrow.</exception>
		/// <remarks>
		/// For symbolic links etc, gets properties of the link, not of its target.
		/// </remarks>
		public static unsafe bool GetAttributes(string path, out FileAttributes attributes, FAFlags flags = 0)
		{
			attributes = 0;
			if(0 == (flags & FAFlags.UseRawPath)) path = Path_.LibNormalizeMinimally(path, true); //don't need LibNormalizeExpandEV, the API itself supports .. etc
			_DisableDeviceNotReadyMessageBox();
			var a = Api.GetFileAttributes(path);
			if(a == (FileAttributes)(-1)) return _GetAttributesOnError(path, flags, out attributes);
			attributes = a;
			return true;
		}

		static unsafe bool _GetAttributesOnError(string path, FAFlags flags, out FileAttributes attr, Api.WIN32_FILE_ATTRIBUTE_DATA* p = null)
		{
			attr = 0;
			var ec = Native.GetError();
			switch(ec) {
			case Api.ERROR_FILE_NOT_FOUND:
			case Api.ERROR_PATH_NOT_FOUND:
			case Api.ERROR_NOT_READY:
				return false;
			case Api.ERROR_SHARING_VIOLATION: //eg c:\pagefile.sys. GetFileAttributes fails, but FindFirstFile succeeds.
			case Api.ERROR_ACCESS_DENIED: //probably in a protected directory. Then FindFirstFile fails, but try anyway.
				var d = new Api.WIN32_FIND_DATA();
				var hfind = Api.FindFirstFile(path, out d);
				if(hfind != (IntPtr)(-1)) {
					Api.FindClose(hfind);
					attr = d.dwFileAttributes;
					if(p != null) {
						p->dwFileAttributes = d.dwFileAttributes;
						p->nFileSizeHigh = d.nFileSizeHigh;
						p->nFileSizeLow = d.nFileSizeLow;
						p->ftCreationTime = d.ftCreationTime;
						p->ftLastAccessTime = d.ftLastAccessTime;
						p->ftLastWriteTime = d.ftLastWriteTime;
					}
					return true;
				}
				Native.SetError(ec);
				attr = (FileAttributes)(-1);
				break;
			}
			if(0 != (flags & FAFlags.DoNotThrow)) return false;
			throw new AuException(ec, $"*get file attributes: '{path}'");

			//tested:
			//	If the file is in a protected directory, ERROR_ACCESS_DENIED.
			//	If the path is to a non-existing file in a protected directory, ERROR_FILE_NOT_FOUND.
			//	ERROR_SHARING_VIOLATION for C:\pagefile.sys etc.
		}

		/// <summary>
		/// Gets attributes.
		/// Returns false if INVALID_FILE_ATTRIBUTES or if relative path. No exceptions.
		/// </summary>
		static unsafe bool _GetAttributes(string path, out FileAttributes attr, bool useRawPath)
		{
			if(!useRawPath) path = Path_.LibNormalizeMinimally(path, false);
			_DisableDeviceNotReadyMessageBox();
			attr = Api.GetFileAttributes(path);
			if(attr == (FileAttributes)(-1) && !_GetAttributesOnError(path, FAFlags.DoNotThrow, out attr)) return false;
			if(!useRawPath && !Path_.IsFullPath(path)) { Native.SetError(Api.ERROR_FILE_NOT_FOUND); return false; }
			return true;
		}

		/// <summary>
		/// Gets file system entry type - file, directory, and whether it exists.
		/// Returns NotFound (0) if does not exist or if fails to get attributes.
		/// Calls API <msdn>GetFileAttributes</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If useRawPath is false (default), supports environment variables (see <see cref="Path_.ExpandEnvVar"/>). Can be null.</param>
		/// <param name="useRawPath">Pass path to the API as it is, without any normalizing and full-path checking.</param>
		/// <remarks>
		/// Supports <see cref="Native.GetError"/>. If you need exception when fails, instead call <see cref="GetAttributes"/> and check attribute Directory.
		/// Always use full path. If path is not full: if useRawPath is false (default) returns NotFound; if useRawPath is true, searches in "current directory".
		/// </remarks>
		public static FileDir ExistsAs(string path, bool useRawPath = false)
		{
			if(!_GetAttributes(path, out var a, useRawPath)) return FileDir.NotFound;
			var R = (0 != (a & FileAttributes.Directory)) ? FileDir.Directory : FileDir.File;
			return R;
		}

		/// <summary>
		/// Gets file system entry type - file, directory, symbolic link, whether it exists and is accessible.
		/// Returns NotFound (0) if does not exist. Returns AccessDenied (&lt; 0) if exists but this process cannot access it and get attributes.
		/// Calls API <msdn>GetFileAttributes</msdn>.
		/// The same as <see cref="ExistsAs"/> but provides more complete result. In most cases you can use ExistsAs, it's simpler.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If useRawPath is false (default), supports environment variables (see <see cref="Path_.ExpandEnvVar"/>). Can be null.</param>
		/// <param name="useRawPath">Pass path to the API as it is, without any normalizing and full-path checking.</param>
		/// <remarks>
		/// Supports <see cref="Native.GetError"/>. If you need exception when fails, instead call <see cref="GetAttributes"/> and check attributes Directory and ReparsePoint.
		/// Always use full path. If path is not full: if useRawPath is false (default) returns NotFound; if useRawPath is true, searches in "current directory".
		/// </remarks>
		public static unsafe FileDir2 ExistsAs2(string path, bool useRawPath = false)
		{
			if(!_GetAttributes(path, out var a, useRawPath)) {
				return (a == (FileAttributes)(-1)) ? FileDir2.AccessDenied : FileDir2.NotFound;
			}
			var R = (0 != (a & FileAttributes.Directory)) ? FileDir2.Directory : FileDir2.File;
			if(0 != (a & FileAttributes.ReparsePoint)) R |= (FileDir2)4;
			return R;
		}

		/// <summary>
		/// Returns true if file or directory exists.
		/// Calls <see cref="ExistsAs2"/>, which calls API <msdn>GetFileAttributes</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If useRawPath is false (default), supports environment variables (see <see cref="Path_.ExpandEnvVar"/>). Can be null.</param>
		/// <param name="useRawPath">Pass path to the API as it is, without any normalizing and full-path checking.</param>
		/// <remarks>
		/// Supports <see cref="Native.GetError"/>. If you need exception when fails, instead call <see cref="GetAttributes"/>.
		/// Always use full path. If path is not full: if useRawPath is false (default) returns NotFound; if useRawPath is true, searches in "current directory".
		/// For symbolic links etc, returns true if the link exists. Does not care whether its target exists.
		/// Unlike <see cref="ExistsAsFile"/> and <see cref="ExistsAsDirectory"/>, this function returns true when the file exists but cannot get its attributes. Then <c>ExistsAsAny(path)</c> is not the same as <c>ExistsAsFile(path) || ExistsAsDirectory(path)</c>.
		/// </remarks>
		public static bool ExistsAsAny(string path, bool useRawPath = false)
		{
			return ExistsAs2(path, useRawPath) != FileDir2.NotFound;
		}

		/// <summary>
		/// Returns true if file exists and is not a directory.
		/// Returns false if does not exist or if fails to get its attributes.
		/// Calls <see cref="ExistsAs"/>, which calls API <msdn>GetFileAttributes</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If useRawPath is false (default), supports environment variables (see <see cref="Path_.ExpandEnvVar"/>). Can be null.</param>
		/// <param name="useRawPath">Pass path to the API as it is, without any normalizing and full-path checking.</param>
		/// <remarks>
		/// Supports <see cref="Native.GetError"/>. If you need exception when fails, instead call <see cref="GetAttributes"/> and check attribute Directory.
		/// Always use full path. If path is not full: if useRawPath is false (default) returns NotFound; if useRawPath is true, searches in "current directory".
		/// For symbolic links etc, returns true if the link exists and its target is not a directory. Does not care whether its target exists.
		/// </remarks>
		public static bool ExistsAsFile(string path, bool useRawPath = false)
		{
			var R = ExistsAs(path, useRawPath);
			if(R == FileDir.File) return true;
			if(R != FileDir.NotFound) Native.ClearError();
			return false;
		}

		/// <summary>
		/// Returns true if directory (folder or drive) exists.
		/// Returns false if does not exist or if fails to get its attributes.
		/// Calls <see cref="ExistsAs"/>, which calls API <msdn>GetFileAttributes</msdn>.
		/// </summary>
		/// <param name="path">Full path. Supports @"\.." etc. If useRawPath is false (default), supports environment variables (see <see cref="Path_.ExpandEnvVar"/>). Can be null.</param>
		/// <param name="useRawPath">Pass path to the API as it is, without any normalizing and full-path checking.</param>
		/// <remarks>
		/// Supports <see cref="Native.GetError"/>. If you need exception when fails, instead call <see cref="GetAttributes"/> and check attribute Directory.
		/// Always use full path. If path is not full: if useRawPath is false (default) returns NotFound; if useRawPath is true, searches in "current directory".
		/// For symbolic links etc, returns true if the link exists and its target is a directory. Does not care whether its target exists.
		/// </remarks>
		public static bool ExistsAsDirectory(string path, bool useRawPath = false)
		{
			var R = ExistsAs(path, useRawPath);
			if(R == FileDir.Directory) return true;
			if(R != FileDir.NotFound) Native.ClearError();
			return false;
		}

		/// <summary>
		/// Finds file or directory and returns full path.
		/// Returns null if cannot be found.
		/// If the path argument is full path, calls <see cref="ExistsAsAny"/> and returns normalized path if exists, null if not.
		/// Else searches in these places:
		///	1. dirs, if used.
		/// 2. <see cref="Folders.ThisApp"/>.
		/// 3. Calls API <msdn>SearchPath</msdn>, which searches in process directory, Windows system directories, current directory, PATH environment variable. The search order depends on API <msdn>SetSearchPathMode</msdn> or registry settings.
		/// 4. If path ends with ".exe", tries to get path from registry "App Paths" keys.
		/// </summary>
		/// <param name="path">Full or relative path or just filename with extension. Supports network paths too.</param>
		/// <param name="dirs">0 or more directories where to search.</param>
		public static unsafe string SearchPath(string path, params string[] dirs)
		{
			if(Empty(path)) return null;

			string s = path;
			if(Path_.IsFullPathExpandEnvVar(ref s)) {
				if(ExistsAsAny(s)) return Path_.LibNormalize(s, noExpandEV: true);
				return null;
			}

			if(dirs != null) {
				foreach(var d in dirs) {
					s = d;
					if(!Path_.IsFullPathExpandEnvVar(ref s)) continue;
					s = Path_.Combine(s, path);
					if(ExistsAsAny(s)) return Path_.LibNormalize(s, noExpandEV: true);
				}
			}

			s = Folders.ThisApp + path;
			if(ExistsAsAny(s)) return Path_.LibNormalize(s, noExpandEV: true);

			for(int na = 300; ;) {
				var b = Util.Buffers.LibChar(ref na);
				int nr = Api.SearchPath(null, path, null, na, b, null);
				if(nr > na) na = nr; else if(nr > 0) return b.ToString(nr); else break;
			}

			if(path.EndsWith_(".exe", true) && path.IndexOfAny(String_.Lib.pathSep) < 0) {
				try {
					string rk = @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + path;
					if(Registry_.GetString(out path, "", rk) || Registry_.GetString(out path, "", rk, Registry.LocalMachine)) {
						path = Path_.Normalize(path.Trim('\"'));
						if(ExistsAsAny(path, true)) return path;
					}
				}
				catch(Exception ex) { Debug_.Print(path + "    " + ex.Message); }
			}

			return null;
		}

		/// <summary>
		/// Gets names and other info of files and subdirectories in the specified directory.
		/// Returns an enumerable collection of <see cref="FEFile"/> objects containing the info.
		/// By default gets only direct children. Use flag <see cref="FEFlags.AndSubdirectories"/> to get all descendants.
		/// </summary>
		/// <param name="directoryPath">Full path of the directory.</param>
		/// <param name="flags"></param>
		/// <param name="filter">
		/// Callback function. Called for each file and subdirectory.
		/// If it returns false, the file/subdirectory is not included in results.
		/// This can be useful when EnumDirectory is called indirectly, for example by the Copy method. If you call it directly, you can instead skip processing the file in your foreach loop.
		/// </param>
		/// <param name="errorHandler">
		/// Callback function. Called when fails to get children of a subdirectory, when using flag <see cref="FEFlags.AndSubdirectories"/>.
		/// It receives the subdirectory path. It can call <see cref="Native.GetError"/> and throw an exception.
		/// If it does not throw an exception, the enumeration continues as if the directory is empty.
		/// If errorHandler not used, then throws exception.
		/// Read more in Remarks.
		/// </param>
		/// <exception cref="ArgumentException">directoryPath is invalid path or not full path.</exception>
		/// <exception cref="DirectoryNotFoundException">directoryPath directory does not exist.</exception>
		/// <exception cref="AuException">Failed to get children of directoryPath or of a subdirectory. Read more in Remarks.</exception>
		/// <remarks>
		/// Uses API <msdn>FindFirstFile</msdn>.
		/// 
		/// The paths that this function gets are normalized, ie may not start with exact directoryPath string. Expanded environment variables (see <see cref="Path_.ExpandEnvVar"/>), "..", DOS path etc.
		/// Paths longer than <see cref="Path_.MaxDirectoryPathLength"/> have @"\\?\" prefix (see <see cref="Path_.PrefixLongPathIfNeed"/>).
		/// For symbolic links and mounted folders, gets info of the link/folder and not of its target.
		/// 
		/// These errors are ignored:
		/// 1. Access denied (usually because of security permissions), unless used flag FailIfAccessDenied.
		/// 2. Missing target directory of a symbolic link or mounted folder.
		/// When an error is ignored, the function works as if that [sub]directory is empty; does not throw exception and does not call errorHandler.
		/// 
		/// Enumeration of a subdirectory starts immediately after the subdirectory itself is retrieved.
		/// </remarks>
		public static IEnumerable<FEFile> EnumDirectory(string directoryPath, FEFlags flags = 0, Func<FEFile, bool> filter = null, Action<string> errorHandler = null)
		{
			string path = directoryPath;
			if(0 == (flags & FEFlags.UseRawPath)) path = Path_.Normalize(path);
			if(path.EndsWith_('\\')) path = path.Remove(path.Length - 1);

			_DisableDeviceNotReadyMessageBox();

			var d = new Api.WIN32_FIND_DATA();
			IntPtr hfind = default;
			var stack = new Stack<_EDStackEntry>();
			bool isFirst = true;
			FileAttributes attr = 0;
			int basePathLength = path.Length;
			var redir = new Misc.DisableRedirection();

			try {
				if(0 != (flags & FEFlags.DisableRedirection)) redir.Disable();

				for(;;) {
					if(isFirst) {
						isFirst = false;
						var path2 = ((path.Length <= Path_.MaxDirectoryPathLength - 2) ? path : Path_.PrefixLongPath(path)) + @"\*";
#if TEST_FINDFIRSTFILEEX
						hfind = Api.FindFirstFileEx(path2, Api.FINDEX_INFO_LEVELS.FindExInfoBasic, out d, 0, default, 0);
						//speed: FindFirstFileEx 0-2 % slower. FindExInfoBasic makes 0-2% faster. FIND_FIRST_EX_LARGE_FETCH makes 1-50% slower.
#else
						hfind = Api.FindFirstFile(path2, out d);
#endif
						if(hfind == (IntPtr)(-1)) {
							hfind = default;
							var ec = Native.GetError();
							//Print(ec, Native.GetErrorMessage(ec), path);
							bool itsOK = false;
							switch(ec) {
							case Api.ERROR_FILE_NOT_FOUND:
							case Api.ERROR_NO_MORE_FILES:
							case Api.ERROR_NOT_READY:
								//rare, because most directories have "." and "..".
								//FindFirstFileEx sets ERROR_NO_MORE_FILES for some.
								//FindFirstFile could set ERROR_FILE_NOT_FOUND (documented), but was never in my tests.
								//ERROR_NOT_READY when trying to access an ejected CD/DVD drive etc.
								itsOK = true;
								break;
							case Api.ERROR_ACCESS_DENIED:
								itsOK = 0 != (flags & FEFlags.IgnoreAccessDeniedErrors);
								break;
							case Api.ERROR_PATH_NOT_FOUND: //the directory not found, or symlink target directory is missing
							case Api.ERROR_DIRECTORY: //it is file, not directory. Error text is "The directory name is invalid".
							case Api.ERROR_BAD_NETPATH: //eg \\COMPUTER\MissingFolder
								if(stack.Count == 0 && !ExistsAsDirectory(path, true))
									throw new DirectoryNotFoundException($"Directory not found: '{path}'. {Native.GetErrorMessage(ec)}");
								//itsOK = (attr & Api.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
								itsOK = true; //or maybe the subdirectory was deleted after we retrieved it
								break;
							case Api.ERROR_INVALID_NAME: //eg contains invalid characters
							case Api.ERROR_BAD_NET_NAME: //eg \\COMPUTER
								if(stack.Count == 0)
									throw new ArgumentException(Native.GetErrorMessage(ec));
								itsOK = true;
								break;
							}
							if(!itsOK) {
								if(errorHandler == null || stack.Count == 0) throw new AuException(ec, $"*enumerate directory '{path}'");
								Api.SetLastError(ec); //the above code possibly changed it, although currently it doesn't
								errorHandler(path);
							}
						}
					} else {
						if(!Api.FindNextFile(hfind, out d)) {
							Debug.Assert(Native.GetError() == Api.ERROR_NO_MORE_FILES);
							Api.FindClose(hfind);
							hfind = default;
						}
					}

					if(hfind == default) {
						if(stack.Count == 0) break;
						var t = stack.Pop();
						hfind = t.hfind; path = t.path;
						continue;
					}

					var name = d.Name;
					if(name == null) continue; //".", ".."
					attr = d.dwFileAttributes;
					bool isDir = (attr & FileAttributes.Directory) != 0;

					if((flags & FEFlags.SkipHidden) != 0 && (attr & FileAttributes.Hidden) != 0) continue;
					const FileAttributes hidSys = FileAttributes.Hidden | FileAttributes.System;
					if((attr & hidSys) == hidSys) {
						if((flags & FEFlags.SkipHiddenSystem) != 0) continue;
						//skip Recycle Bin etc. It is useless, prevents copying drives, etc.
						if(isDir && path.EndsWith_(':')) {
							if(name.Equals_("$Recycle.Bin", true)) continue;
							if(name.Equals_("System Volume Information", true)) continue;
							if(name.Equals_("Recovery", true)) continue;
						}
					}

					var fullPath = path + @"\" + name;
					if(0 != (flags & FEFlags.NeedRelativePaths)) name = fullPath.Substring(basePathLength);

					//prepend @"\\?\" etc if need. Don't change fullPath length, because then would be difficult to get relative path.
					var fp2 = Path_.PrefixLongPathIfNeed(fullPath);

					var r = new FEFile(name, fp2, d, stack.Count);

					if(filter != null && !filter(r)) continue;

					yield return r;

					if(!isDir || (flags & FEFlags.AndSubdirectories) == 0 || r._skip) continue;
					if((attr & FileAttributes.ReparsePoint) != 0 && (flags & FEFlags.AndSymbolicLinkSubdirectories) == 0) continue;
					stack.Push(new _EDStackEntry() { hfind = hfind, path = path });
					hfind = default; path = fullPath;
					isFirst = true;
				}
			}
			finally {
				if(hfind != default) Api.FindClose(hfind);
				while(stack.Count > 0) Api.FindClose(stack.Pop().hfind);

				redir.Revert();
			}
		}

		struct _EDStackEntry { internal IntPtr hfind; internal string path; }

		#endregion

		#region move, copy, rename, delete

		enum _FileOpType { Rename, Move, Copy, }

		static unsafe void _FileOp(_FileOpType opType, bool into, string path1, string path2, IfExists ifExists, FCFlags copyFlags, Func<FEFile, bool> filter)
		{
			string opName = (opType == _FileOpType.Rename) ? "rename" : ((opType == _FileOpType.Move) ? "move" : "copy");
			path1 = _PreparePath(path1);
			var type1 = ExistsAs2(path1, true);
			if(type1 <= 0) throw new FileNotFoundException($"Failed to {opName}. File not found: '{path1}'");

			if(opType == _FileOpType.Rename) {
				opType = _FileOpType.Move;
				if(Path_.IsInvalidFileName(path2)) throw new ArgumentException($"Invalid filename: '{path2}'");
				path2 = Path_.LibCombine(_RemoveFilename(path1), path2);
			} else {
				string path2Parent;
				if(into) {
					path2Parent = _PreparePath(path2);
					path2 = Path_.LibCombine(path2Parent, _GetFilename(path1));
				} else {
					path2 = _PreparePath(path2);
					path2Parent = _RemoveFilename(path2, true);
				}
				if(path2Parent != null) {
					try { _CreateDirectory(path2Parent, pathIsPrepared: true); }
					catch(Exception ex) { throw new AuException($"*create directory: '{path2Parent}'", ex); }
				}
			}

			bool ok = false, copy = opType == _FileOpType.Copy, deleteSource = false, mergeDirectory = false;
			var del = new _SafeDeleteExistingDirectory();
			try {
				if(ifExists == IfExists.MergeDirectory && type1 != FileDir2.Directory) ifExists = IfExists.Fail;

				if(ifExists == IfExists.Fail) {
					//API will fail if exists. We don't use use API flags 'replace existing'.
				} else {
					//Delete, RenameExisting, IfExists
					//bool deleted = false;
					var existsAs = ExistsAs2(path2, true);
					switch(existsAs) {
					case FileDir2.NotFound:
						//deleted = true;
						break;
					case FileDir2.AccessDenied:
						break;
					default:
						if(Misc.LibIsSameFile(path1, path2)) {
							//eg renaming "file.txt" to "FILE.txt"
							Debug_.Print("same file");
							//deleted = true;
							//copy will fail, move will succeed
						} else if(ifExists == IfExists.MergeDirectory && (existsAs == FileDir2.Directory || existsAs == FileDir2.SymLinkDirectory)) {
							if(type1 == FileDir2.Directory || type1 == FileDir2.SymLinkDirectory) {
								//deleted = true;
								mergeDirectory = true;
								if(!copy) { copy = true; deleteSource = true; }
							} // else API will fail. We refuse to replace a directory with a file.
						} else if(ifExists == IfExists.RenameExisting || existsAs == FileDir2.Directory) {
							//deleted = 
							del.Rename(path2, ifExists == IfExists.RenameExisting);
							//Rename to a temp name. Finally delete if ok (if !RenameExisting), undo if failed.
							//It also solves this problem: if we delete the directory now, need to ensure that it does not delete the source directory, which is quite difficult.
						} else {
							//deleted = 0 ==
							_DeleteLL(path2, existsAs == FileDir2.SymLinkDirectory);
						}
						break;
					}
					//if(!deleted) throw new AuException(Api.ERROR_FILE_EXISTS, $"*{opName}"); //don't need, later API will fail
				}

				if(!copy) {
					//note: don't use MOVEFILE_COPY_ALLOWED, because then moving directory to another drive fails with ERROR_ACCESS_DENIED and we don't know that the reason is different drive
					if(ok = Api.MoveFileEx(path1, path2, 0)) return;
					if(Native.GetError() == Api.ERROR_NOT_SAME_DEVICE) {
						copy = true;
						deleteSource = true;
					}
				}

				if(copy) {
					if(type1 == FileDir2.Directory) {
						try {
							_CopyDirectory(path1, path2, mergeDirectory, copyFlags, filter);
							ok = true;
						}
						catch(Exception ex) when(opType != _FileOpType.Copy) {
							throw new AuException($"*{opName} '{path1}' to '{path2}'", ex);
							//FUTURE: test when it is ThreadAbortException
						}
					} else {
						if(type1 == FileDir2.SymLinkDirectory)
							ok = Api.CreateDirectoryEx(path1, path2, default);
						else
							ok = Api.CopyFileEx(path1, path2, null, default, null, Api.COPY_FILE_FAIL_IF_EXISTS | Api.COPY_FILE_COPY_SYMLINK);
					}
				}

				if(!ok) throw new AuException(0, $"*{opName} '{path1}' to '{path2}'");

				if(deleteSource) {
					try {
						_Delete(path1);
					}
					catch(Exception ex) {
						if(!path1.EndsWith_(':')) //moving drive contents. Deleted contents but cannot delete drive.
							PrintWarning($"Failed to delete '{path1}' after copying it to another drive. {ex.Message}");
						//throw new AuException("*move", ex); //don't. MoveFileEx also succeeds even if fails to delete source.
					}
				}
			}
			finally {
				//AuDialog.Show();
				del.Finally(ok);
			}
		}

		//note: if merge, the destination directory must exist
		static unsafe void _CopyDirectory(string path1, string path2, bool merge, FCFlags copyFlags, Func<FEFile, bool> filter)
		{
			//FUTURE: add progressInterface parameter. Create a default interface implementation class that supports progress dialog and/or progress in taskbar button. Or instead create a ShellCopy function.
			//FUTURE: maybe add errorHandler parameter. Call it here when fails to copy, and also pass to EnumDirectory which calls it when fails to enum.

			//use intermediate array, and get it before creating path2 directory. It requires more memory, but is safer. Without it, eg bad things happen when copying a directory into itself.
			var edFlags = FEFlags.AndSubdirectories | FEFlags.NeedRelativePaths | FEFlags.UseRawPath | (FEFlags)copyFlags;
			var a = EnumDirectory(path1, edFlags, filter).ToArray();

			bool ok = false;
			string s1 = null, s2 = null;
			if(!merge) {
				if(!path1.EndsWith_(@":\")) ok = Api.CreateDirectoryEx(path1, path2, default);
				if(!ok) ok = Api.CreateDirectory(path2, default);
				if(!ok) goto ge;
			}

			//foreach(var f in EnumDirectory(path1, edFlags, filter)) { //no, see comments above
			foreach(var f in a) {
				s1 = f.FullPath; s2 = Path_.PrefixLongPathIfNeed(path2 + f.Name);
				//Print(s1, s2);
				//continue;
				if(f.IsDirectory) {
					if(merge) switch(ExistsAs(s2, true)) {
						case FileDir.Directory: continue; //never mind: check symbolic link mismatch
						case FileDir.File: _DeleteLL(s2, false); break;
						}

					ok = Api.CreateDirectoryEx(s1, s2, default);
					if(!ok && 0 == (f.Attributes & FileAttributes.ReparsePoint)) ok = Api.CreateDirectory(s2, default);
				} else {
					if(merge && GetAttributes(s2, out var attr, FAFlags.DoNotThrow | FAFlags.UseRawPath)) {
						const FileAttributes badAttr = FileAttributes.ReadOnly | FileAttributes.Hidden;
						if(0 != (attr & FileAttributes.Directory)) _Delete(s2);
						else if(0 != (attr & badAttr)) Api.SetFileAttributes(s2, attr & ~badAttr);
					}

					uint fl = Api.COPY_FILE_COPY_SYMLINK; if(!merge) fl |= Api.COPY_FILE_FAIL_IF_EXISTS;
					ok = Api.CopyFileEx(s1, s2, null, default, null, fl);
				}
				if(!ok) {
					if(0 != (f.Attributes & FileAttributes.ReparsePoint)) {
						//To create or copy symbolic links, need SeCreateSymbolicLinkPrivilege privilege.
						//Admins have it, else this process cannot get it.
						//More info: MS technet -> "Create symbolic links".
						//Debug_.Print($"failed to copy symbolic link '{s1}'. It's OK, skipped it. Error: {Native.GetErrorMessage()}");
						continue;
					}
					if(0 != (copyFlags & FCFlags.IgnoreAccessDeniedErrors)) {
						if(Native.GetError() == Api.ERROR_ACCESS_DENIED) continue;
					}
					goto ge;
				}
			}
			return;
			ge:
			string se = $"*copy directory '{path1}' to '{path2}'";
			if(s1 != null) se += $" ('{s1}' to '{s2}')";
			throw new AuException(0, se);
			//never mind: wrong API error code if path1 and path2 is the same directory.
		}

		struct _SafeDeleteExistingDirectory
		{
			string _oldPath, _tempPath;
			bool _doNotDelete;

			/// <summary>
			/// note: path must be normalized.
			/// </summary>
			internal bool Rename(string path, bool doNotDelete = false)
			{
				if(path.Length > Path_.MaxDirectoryPathLength - 10) path = Path_.PrefixLongPath(path);
				string tempPath = null;
				int iFN = _FindFilename(path);
				string s1 = path.Remove(iFN) + "old", s2 = " " + path.Substring(iFN);
				for(int i = 1; ; i++) {
					tempPath = s1 + i + s2;
					if(!ExistsAsAny(tempPath, true)) break;
				}
				if(!Api.MoveFileEx(path, tempPath, 0)) return false;
				_oldPath = path; _tempPath = tempPath; _doNotDelete = doNotDelete;
				return true;
			}

			internal bool Finally(bool succeeded)
			{
				if(_tempPath == null) return true;
				if(!succeeded) {
					if(!Api.MoveFileEx(_tempPath, _oldPath, 0)) return false;
				} else if(!_doNotDelete) {
					try { _Delete(_tempPath); } catch { return false; }
				}
				_oldPath = _tempPath = null;
				return true;
			}
		}

		/// <summary>
		/// Renames file or directory.
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="newName">New name without path. Example: "name.txt".</param>
		/// <param name="ifExists"></param>
		/// <exception cref="ArgumentException">
		/// path is not full path (see <see cref="Path_.IsFullPath"/>).
		/// newName is invalid filename.
		/// </exception>
		/// <exception cref="FileNotFoundException">The file (path) does not exist or cannot be found.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Uses API <msdn>MoveFileEx</msdn>.
		/// </remarks>
		public static void Rename(string path, string newName, IfExists ifExists = IfExists.Fail)
		{
			_FileOp(_FileOpType.Rename, false, path, newName, ifExists, 0, null);
		}

		/// <summary>
		/// Moves (changes path of) file or directory.
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="newPath">
		/// New full path.
		/// <note type="note">It is not the new parent directory. Use <see cref="MoveTo"/> for it.</note>
		/// </param>
		/// <param name="ifExists"></param>
		/// <exception cref="ArgumentException">path or newPath is not full path (see <see cref="Path_.IsFullPath"/>).</exception>
		/// <exception cref="FileNotFoundException">The source file (path) does not exist or cannot be found.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// In most cases uses API <msdn>MoveFileEx</msdn>. It's fast, because don't need to copy files.
		/// In these cases copies/deletes: destination is on another drive; need to merge directories.
		/// When need to copy, does not copy the security properties; sets default security properties.
		/// 
		/// Creates the destination directory if does not exist (see <see cref="CreateDirectory"/>).
		/// If path and newPath share the same parent directory, just renames the file.
		/// </remarks>
		public static void Move(string path, string newPath, IfExists ifExists = IfExists.Fail)
		{
			_FileOp(_FileOpType.Move, false, path, newPath, ifExists, 0, null);
		}

		/// <summary>
		/// Moves file or directory into another directory.
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="newDirectory">New parent directory.</param>
		/// <param name="ifExists"></param>
		/// <exception cref="ArgumentException">
		/// path or newDirectory is not full path (see <see cref="Path_.IsFullPath"/>).
		/// path is drive. To move drive content, use <see cref="Move"/>.
		/// </exception>
		/// <exception cref="FileNotFoundException">The source file (path) does not exist or cannot be found.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// In most cases uses API <msdn>MoveFileEx</msdn>. It's fast, because don't need to copy files.
		/// In these cases copies/deletes: destination is on another drive; need to merge directories.
		/// When need to copy, does not copy the security properties; sets default security properties.
		/// 
		/// Creates the destination directory if does not exist (see <see cref="CreateDirectory"/>).
		/// </remarks>
		public static void MoveTo(string path, string newDirectory, IfExists ifExists = IfExists.Fail)
		{
			_FileOp(_FileOpType.Move, true, path, newDirectory, ifExists, 0, null);
		}

		/// <summary>
		/// Copies file or directory.
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="newPath">
		/// New full path.
		/// <note type="note">It is not the new parent directory. Use <see cref="CopyTo"/> for it.</note>
		/// </param>
		/// <param name="ifExists"></param>
		/// <param name="copyFlags">Options used when copying directory.</param>
		/// <param name="filter">
		/// This callback function can be used when copying directory. Called for each descendant file and subdirectory.
		/// If it returns false, the file/subdirectory is not copied.
		/// </param>
		/// <exception cref="ArgumentException">path or newPath is not full path (see <see cref="Path_.IsFullPath"/>).</exception>
		/// <exception cref="FileNotFoundException">The source file (path) does not exist or cannot be found.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Uses API <msdn>CopyFileEx</msdn>.
		/// On Windows 7 does not copy the security properties; sets default security properties.
		/// Does not copy symbolic links (silently skips, no exception) if this process is not running as administrator.
		/// Creates the destination directory if does not exist (see <see cref="CreateDirectory"/>).
		/// </remarks>
		public static void Copy(string path, string newPath, IfExists ifExists = IfExists.Fail, FCFlags copyFlags = 0, Func<FEFile, bool> filter = null)
		{
			_FileOp(_FileOpType.Copy, false, path, newPath, ifExists, copyFlags, filter);
		}

		/// <summary>
		/// Copies file or directory into another directory.
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="newDirectory">New parent directory.</param>
		/// <param name="ifExists"></param>
		/// <param name="copyFlags">Options used when copying directory.</param>
		/// <param name="filter"><inheritdoc cref="Copy"/></param>
		/// <exception cref="ArgumentException">
		/// path or newDirectory is not full path (see <see cref="Path_.IsFullPath"/>).
		/// path is drive. To copy drive content, use <see cref="Copy"/>.
		/// </exception>
		/// <exception cref="FileNotFoundException">The source file (path) does not exist or cannot be found.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Uses API <msdn>CopyFileEx</msdn>.
		/// On Windows 7 does not copy the security properties; sets default security properties.
		/// Does not copy symbolic links (silently skips, no exception) if this process is not running as administrator.
		/// Creates the destination directory if does not exist (see <see cref="CreateDirectory"/>).
		/// </remarks>
		public static void CopyTo(string path, string newDirectory, IfExists ifExists = IfExists.Fail, FCFlags copyFlags = 0, Func<FEFile, bool> filter = null)
		{
			_FileOp(_FileOpType.Copy, true, path, newDirectory, ifExists, copyFlags, filter);
		}

		/// <summary>
		/// Deletes file or directory.
		/// Does nothing if it does not exist (no exception).
		/// </summary>
		/// <param name="path">Full path.</param>
		/// <param name="tryRecycleBin">
		/// Send to the Recycle Bin. If not possible, delete anyway.
		/// Why could be not possible: 1. The file is in a removable drive (most removables don't have a recycle bin). 2. The file is too large. 3. The path is too long. 4. The Recycle Bin is not used on that drive (it can be set in the Recycle Bin Properties dialog). 5. This process is non-UI-interactive, eg a service. 6. Unknown reasons.
		/// Note: it is much slower. To delete multiple, use <see cref="Delete(IEnumerable{string}, bool)"/>.
		/// </param>
		/// <exception cref="ArgumentException">path is not full path (see <see cref="Path_.IsFullPath"/>).</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// If directory, also deletes all its files and subdirectories. If fails to delete some, tries to delete as many as possible.
		/// Deletes read-only files too.
		/// Does not show any message boxes etc (confirmation, error, UAC consent, progress).
		/// 
		/// Some reasons why this function can fail:
		/// 1. The file is open (in any process). Or a file in the directory is open.
		/// 2. This process does not have security permissions to access or delete the file or directory or some of its descendants.
		/// 3. The directory is (or contains) the "current directory" (in any process).
		/// </remarks>
		public static void Delete(string path, bool tryRecycleBin = false)
		{
			path = _PreparePath(path);
			_Delete(path, tryRecycleBin);
		}

		/// <summary>
		/// Deletes multiple files or/and directories.
		/// The same as <see cref="Delete(string, bool)"/>, but faster when using Recycle Bin.
		/// </summary>
		/// <param name="paths">string array, List or other collection. Full paths.</param>
		/// <param name="tryRecycleBin"></param>
		public static void Delete(IEnumerable<string> paths, bool tryRecycleBin = false)
		{
			if(tryRecycleBin) {
				var a = new List<string>();
				foreach(var v in paths) {
					var s = _PreparePath(v);
					if(ExistsAsAny(s, true)) a.Add(s);
				}
				if(a.Count == 0) return;
				if(_DeleteShell(null, true, a)) return;
				Debug_.Print("_DeleteShell failed");
			}
			foreach(var v in paths) Delete(v);
		}

		/// <summary>
		/// note: path must be normalized.
		/// </summary>
		static FileDir2 _Delete(string path, bool tryRecycleBin = false)
		{
			var type = ExistsAs2(path, true);
			if(type == FileDir2.NotFound) return type;
			if(type == FileDir2.AccessDenied) throw new AuException(0, $"*delete '{path}'");

			if(tryRecycleBin) {
				if(_DeleteShell(path, true)) return type;
				Debug_.Print("_DeleteShell failed");
			}

			int ec = 0;
			if(type == FileDir2.Directory) {
				var dirs = new List<string>();
				foreach(var f in EnumDirectory(path, FEFlags.AndSubdirectories | FEFlags.UseRawPath | FEFlags.IgnoreAccessDeniedErrors)) {
					if(f.IsDirectory) dirs.Add(f.FullPath);
					else _DeleteLL(f.FullPath, false); //delete as many as possible
				}
				for(int i = dirs.Count - 1; i >= 0; i--) {
					_DeleteLL(dirs[i], true); //delete as many as possible
				}
				ec = _DeleteLL(path, true);
				if(ec == 0) {
					//notify shell. Else, if it was open in Explorer, it shows an error message box.
					//Info: .NET does not notify; SHFileOperation does.
					_ShellNotify(Api.SHCNE_RMDIR, path);
					return type;
				}
				Debug_.Print("Using _DeleteShell.");
				//if(_DeleteShell(path, Recycle.No)) return type;
				if(_DeleteShell(path, false)) return type;
			} else {
				ec = _DeleteLL(path, type == FileDir2.SymLinkDirectory);
				if(ec == 0) return type;
			}
			if(ExistsAsAny(path, true)) throw new AuException(ec, $"*delete '{path}'");

			//info:
			//RemoveDirectory fails if not empty.
			//Directory.Delete fails if a descendant is read-only, etc.
			//Also both fail if a [now deleted] subfolder containing files was open in Explorer. Workaround: sleep(10) and retry.
			//_DeleteShell does not have these problems. But it is very slow.
			//But all fail if it is current directory in any process. If in current process, _DeleteShell succeeds; it makes current directory = its parent.
			return type;
		}

		static int _DeleteLL(string path, bool dir)
		{
			//Print(dir, path);
			if(dir ? Api.RemoveDirectory(path) : Api.DeleteFile(path)) return 0;
			var ec = Native.GetError();
			if(ec == Api.ERROR_ACCESS_DENIED) {
				var a = Api.GetFileAttributes(path);
				if(a != (FileAttributes)(-1) && 0 != (a & FileAttributes.ReadOnly)) {
					Api.SetFileAttributes(path, a & ~FileAttributes.ReadOnly);
					if(dir ? Api.RemoveDirectory(path) : Api.DeleteFile(path)) return 0;
					ec = Native.GetError();
				}
			}
			if(ec == Api.ERROR_DIR_NOT_EMPTY && Api.PathIsDirectoryEmpty(path)) {
				//see comments above about Explorer
				Debug_.Print("ERROR_DIR_NOT_EMPTY when empty");
				for(int i = 0; i < 5; i++) {
					Thread.Sleep(15);
					if(Api.RemoveDirectory(path)) return 0;
				}
				ec = Native.GetError();
			}
			if(ec == Api.ERROR_FILE_NOT_FOUND || ec == Api.ERROR_PATH_NOT_FOUND) return 0;
			Debug_.Print("_DeleteLL failed. " + Native.GetErrorMessage(ec) + "  " + path
				+ (dir ? ("   Children: " + string.Join(" | ", EnumDirectory(path).Select(f => f.Name))) : null));
			return ec;

			//never mind: .NET also calls DeleteVolumeMountPoint if it is a mounted folder.
			//	But I did not find in MSDN doc that need to do it before calling removedirectory. I think OS should unmount automatically.
			//	Tested on Win10, works without unmounting explicitly. Even Explorer updates its current folder without notification.
		}

		static bool _DeleteShell(string path, bool recycle, List<string> a = null)
		{
			if(a != null) path = string.Join("\0", a);
			if(Wildex.HasWildcards(path)) throw new ArgumentException("*? not supported.");
			var x = new Api.SHFILEOPSTRUCT() { wFunc = Api.FO_DELETE };
			uint f = Api.FOF_NO_UI; //info: FOF_NO_UI includes 4 flags - noerrorui, silent, noconfirm, noconfirmmkdir
			if(recycle) f |= Api.FOF_ALLOWUNDO; else f |= Api.FOF_NO_CONNECTED_ELEMENTS;
			x.fFlags = (ushort)f;
			x.pFrom = path + "\0";
			x.hwnd = Wnd.GetWnd.Root;
			var r = Api.SHFileOperation(x);
			//if(r != 0 || x.fAnyOperationsAborted) return false; //do not use fAnyOperationsAborted, it can be true even if finished to delete. Also, I guess it cannot be aborted because there is no UI, because we use FOF_SILENT to avoid deactivating the active window even when the UI is not displayed.
			//if(r != 0) return false; //after all, I don't trust this too
			//in some cases API returns 0 but does not delete. For example when path too long.
			if(a == null) {
				if(ExistsAsAny(path, true)) return false;
			} else {
				foreach(var v in a) if(ExistsAsAny(v, true)) return false;
			}
			return true;

			//Also tested IFileOperation, but it is even slower. Also it requires STA, which is not a big problem.

			//Unsuccessfully tried to add flag 'if cannot use Recycle Bin, show a Yes|No message box and delete or fail'.
			//	FOF_WANTNUKEWARNING does not work as it should, eg is ignored when the file is in a flash drive.
			//	It works only without FOF_NOCONFIRMATION, but then always shows confirmation if not disabled in RB Properties.
		}

		/// <summary>
		/// Creates new directory if does not exists.
		/// If need, creates missing parent/ancestor directories.
		/// Returns true if created new directory, false if the directory already exists. Throws exception if fails.
		/// </summary>
		/// <param name="path">Path of new directory.</param>
		/// <param name="templateDirectory">Optional path of a template directory from which to copy some properties. See API <msdn>CreateDirectoryEx</msdn>.</param>
		/// <exception cref="ArgumentException">Not full path.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// If the directory already exists, this function does nothing, and returns false.
		/// Else, at first it creates missing parent/ancestor directories, then creates the specified (path) directory.
		/// To create the specified directory, calls API <msdn>CreateDirectory</msdn> or <msdn>CreateDirectoryEx</msdn> (if templateDirectory is not null).
		/// </remarks>
		public static bool CreateDirectory(string path, string templateDirectory = null)
		{
			return _CreateDirectory(path, templateDirectory: templateDirectory);
		}

		/// <summary>
		/// Creates parent directory for a new file, if does not exist.
		/// The same as <see cref="CreateDirectory"/>, just removes filename from filePath.
		/// </summary>
		/// <param name="filePath">Path of new file.</param>
		/// <exception cref="ArgumentException">Not full path. No filename.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <example>
		/// <code><![CDATA[
		/// string path = @"D:\Test\new\test.txt";
		/// File_.CreateDirectoryFor(path);
		/// File.WriteAllText(path, "text"); //would fail if directory @"D:\Test\new" does not exist
		/// ]]></code>
		/// </example>
		public static void CreateDirectoryFor(string filePath)
		{
			filePath = _PreparePath(filePath);
			var path = _RemoveFilename(filePath);
			_CreateDirectory(path, pathIsPrepared: true);
		}

		static bool _CreateDirectory(string path, bool pathIsPrepared = false, string templateDirectory = null)
		{
			if(ExistsAsDirectory(path, pathIsPrepared)) return false;
			if(!pathIsPrepared) path = _PreparePath(path);
			if(templateDirectory != null) templateDirectory = _PreparePath(templateDirectory);

			var stack = new Stack<string>();
			var s = path;
			do {
				stack.Push(s);
				s = _RemoveFilename(s, true);
				if(s == null) throw new AuException($@"*create directory '{path}'. Drive not found.");
			} while(!ExistsAsDirectory(s, true));

			while(stack.Count > 0) {
				s = stack.Pop();
				int retry = 0;
				g1:
				bool ok = (templateDirectory == null || stack.Count > 0)
					? Api.CreateDirectory(s, default)
					: Api.CreateDirectoryEx(templateDirectory, s, default);
				if(!ok) {
					int ec = Native.GetError();
					if(ec == Api.ERROR_ALREADY_EXISTS) continue;
					if(ec == Api.ERROR_ACCESS_DENIED && ++retry < 5) { Thread.Sleep(15); goto g1; } //sometimes access denied briefly, eg immediately after deleting the folder while its parent is open in Explorer. Now could not reproduce on Win10.
					throw new AuException(0, $@"*create directory '{path}'");
				}
			}
			return true;
		}

		static void _ShellNotify(uint @event, string path, string path2 = null)
		{
			ThreadPool.QueueUserWorkItem(_ => Api.SHChangeNotify(@event, Api.SHCNF_PATH, path, path2));
		}

		/// <summary>
		/// Expands environment variables (see <see cref="Path_.ExpandEnvVar"/>). Throws ArgumentException if not full path. Normalizes. Removes or adds '\\' at the end.
		/// </summary>
		/// <exception cref="ArgumentException">Not full path.</exception>
		static string _PreparePath(string path)
		{
			if(!Path_.IsFullPathExpandEnvVar(ref path)) throw new ArgumentException($"Not full path: '{path}'.");
			return Path_.LibNormalize(path, noExpandEV: true);
		}

		/// <summary>
		/// Finds filename, eg @"b.txt" in @"c:\a\b.txt".
		/// </summary>
		/// <exception cref="ArgumentException">'\\' not found or is at the end. If noException, instead returns -1.</exception>
		static int _FindFilename(string path, bool noException = false)
		{
			int R = path.LastIndexOfAny(String_.Lib.pathSep);
			if(R < 0 || R == path.Length - 1) {
				if(noException) return -1;
				throw new ArgumentException($"No filename in path: '{path}'.");
			}
			return R + 1;
		}

		/// <summary>
		/// Removes filename, eg @"c:\a\b.txt" -> @"c:\a".
		/// </summary>
		/// <exception cref="ArgumentException">'\\' not found or is at the end. If noException, instead returns null.</exception>
		static string _RemoveFilename(string path, bool noException = false)
		{
			int i = _FindFilename(path, noException); if(i < 0) return null;
			return path.Remove(i - 1);
		}

		/// <summary>
		/// Gets filename, eg @"c:\a\b.txt" -> @"b.txt".
		/// </summary>
		/// <exception cref="ArgumentException">'\\' not found or is at the end. If noException, instead returns null.</exception>
		static string _GetFilename(string path, bool noException = false)
		{
			int i = _FindFilename(path, noException); if(i < 0) return null;
			return path.Substring(i);
		}

		/// <summary>
		/// Returns true if character c == '\\' || c == '/'.
		/// </summary>
		static bool _IsSepChar(char c) { return c == '\\' || c == '/'; }
#endregion
	}
}

namespace Au.Types
{
	/// <summary>
	/// File system entry type - file, directory, and whether it exists.
	/// Returned by <see cref="File_.ExistsAs"/>.
	/// </summary>
	public enum FileDir
	{
		/// <summary>Does not exist, or failed to get attributes.</summary>
		NotFound = 0,
		/// <summary>Is file, or symbolic link to a file.</summary>
		File = 1,
		/// <summary>Is directory, or symbolic link to a directory.</summary>
		Directory = 2,
	}

	/// <summary>
	/// File system entry type - file, directory, symbolic link, whether it exists and is accessible.
	/// Returned by <see cref="File_.ExistsAs2"/>.
	/// The enum value NotFound is 0; AccessDenied is negative ((int)0x80000000); other values are greater than 0.
	/// </summary>
	public enum FileDir2
	{
		/// <summary>Does not exist.</summary>
		NotFound = 0,
		/// <summary>Is file. Attributes: Directory no, ReparsePoint no.</summary>
		File = 1,
		/// <summary>Is directory. Attributes: Directory yes, ReparsePoint no.</summary>
		Directory = 2,
		/// <summary>Is symbolic link to a file. Attributes: Directory no, ReparsePoint yes.</summary>
		SymLinkFile = 5,
		/// <summary>Is symbolic link to a directory, or is a mounted folder. Attributes: Directory yes, ReparsePoint yes.</summary>
		SymLinkDirectory = 6,
		/// <summary>Exists but this process cannot access it and get attributes.</summary>
		AccessDenied = int.MinValue,
	}

	/// <summary>
	/// Flags for <see cref="File_.GetAttributes"/> and some other functions.
	/// </summary>
	[Flags]
	public enum FAFlags
	{
		///<summary>Pass path to the API as it is, without any normalizing and validating.</summary>
		UseRawPath = 1,

		///<summary>
		///If failed, return false and don't throw exception.
		///Then, if you need error info, you can use <see cref="Native.GetError"/>. If the file/directory does not exist, it will return ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND or ERROR_NOT_READY.
		///If failed and the native error code is ERROR_ACCESS_DENIED or ERROR_SHARING_VIOLATION, the returned attributes will be (FileAttributes)(-1). The file probably exists but is protected so that this process cannot access and use it. Else attributes will be 0.
		///</summary>
		DoNotThrow = 2,
	}

	/// <summary>
	/// File or directory properties. Used with <see cref="File_.GetProperties"/>.
	/// </summary>
	public struct FileProperties
	{
		///
		public FileAttributes Attributes;

		///<summary>File size. For directories it is usually 0.</summary>
		public long Size;

		///
		public DateTime LastWriteTimeUtc;

		///
		public DateTime CreationTimeUtc;

		///<summary>Note: this is unreliable. The operating system may not record this time automatically.</summary>
		public DateTime LastAccessTimeUtc;
	}

	/// <summary>
	/// flags for <see cref="File_.EnumDirectory"/>.
	/// </summary>
	[Flags]
	public enum FEFlags
	{
		/// <summary>
		/// Enumerate subdirectories too.
		/// </summary>
		AndSubdirectories = 1,

		/// <summary>
		/// Also enumerate symbolic link and mounted folder target directories. Use with AndSubdirectories.
		/// </summary>
		AndSymbolicLinkSubdirectories = 2,

		/// <summary>
		/// Skip files and subdirectories that have Hidden attribute.
		/// </summary>
		SkipHidden = 4,

		/// <summary>
		/// Skip files and subdirectories that have Hidden and System attributes (both).
		/// These files/directories usually are created and used only by the operating system. Drives usually have several such directories. Another example - thumbnail cache files.
		/// Without this flag the function skips only these hidden-system root directories when enumerating a drive: "$Recycle.Bin", "System Volume Information", "Recovery". If you want to include them too, use network path of the drive, for example @"\\localhost\D$\" for D drive.
		/// </summary>
		SkipHiddenSystem = 8,

		/// <summary>
		/// If fails to get contents of the directory or a subdirectory because of its security settings, assume that the [sub]directory is empty.
		/// Without this flag then throws exception or calls errorHandler.
		/// </summary>
		IgnoreAccessDeniedErrors = 0x10,

		/// <summary>
		/// Temporarily disable file system redirection in this thread of this 32-bit process running on 64-bit Windows.
		/// Then you can enumerate the 64-bit System32 folder in your 32-bit process.
		/// Uses API <msdn>Wow64DisableWow64FsRedirection</msdn>.
		/// For vice versa (in 64-bit process enumerate the 32-bit System folder), instead use path Folders.SystemX86.
		/// </summary>
		DisableRedirection = 0x20,

		/// <summary>
		/// Don't call <see cref="Path_.Normalize"/>(directoryPath) and don't throw exception for non-full path.
		/// </summary>
		UseRawPath = 0x40,

		/// <summary>
		/// Let <see cref="FEFile.Name"/> be path relative to the specified directory path. Like @"\name.txt" or @"\subdirectory\name.txt" instead of "name.txt".
		/// </summary>
		NeedRelativePaths = 0x80,
	}

	/// <summary>
	/// flags for <see cref="File_.Copy"/> and some other similar functions.
	/// Used only when copying directory.
	/// </summary>
	[Flags]
	public enum FCFlags
	{
		//note: these values must match the corresponding FEFlags values.

		/// <summary>
		/// Skip descendant files and directories that have Hidden and System attributes (both).
		/// They usually are created and used only by the operating system. Drives usually have several such directories. Another example - thumbnail cache files.
		/// They often are protected and would fail to copy, ruining whole copy operation.
		/// Without this flag the function skips only these hidden-system root directories when enumerating a drive: "$Recycle.Bin", "System Volume Information", "Recovery".
		/// </summary>
		SkipHiddenSystem = 8,

		/// <summary>
		/// If fails to get contents of the directory or a subdirectory because of its security settings, don't throw exception but assume that the [sub]directory is empty.
		/// </summary>
		IgnoreAccessDeniedErrors = 0x10,
	}

	/// <summary>
	/// Contains name and other main properties of a file or subdirectory retrieved by <see cref="File_.EnumDirectory"/>.
	/// The values are not changed after creating the variable.
	/// </summary>
	public class FEFile
	{
		///
		internal FEFile(string name, string fullPath, in Api.WIN32_FIND_DATA d, int level)
		{
			Name = name; FullPath = fullPath;
			Attributes = d.dwFileAttributes;
			Size = (long)d.nFileSizeHigh << 32 | d.nFileSizeLow;
			LastWriteTimeUtc = DateTime.FromFileTimeUtc(d.ftLastWriteTime); //fast, sizeof 8
			CreationTimeUtc = DateTime.FromFileTimeUtc(d.ftCreationTime);
			_level = (short)level;
		}

		///
		public string Name { get; }

		///
		public string FullPath { get; }

		/// <summary>
		/// Returns file size. For directories it is usually 0.
		/// </summary>
		public long Size { get; }

		///
		public DateTime LastWriteTimeUtc { get; }

		///
		public DateTime CreationTimeUtc { get; }

		///
		public FileAttributes Attributes { get; }

		/// <summary>
		/// Returns true if is directory or symbolic link to a directory or mounted folder.
		/// </summary>
		public bool IsDirectory { get { return (Attributes & FileAttributes.Directory) != 0; } }

		/// <summary>
		/// Descendant level.
		/// 0 if direct child of directoryPath, 1 if child of child, an so on.
		/// </summary>
		public int Level { get { return _level; } }
		short _level;

		/// <summary>
		/// Call this function if don't want to enumerate children of this subdirectory.
		/// </summary>
		public void SkipThisDirectory() { _skip = true; }
		internal bool _skip;

		/// <summary>
		/// Returns FullPath.
		/// </summary>
		public override string ToString() { return FullPath; }

		//This could be more dangerous than useful.
		///// <summary>
		///// Returns FullPath.
		///// </summary>
		//public static implicit operator string(FEFile f) { return f?.FullPath; }
	}

	/// <summary>
	/// What to do if the destination directory contains a file or directory with the same name as the source file or directory when copying, moving or renaming.
	/// Used with <see cref="File_.Copy"/>, <see cref="File_.Move"/> and similar functions.
	/// When renaming or moving, if the destination is the same as the source, these options are ignored and the destination is simply renamed. For example when renaming "file.txt" to "FILE.TXT".
	/// </summary>
	public enum IfExists
	{
		/// <summary>Throw exception. Default.</summary>
		Fail,
		/// <summary>Delete destination.</summary>
		Delete,
		/// <summary>Rename (backup) destination.</summary>
		RenameExisting,
		/// <summary>
		/// If destination directory exists, merge the source directory into it, replacing existing files.
		/// If destination file exists, deletes it.
		/// If destination directory exists and source is file, fails.
		/// </summary>
		MergeDirectory,
#if not_implemented
			/// <summary>Copy/move with a different name.</summary>
			RenameNew,
			/// <summary>Display a dialog asking the user what to do.</summary>
			AskUser,
#endif
	}
}