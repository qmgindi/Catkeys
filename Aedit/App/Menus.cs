﻿using Au;
using Au.Types;
using Au.Controls;
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
using Au.More;
using Au.Tools;
//using System.Linq;

static class Menus
{
	[Command(target = "Files")]
	public static class File
	{
		[Command(target = "", image = "*Typicons.DocumentAdd #008EEE")]
		public static class New
		{
			static FileNode _New(string name) => App.Model.NewItem(name, beginRenaming: true);

			[Command('s', keys = "Ctrl+N", keysText = "Ctrl+N", image = FileNode.c_imageScript)]
			public static void New_script() { _New("Script.cs"); }

			[Command('c', image = FileNode.c_imageClass)]
			public static void New_class() { _New("Class.cs"); }

			[Command('f', image = FileNode.c_imageFolder)]
			public static void New_folder() { _New(null); }
		}

		[Command("Delete...", separator = true, keysText = "Delete", image = "*Typicons.DocumentDelete #FF4040")]
		public static void Delete() { App.Model.DeleteSelected(); }

		[Command(keys = "F2", image = "*BoxIcons.RegularRename #505050")]
		public static void Rename() { App.Model.RenameSelected(); }

		[Command(image = "*RemixIcon.ChatSettingsLine #99BF00")]
		public static void Properties() { App.Model.Properties(); }

		[Command("Copy, paste")]
		public static class CopyPaste
		{
			[Command("Multi-select", checkable = true, image = "*Modern.ListTwo #99BF00", tooltip = "Multi-select (with Ctrl or Shift).\nDouble click to open.")]
			public static void MultiSelect_files() { Panels.Files.TreeControl.SetMultiSelect(toggle: true); }

			[Command("Cu_t", separator = true, keysText = "Ctrl+X")]
			public static void Cut_file() { App.Model.CutCopySelected(true); }

			[Command("Copy", keysText = "Ctrl+C")]
			public static void Copy_file() { App.Model.CutCopySelected(false); }

			[Command("Paste", keysText = "Ctrl+V")]
			public static void Paste_file() { App.Model.Paste(); }

			[Command("Cancel Cut/Copy", keysText = "Esc")]
			public static void CancelCutCopy_file() { App.Model.Uncut(); }

			[Command('r', separator = true)]
			public static void Copy_relative_path() { App.Model.SelectedCopyPath(false); }

			[Command('f')]
			public static void Copy_full_path() { App.Model.SelectedCopyPath(true); }
		}

		[Command("Open, close")]
		public static class OpenClose
		{
			[Command(keysText = "Enter")]
			public static void Open() { App.Model.OpenSelected(1); }

			[Command]
			public static void Open_in_default_app() { App.Model.OpenSelected(3); }

			[Command]
			public static void Select_in_explorer() { App.Model.OpenSelected(4); }

			[Command(separator = true, target = "", keysText = "M-click")]
			public static void Close() { App.Model.CloseEtc(FilesModel.ECloseCmd.CloseSelectedOrCurrent); }

			[Command(target = "")]
			public static void Close_all() { App.Model.CloseEtc(FilesModel.ECloseCmd.CloseAll); }

			[Command(target = "")]
			public static void Collapse_all_folders() { App.Model.CloseEtc(FilesModel.ECloseCmd.CollapseAllFolders); }

			[Command(target = "")]
			public static void Collapse_inactive_folders() { App.Model.CloseEtc(FilesModel.ECloseCmd.CollapseInactiveFolders); }

			[Command(separator = true, target = "", keys = "Ctrl+Tab")]
			public static void Previous_document() { var a = App.Model.OpenFiles; if (a.Count > 1) App.Model.SetCurrentFile(a[1]); }
		}

		//[Command]
		//public static class More
		//{
		//	//[Command("...", separator = true)]
		//	//public static void Print_setup() { }

		//	//[Command("...")]
		//	//public static void Print() { }
		//}

		[Command("Export, import", separator = true)]
		public static class ExportImport
		{
			[Command("Export as .zip...")]
			public static void Export_as_zip() { App.Model.ExportSelected(zip: true); }

