﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel; //Win32Exception

//using System.Reflection;
//using System.Linq;

using Catkeys;
using static Catkeys.NoClass;
using Util = Catkeys.Util;
using static Catkeys.Util.NoClass;
using Catkeys.Winapi;
using Auto = Catkeys.Automation;

namespace SdkConverter
{
	unsafe partial class Converter
	{
		void _DeclareFunction()
		{
			//now we expect one of:
			//	function declaration, like TYPE __stdcall Func(TYPE a, TYPE b);
			//	global variable, which can be:
			//		previously-defined type, like TYPE var;
			//		now-defined function type, like TYPE (*var)(TYPE a, TYPE b);


			var d = new _FINDTYPEDATA();
			_FindTypename(false, ref d);
			_i++;

			//pointer
			int ptr = 0;
			while(_TokIsChar(_i, '*', '&')) { _i++; ptr++; }

			if(!_TokIsIdent(_i)) {
				//could be 'T (*var)(...);' //0 in SDK
				//_Err(_i, "unexpected");
				_SkipStatement(true);
				return;
			}

			int iCallConv = 0;
			if(_TokIsIdent(_i + 1)) iCallConv = _i++;

			if(!_TokIsChar(_i + 1, '(')) {
				//could be 'T var;' //1 in SDK
				//_Err(_i, "unexpected");
				_i = d.outTypenameToken;
				if(!_ExternConst()) _SkipStatement(true);
				return;
			}

			string name = _TokToString(_i++);

			//skip some
			if(name.IndexOf('_') > 0) {
				if(name.EndsWith_("_UserSize")
					|| name.EndsWith_("_UserMarshal")
					|| name.EndsWith_("_UserUnmarshal")
					|| name.EndsWith_("_UserFree")
					|| name.EndsWith_("_UserSize64")
					|| name.EndsWith_("_UserMarshal64")
					|| name.EndsWith_("_UserUnmarshal64")
					|| name.EndsWith_("_UserFree64")
					) {
					_SkipStatement(false);
					return;
				}
			}

			string callConv = iCallConv == 0 ? "Cdecl" : _ConvertCallConv(iCallConv);
			string returnType, returnAttr = null; bool isHRESULT = false;
			if(ptr == 0 && _TokIs(d.outTypenameToken, "HRESULT")) {
				isHRESULT = true;
				returnType = "int";
			} else {
				returnType = _ConvertTypeName(d.outSym, ref ptr, d.outIsConst, d.outTypenameToken, _TypeContext.Return, out returnAttr);
			}

			string dll, nameInDll = null;
			if(_funcDllMap.TryGetValue(name, out dll)) {
				int i = dll.IndexOf('|');
				if(i > 0) {
					nameInDll = dll.Substring(i + 1);
					dll = dll.Substring(0, i);
					//Out(nameInDll);
				} else if(name.StartsWith_("K32")) {
					//Out(name);
					nameInDll = name;
					name = name.Substring(3);
					if(name.EndsWith("W")) name = name.Remove(name.Length - 1);
					else if(name.EndsWith("A")) {
						_SkipStatement(false);
						return;
					}
				}
			} else {
				bool skip = false;
				if(name.Contains("_")) skip = true; //mostly CRT library functions, X_Proxy/X_Stub, X_UserMarshal and similar
				else if(name.StartsWith_("Dll")) skip = true; //DllInstall, DllRegisterServer etc
				else if(name.StartsWith_("Ndr") || name.StartsWith_("Rpc")) skip = true; //undocumented
				else {
					//Out(name);
					skip = true; //all these in SDK others are undocumented, or documented as deprecated/removed
					_funcUnknownDll.Add(name);
				}

				if(skip) {
					_SkipStatement();
					return;
				}
				dll = "?";
			}

			var sb = _sbFuncTemp;
			sb.Clear();
			sb.Append("\r\n[DllImport(\"");
			sb.Append(dll);
			sb.Append('\"');
			if(nameInDll != null) sb.AppendFormat(", EntryPoint=\"{0}\"", nameInDll);
			if(callConv != null) sb.AppendFormat(", CallingConvention=CallingConvention.{0}", callConv);
			if(isHRESULT) sb.Append(", PreserveSig=true"); //default, but makes clear that it returns HRESULT and easier to change to 'false'
			sb.AppendLine(")]");
			if(returnAttr != null) sb.AppendLine(returnAttr);
			sb.Append("public static extern ");
			sb.Append(returnType);
			sb.Append(' ');
			sb.Append(name);

			_ConvertParameters(sb, name, _TypeContext.Parameter);

			string decl = sb.ToString();
			_func[name] = decl;
			//try { _func.Add(name, decl); }
			//catch { //about 10 in SDK. The second declarations are identical or better (without tagSTRUCT).
			//	Out("----");
			//	Out(name);
			//	Out(_func[name]);
			//	Out(decl);
			//}

			if(!_TokIsChar(_i, ';')) _Err(_i, "unexpected");
		}

		Dictionary<string, string> _funcDllMap;
		List<string> _funcUnknownDll = new List<string>();

		void _FunctionsFinally()
		{
			if(_funcUnknownDll.Count > 50) {
				Out("Warning: too many unknown dll:");
				Out(_funcUnknownDll);
			}
		}
	}
}