			[Command("...")]
			public static void Export_as_workspace() { App.Model.ExportSelected(zip: false); }

			[Command("Import .zip...", separator = true)]
			public static void Import_zip() {
				var d = new OpenFileDialog { Title = "Import .zip", Filter = "Zip files|*.zip" };
				if (d.ShowDialog(App.Wmain) != true) return;
				App.Model.ImportWorkspace(d.FileName);
			}

			[Command("...")]
			public static void Import_workspace() {
				var d = new OpenFileDialog { Title = "Import workspace", Filter = "files.xml|files.xml" };
				if (d.ShowDialog(App.Wmain) != true) return;
				App.Model.ImportWorkspace(pathname.getDirectory(d.FileName));
			}

			[Command("...")]
			public static void Import_files() { App.Model.ImportFiles(); }
		}

		[Command(target = "")]
		public static class Workspace
		{
			[Command]
			public static class Recent_workspaces { }

			[Command("...")]
			public static void Open_workspace() { FilesModel.OpenWorkspaceUI(); }

			[Command("...")]
			public static void New_workspace() { FilesModel.NewWorkspaceUI(); }

			[Command(separator = true, keys = "Ctrl+S", image = "*BoxIcons.RegularSave #EABB00")]
			public static void Save_now() { App.Model?.Save.AllNowIfNeed(); }
		}

		[Command(separator = true, target = "", keysText = "Alt+F4")]
		public static void Close_window() { App.Wmain.Close(); }

		[Command(target = "")]
		public static void Exit() {
			App.Wmain.Hide();
			App.Wmain.Close();
		}
	}

	[Command(target = "Edit")]
	public static class Edit
	{
		[Command(keysText = "Ctrl+Z", image = "*Ionicons.UndoiOS #9F5300")]
		public static void Undo() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_UNDO); }

		[Command(keysText = "Ctrl+Y", image = "*Ionicons.RedoiOS #9F5300")]
		public static void Redo() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_REDO); }

		[Command('t', separator = true, keysText = "Ctrl+X", image = "*Zondicons.EditCut #9F5300")]
		public static void Cut() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_CUT); }

		[Command(keysText = "Ctrl+C", image = "*Material.ContentCopy #9F5300")]
		public static void Copy() { Panels.Editor.ZActiveDoc.ZCopy(); }

		[Command(keysText = "Ctrl+V", image = "*Material.ContentPaste #9F5300")]
		public static void Paste() { Panels.Editor.ZActiveDoc.ZPaste(); }

		[Command(image = "*Material.ForumOutline #9F5300")]
		public static void Forum_copy() { Panels.Editor.ZActiveDoc.ZCopy(forum: true); }

		[Command(separator = true, keys = "Ctrl+F", image = "*Material.FindReplace #008EEE")]
		public static void Find() { Panels.Find.ZCtrlF(Panels.Editor.ZActiveDoc); }

		//[Command(keys = "Ctrl+Shift+F")]
		//public static void Find_in_files() { Panels.Find.ZCtrlF(Panels.Editor.ZActiveDoc, findInFiles: true); }

		[Command(separator = true, keysText = "Ctrl+Space", image = "*FontAwesome.ListUlSolid #B340FF")]
		public static void Autocompletion_list() { CodeInfo.ShowCompletionList(Panels.Editor.ZActiveDoc); }

		[Command(keysText = "Ctrl+Shift+Space", image = "*RemixIcon.ParenthesesLine #B340FF")]
		public static void Parameter_info() { CodeInfo.ShowSignature(); }

		[Command(keysText = "F12, Ctrl+click", image = "*RemixIcon.WalkFill #B340FF")]
		public static void Go_to_definition() { CiGoTo.GoToSymbolFromPos(); }

		[Command(separator = true)]
		public static class Selection
		{
			[Command(keysText = "R-click margin", keys = "Ctrl+/", image = "*BoxIcons.RegularCommentAdd #9F5300")]
			public static void Comment() { Panels.Editor.ZActiveDoc.ZCommentLines(true); }

			[Command(keysText = "R-click margin", keys = "Ctrl+\\", image = "*BoxIcons.RegularCommentMinus #9F5300")]
			public static void Uncomment() { Panels.Editor.ZActiveDoc.ZCommentLines(false); }

			[Command(keysText = "Tab", image = "*Material.FormatIndentIncrease #9F5300")]
			public static void Indent() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_TAB); }

			[Command(keysText = "Shift+Tab", image = "*Material.FormatIndentDecrease #9F5300")]
			public static void Unindent() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_BACKTAB); }

			[Command(keysText = "Ctrl+A")]
			public static void Select_all() { Panels.Editor.ZActiveDoc.Call(Sci.SCI_SELECTALL); }
		}

		//[Command]
		//public static class Convert
		//{
		//}

		[Command]
		public static class View
		{
			[Command(checkable = true, keys = "Ctrl+W", image = "*Codicons.WordWrap #99BF00")]
			public static void Wrap_lines() { SciCode.ZToggleView_call_from_menu_only_(SciCode.EView.Wrap); }

			[Command(checkable = true, image = "*Material.TooltipImageOutline #99BF00")]
			public static void Images_in_code() { SciCode.ZToggleView_call_from_menu_only_(SciCode.EView.Images); }
		}
	}

	[Command(target = "Edit")]
	public static class Code
	{
		[Command("wnd (find window)")]
		public static void wnd() { new Dwnd().Show(); }

		[Command("elm (find UI element)")]
		public static void elm() { Delm.Dialog(); }

		[Command("u_iimage (find image)")]
		public static void uiimage() { new Duiimage().Show(); }

		[Command(separator = true, keysText = "Ctrl+Space in string")]
		public static void Keys() { CiTools.CmdShowKeysWindow(); }

		[Command(keysText = "Ctrl+Space in string")]
		public static void Regex() { CiTools.CmdShowRegexWindow(); }

		[Command(separator = true)]
		public static void Windows_API() { new DWinapi().Show(); }

		[Command(keysText = "Ctrl+Shift+Win+W")]
		public static void Quick_capture() { print.it("Info: To quickly capture a window and insert code to find it etc, move the mouse to the window and press Ctrl+Shift+Win+W."); }

		[Command(separator = true, image = "*Material.CommentEditOutline #B340FF")]
		public static void Add_file_description() { InsertCode.AddFileDescription(); }

		[Command(image = "*Codicons.SymbolClass #B340FF")]
		public static void Add_class_Program() { InsertCode.AddClassProgram(); }
	}

	[Command(target = "Edit")]
	public static class Run
	{
		[Command(keys = "F5", image = "*Codicons.DebugStart #40B000")]
		public static void Run_script() { CompileRun.CompileAndRun(true, App.Model.CurrentFile, runFromEditor: true); }

		[Command(image = "*PicolIcons.BadgeStop #505050")]
		public static void End_task() {
			var f = App.Model.CurrentFile;
			if (f != null) {
				if (f.FindProject(out _, out var fMain)) f = fMain;
				if (App.Tasks.EndTasksOf(f)) return;
			}
			var a = App.Tasks.Items;
			if (a.Count > 0) {
				var m = new popupMenu { RawText = true };
				m.Submenu("End task", m => {
					foreach (var t in a) m[t.f.DisplayName] = o => App.Tasks.EndTask(t);
				});
				m.Show();
			}
		}

		//[Command(image = "")]
		//public static void Pause() { }

		[Command(keys = "F7", image = "*VaadinIcons.Compile #008EEE")]
		public static void Compile() { CompileRun.CompileAndRun(false, App.Model.CurrentFile); }

		[Command("...")]
		public static void Recent() { RecentTT.Show(); }

		[Command(separator = true)]
		public static void Debug_break() {
			InsertCode.UsingDirective("System.Diagnostics");
			InsertCode.Statements("if(Debugger.IsAttached) Debugger.Break(); else Debugger.Launch();");
		}
	}

	[Command(target = ""/*, tooltip = "Triggers and toolbars"*/)] //FUTURE: support tooltip for menu items
	public static class TT
	{
		//[Command("...")]
		//public static void Add_trigger() { TriggersAndToolbars.AddTrigger(); }

		//[Command("...")]
		//public static void Add_toolbar() { TriggersAndToolbars.AddToolbar(); }

		[Command('k'/*, separator = true*/)]
		public static void Edit_hotkey_triggers() { TriggersAndToolbars.Edit(@"Triggers\Hotkey triggers.cs"); }

		[Command('a')]
		public static void Edit_autotext_triggers() { TriggersAndToolbars.Edit(@"Triggers\Autotext triggers.cs"); }

		[Command('m')]
		public static void Edit_mouse_triggers() { TriggersAndToolbars.Edit(@"Triggers\Mouse triggers.cs"); }

		[Command('w')]
		public static void Edit_window_triggers() { TriggersAndToolbars.Edit(@"Triggers\Window triggers.cs"); }

		[Command(separator = true)]
		public static void Edit_common_toolbars() { TriggersAndToolbars.Edit(@"Toolbars\Common toolbars.cs"); }

		[Command()]
		public static void Edit_window_toolbars() { TriggersAndToolbars.Edit(@"Toolbars\Window toolbars.cs"); }

		[Command(separator = true)]
		public static void Edit_TT_script() { TriggersAndToolbars.Edit(@"Triggers and toolbars.cs"); }

		[Command()]
		public static void Restart_TT_script() { TriggersAndToolbars.Restart(); }

		[Command(separator = true)]
		public static void Disable_triggers() { TriggersAndToolbars.DisableTriggers(null); }

		//[Command("...")]
		//public static void Active_triggers() {  }

		[Command("...")]
		public static void Active_toolbars() { TriggersAndToolbars.ShowActiveTriggers(); }
	}

	[Command(target = "")]
	public static class Tools
	{
		[Command(image = "*PicolIcons.Settings #99BF00")]
		public static void Options() { DOptions.ZShow(); }

		[Command(image = "*FontAwesome.IconsSolid #EABB00")]
		public static void Icons() { DIcons.ZShow(); }

		[Command(separator = true, target = "Output")]
		public static class Output
		{
			[Command(keysText = "M-click")]
			public static void Clear() { Panels.Output.ZClear(); }

			[Command("Copy", keysText = "Ctrl+C")]
			public static void Copy_output() { Panels.Output.ZCopy(); }

			[Command(keys = "Ctrl+F")]
			public static void Find_selected_text() { Panels.Output.ZFind(); }

			[Command]
			public static void History() { Panels.Output.ZHistory(); }

			[Command("Wrap lines", separator = true, checkable = true)]
			public static void Wrap_lines_in_output() { Panels.Output.ZWrapLines ^= true; }

			[Command("White space", checkable = true)]
			public static void White_space_in_output() { Panels.Output.ZWhiteSpace ^= true; }

			[Command(checkable = true)]
			public static void Topmost_when_floating() { Panels.Output.ZTopmost ^= true; }
		}
	}

	[Command(target = "")]
	public static class Help
	{
		[Command(image = "*RPGAwesome.Help #BB54FF")]
		public static void Program_help() { HelpUtil.AuHelp(""); }

		[Command(image = "*RPGAwesome.Help #EABB00")]
		public static void Library_help() { HelpUtil.AuHelp("api/"); }

		[Command(keys = "F1", image = "*RPGAwesome.Help #70B0FF")]
		public static void Context_help() {
			var w = Api.GetFocus();
			if (w.ClassNameIs("HwndWrapper*")) {
				//var e = Keyboard.FocusedElement as FrameworkElement;

			} else if (w == Panels.Editor.ZActiveDoc.Hwnd) {
				CiUtil.OpenSymbolOrKeywordFromPosHelp();
			}
		}

		//[Command(separator = true)]
		//public static void Forum() { }

		[Command]
		//public static void Email() { run.itSafe("mailto:support@quickmacros.com?subject=" + App.AppName); }
		public static void Email() { run.itSafe("mailto:support@quickmacros.com?subject=QM3"); } //FUTURE: use the above

		//[Command(separator = true)]
		//public static void About() { }
	}

#if TRACE
	[Command]
	public static void TEST() { Test.FromMenubar(); }

	[Command]
	public static void gc() {
		GC.Collect();
	}
#endif
}
