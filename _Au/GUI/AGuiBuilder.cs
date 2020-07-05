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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Xml.Linq;
using System.Windows.Data;

using Au;
using Au.Types;

//Problem: Roslyn/VS on exception in a chained method gives line number of the chain start, not of the method.

/// <summary>
/// With this class you can create windows with controls, for example for data input.
/// </summary>
/// <remarks>
/// This class uses WPF (Windows Presentation Foundation). It is in .NET. Creates window at run time. No designer. No WPF and XAML knowledge required, unless you want something advanced.
/// 
/// To start, use snippet guiBuilderSnippet.
/// 
/// Most functions return <c>this</c>, to enable method chaining, aka fluent interface, like with <b>StringBuilder</b>. See example.
/// 
/// An <b>AGuiBuilder</b> object can be used to create whole window or some window part, for example a tab page.
/// 
/// The size/position unit in WPF is about 1/96 inch, regardless of screen DPI. For example, if DPI is 96 (100%), 1 unit = 1 physical pixel; if 150% - 1.5 pixel; if 200% - 2 pixels. WPF windows are DPI-scaled automatically when need. Your program's manifest should contain dpiAware=true/PM and dpiAwareness=PerMonitorV2; it is default for scripts/programs created with the script editor of this library.
/// 
/// Note: WPF starts slowly and uses much memory. It is normal if to show the first window in process takes 500-1000 ms and the process uses 30 MB of memory, whereas WinForms takes 250 ms / 10 MB and native takes 50 ms / 2 MB. However WinForms becomes slower than WPF if there are more than 100 controls in window. This library uses WPF because it is the most powerful and works well with high DPI screens.
/// 
/// WPF has many control types, for example <see cref="Button"/>, <see cref="CheckBox"/>, <see cref="TextBox"/>, <see cref="ComboBox"/>, <see cref="Label"/>. Most are in namespaces <b>System.Windows.Controls</b> and <b>System.Windows.Controls.Primitives</b>. Also on the internet you can find many libraries containing WPF controls and themes. For example, search for <i>github awesome dotnet C#</i>. Many libraries are open-source, and most can be found in GitHub (source, info and sometimes compiled files). Compiled files usually can be downloaded from <see href="https://www.nuget.org/"/> as packages. A nupkg file is a zip file. Extract it and use the dll file. Also take the xml and pdb files if available. Note: use .NET Core libraries, not old .NET Framework 4.x libraries. Many libraries have both versions. If original library is only Framework 4, look in NuGet for its Core version.
/// 
/// By default don't need XAML. When need, you can load XAML strings and files with <see cref="System.Windows.Markup.XamlReader"/>. For example when you want to apply a theme from a library or add something to resources. See examples.
/// </remarks>
/// <example>
/// Dialog window with several controls for data input (code from guiBuilderSnippet).
/// <code><![CDATA[
/// var b = new AGuiBuilder("Example").WinSize(400) //create Window object with Grid control; set window width 400
/// 	.R.Add("Text", out TextBox text1).Focus() //add label and text box control in first row
/// 	.R.Add("Combo", out ComboBox combo1).Items("One|Two|Three") //in second row add label and combo box control with items
/// 	.R.Add(out CheckBox c1, "Check") //in third row add check box control
/// 	.R.AddOkCancel() //finally add standard OK and Cancel buttons
/// 	.End();
/// if (!b.ShowDialog()) return; //show the dialog and wait until closed; return if closed not with OK button
/// AOutput.Write(text1.Text, combo1.SelectedIndex, c1.IsCheck()); //get user input from control variables
/// ]]></code>
/// Dialog window with TabControl (code from guiBuilderSnippet).
/// <code><![CDATA[
/// var b = new AGuiBuilder("Window").WinSize(400)
/// 	.Row(-1).Add(out TabControl tc).Height(300..)
/// 	.R.StartOkCancel().AddOkCancel().AddButton("Apply", null).Width(70).Disabled().End();
/// 
/// AGuiBuilder _Page(string name, GBPanelType panelType = GBPanelType.Grid) {
/// 	var tp = new TabItem { Header = name };
/// 	tc.Items.Add(tp);
/// 	return new AGuiBuilder(tp, panelType);
/// }
/// 
/// _Page("Page1")
/// 	.R.Add("Text", out TextBox _)
/// 	.End();
/// 
/// _Page("Page2")
/// 	.R.Add("Combo", out ComboBox _).Editable().Items("One|Two|Three")
/// 	.R.Add(out CheckBox _, "Check")
/// 	.End();
/// 
/// //tc.SelectedIndex = 1;
/// 
/// b.End();
/// if (!b.ShowDialog()) return;
/// ]]></code>
/// Apply theme from downloaded library HandyControl.
/// Also add HandyControl.dll reference.
/// <code><![CDATA[
/// var xaml = @"<Application xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:hc='https://handyorg.github.io/handycontrol'>
///   <Application.Resources>
///   <ResourceDictionary>
///     <ResourceDictionary.MergedDictionaries>
///       <ResourceDictionary Source='pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml'/>
///       <ResourceDictionary Source='pack://application:,,,/HandyControl;component/Themes/Theme.xaml'/>
///     </ResourceDictionary.MergedDictionaries>
///   </ResourceDictionary>
///   </Application.Resources>
/// </Application>";
/// 
/// System.Windows.Markup.XamlReader.Parse(xaml); //creates and sets Application object for this process. Its resources will be used by WPF windows.
/// 
/// new AGuiBuilder("Example").AddOkCancel().ShowDialog();
/// ]]></code>
/// </example>
public class AGuiBuilder
{
	//readonly FrameworkElement _container; //now used only in ctor
	readonly Window _window; //= _container or null
	_PanelBase _p; //current grid/stack/dock/canvas panel, either root or nested

	abstract class _PanelBase
	{
		protected readonly AGuiBuilder _b;
		public readonly _PanelBase parent;
		public Panel panel; //or Grid etc
		public FrameworkElement lastAdded;
		public bool ended;

		protected _PanelBase(AGuiBuilder b, Panel p)
		{
			_b = b;
			parent = b._p;
			lastAdded = panel = p;
		}

		public virtual void BeforeAdd(GBAdd flags = 0)
		{
			if (ended) throw new InvalidOperationException("Cannot add after End()");
			if (flags.Has(GBAdd.ChildOfLast) && (object)lastAdded == panel) throw new ArgumentException("Last element is panel.", "flag ChildOfLast");
		}

		public virtual void Add(FrameworkElement c)
		{
			panel.Children.Add(lastAdded = c);
		}

		public virtual void End() { ended = true; }

		public FrameworkElement LastDirect
		{
			get
			{
				for (var c = lastAdded; ;)
				{
					var pa = c.Parent as FrameworkElement;
					if ((object)pa == panel) return c;
					c = pa;
				}
			}
		}
	}

	class _Canvas : _PanelBase
	{
		public _Canvas(AGuiBuilder b) : base(b, new Canvas())
		{
			panel.HorizontalAlignment = HorizontalAlignment.Left;
			panel.VerticalAlignment = VerticalAlignment.Top;
		}
	}

	class _DockPanel : _PanelBase
	{
		public _DockPanel(AGuiBuilder b) : base(b, new DockPanel())
		{
		}
	}

	class _StackPanel : _PanelBase
	{
		public _StackPanel(AGuiBuilder b, bool vertical) : base(b, new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal })
		{
		}
	}

	class _Grid : _PanelBase
	{
		readonly Grid _grid; //same as panel, just to avoid casting everywhere
		int _row = -1, _col;
		bool _isSpan;
		double? _andWidth;

		public _Grid(AGuiBuilder b) : base(b, new Grid())
		{
			_grid = panel as Grid;
			if (GridLines) _grid.ShowGridLines = true;
		}

		public void Row(GBGridLength height)
		{
			if (_andWidth != null) throw new InvalidOperationException("And().Row()");
			if (_row >= 0)
			{
				_SetLastSpan();
				_col = 0;
			}
			else if (_grid.ColumnDefinitions.Count == 0)
			{
				_grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Auto) });
				_grid.ColumnDefinitions.Add(new ColumnDefinition());
			}
			_row++;
			_grid.RowDefinitions.Add(height.Row);
		}

		public override void BeforeAdd(GBAdd flags = 0)
		{
			base.BeforeAdd(flags);
			if (flags.Has(GBAdd.ChildOfLast)) return;
			if (_row < 0 || _col >= _grid.ColumnDefinitions.Count) Row(0);
			_isSpan = false;
		}

		public override void Add(FrameworkElement c)
		{
			if (_andWidth != null)
			{
				var width = _andWidth.Value; _andWidth = null;
				if (width < 0) { var m = c.Margin; m.Left += -width + 3; c.Margin = m; } else { c.Width = width; c.HorizontalAlignment = HorizontalAlignment.Right; }
				var last = LastDirect;
				Grid.SetColumn(c, Grid.GetColumn(last));
				Grid.SetColumnSpan(c, Grid.GetColumnSpan(last));
				_isSpan = true;
			}
			else
			{
				Grid.SetColumn(c, _col);
			}
			_col++;
			Grid.SetRow(c, _row);
			base.Add(c);
		}

		public void And(double width)
		{
			if (_col == 0 || _andWidth != null) throw new InvalidOperationException("And()");
			var c = LastDirect;
			if (width < 0) { c.Width = -width; c.HorizontalAlignment = HorizontalAlignment.Left; } else { var m = c.Margin; m.Right += width + 3; c.Margin = m; }
			_andWidth = width;
			_col--;
		}

		public void Span(int span)
		{
			if (_col == 0) throw new InvalidOperationException("Span() at row start");
			int cc = _grid.ColumnDefinitions.Count;
			_col--;
			if (span != 0)
			{ //if 0, will add 2 controls in 1 cell
				if (span < 0 || _col + span > cc) span = cc - _col;
				Grid.SetColumnSpan(LastDirect, span);
				_col += span;
			}
			_isSpan = true;
		}

		//If not all row cells filled, let the last control span all remaining cells, unless its span specified explicitly.
		void _SetLastSpan()
		{
			if (!_isSpan && _row >= 0)
			{
				int n = _grid.ColumnDefinitions.Count - _col;
				if (n > 0) Grid.SetColumnSpan(LastDirect, n + 1);
			}
			_isSpan = false;
		}

		public void Skip(int span = 1)
		{
			BeforeAdd();
			_col += span;
			_isSpan = true;
		}

		public override void End()
		{
			base.End();
			_SetLastSpan();
		}

		public (int column, int row) NextCell => (_col, _row);
	}

	#region current panel

	/// <summary>
	/// Ends adding controls etc to the window or nested panel (<see cref="StartGrid"/> etc).
	/// </summary>
	/// <remarks>
	/// Always call this method to end a nested panel. For root panel it is optional if using <see cref="ShowDialog"/>.
	/// </remarks>
	public AGuiBuilder End()
	{
		if (!_p.ended)
		{
			_p.End();
			if (_p.parent != null)
			{
				_p = _p.parent;
			}
			else
			{

			}
		}
		return this;
	}

	/// <summary>
	/// Sets column count and widths of current grid.
	/// </summary>
	/// <param name="widths">
	/// Column widths.
	/// An argument can be:
	/// - an integer or double value specifies <see cref="ColumnDefinition.Width"/>. Value 0 means auto-size. Negative value is star-width (*), ie fraction of total width of star-sized columns. Examples: <c>50</c>, <c>-0.5</c>.
	/// - a range specifies <see cref="ColumnDefinition.MinWidth"/> and/or <see cref="ColumnDefinition.MaxWidth"/> and sets width value = -1 (star-sized). Examples: <c>50..150</c>, <c>50..</c> or <c>..150</c>.
	/// - tuple (double value, Range minMax) specifies width and min/max widths. Example: <c>(-2, 50..)</c>.
	/// - <see cref="ColumnDefinition"/> can specify these and more properties.
	/// </param>
	/// <exception cref="InvalidOperationException">Columns() in non-grid panel or after an <b>Add</b> function.</exception>
	/// <remarks>
	/// If this function not called, the table has 2 columns like <c>.Columns(0, -1)</c>.
	/// 
	/// If there are star-sized columns, grid width should be defined. Call <see cref="Width"/> or <see cref="Size"/>. But it the grid is in a cell of another grid, usually it's better to set column width of that grid to a non-zero value, ie let it be not auto-sized.
	/// </remarks>
	public AGuiBuilder Columns(params GBGridLength[] widths)
	{
		var g = Last as Grid ?? throw new InvalidOperationException("Columns() in wrong place");
		g.ColumnDefinitions.Clear();
		foreach (var v in widths) g.ColumnDefinitions.Add(v.Column);
		return this;
	}

	/// <summary>
	/// Starts new row in current grid.
	/// </summary>
	/// <param name="height">
	/// Row height. Can be:
	/// - integer or double value specifies <see cref="RowDefinition.Height"/>. Value 0 means auto-size. Negative value is star-width (*), ie fraction of total height of star-sized rows. Examples: <c>50</c>, <c>-0.5</c>.
	/// - range specifies <see cref="RowDefinition.MinHeight"/> and/or <see cref="RowDefinition.MaxHeight"/> and sets height value = -1 (star-sized). Examples: <c>50..150</c>, <c>50..</c> or <c>..150</c>.
	/// - tuple (double value, Range minMax) specifies height and min/max heights. Example: <c>(-2, 50..200)</c>.
	/// - <see cref="RowDefinition"/> can specify these and more properties.
	/// </param>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	/// <remarks>
	/// Calling this function is optional, except when not all cells of previous row are explicitly filled.
	/// 
	/// If there are star-sized rows, grid height should be defined. Call <see cref="Height"/> or <see cref="Size"/>. But it the grid is in a cell of another grid, usually it's better to set row height of that grid to a non-zero value, ie let it be not auto-sized.
	/// </remarks>
	public AGuiBuilder Row(GBGridLength height)
	{
		if (_p.ended) throw new InvalidOperationException("Row() after End()");
		var g = _p as _Grid ?? throw new InvalidOperationException("Row() in non-grid panel");
		g.Row(height);
		return this;
	}

	/// <summary>
	/// Starts new auto-sized row in current grid. The same as <c>Row(0)</c>. See <see cref="Row"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	public AGuiBuilder R => Row(0);

	#endregion

	#region ctors, window

	//	static readonly DependencyProperty _AGuiBuilderProperty = DependencyProperty.RegisterAttached("_AGuiBuilder", typeof(AGuiBuilder), typeof(Panel));
	static ConditionalWeakTable<Panel, AGuiBuilder> s_cwt = new ConditionalWeakTable<Panel, AGuiBuilder>();
	//which is better? Both fast.

	/// <summary>
	/// This constructor creates <see cref="Window"/> object with panel of specified type (default is <see cref="Grid"/>).
	/// </summary>
	/// <param name="windowTitle">Window title bar text.</param>
	/// <param name="panelType">Panel type. Default is <see cref="Grid"/>. Later you also can add nested panels of various types with <b>StartX</b> functions.</param>
	public AGuiBuilder(string windowTitle, GBPanelType panelType = GBPanelType.Grid)
	{
		/*_container=*/
		_window = new Window() { Title = windowTitle };
		_AddRootPanel(_window, false, panelType, true);
	}

	/// <summary>
	/// This constructor creates panel of specified type (default is <see cref="Grid"/>) and optionally adds to a container.
	/// </summary>
	/// <param name="container">
	/// Window or some other element that will contain the panel. Should be empty, unless the type supports multiple immediate child elements. Can be null.
	/// If the type (or base type) is <see cref="ContentControl"/> (<see cref="Window"/>, <see cref="TabItem"/>, ToolTip, etc) or <see cref="Popup"/>, this function adds the panel to it. If <i>container</i> is null or an element of some other type, need to explicitly add the panel to it, like <c>.Also(b => container.Child = b.Panel)</c> or <c>.Also(b => container.Children.Add(b.Panel))</c> or <c>.Tooltip(btt.Panel)</c> or <c>hwndSource.RootVisual = btt.Panel</c> (the code depends on container type).
	/// </param>
	/// <param name="panelType">Panel type. Default is <see cref="Grid"/>. Later you also can add nested panels of various types with <b>StartX</b> functions.</param>
	/// <param name="setProperties">
	/// Set some container's properties like other overload does. Default true. Currently sets these properties, and only if container is <b>Window</b>:
	/// - <see cref="Window.SizeToContent"/>, except when container is <b>Canvas</b> or has properties <b>Width</b> and/or <b>Height</b> set.
	/// - <b>SnapsToDevicePixels</b> = true.
	/// - <b>WindowStartupLocation</b> = Center.
	/// - <b>Topmost</b> and <b>Background</b> depending on static properties <see cref="WinTopmost"/> and <see cref="WinWhite"/>.
	/// </param>
	public AGuiBuilder(FrameworkElement container = null, GBPanelType panelType = GBPanelType.Grid, bool setProperties = true)
	{
		//_container=container; // ?? throw new ArgumentNullException("container"); //can be null
		_window = container as Window;
		_AddRootPanel(container, true, panelType, setProperties);
	}

	void _AddRootPanel(FrameworkElement container, bool externalContainer, GBPanelType panelType, bool setProperties)
	{
		switch (panelType)
		{
			case GBPanelType.Grid:
				_p = new _Grid(this);
				break;
			case GBPanelType.Canvas:
				_p = new _Canvas(this);
				break;
			case GBPanelType.Dock:
				_p = new _DockPanel(this);
				break;
			default:
				_p = new _StackPanel(this, panelType == GBPanelType.VerticalStack);
				break;
		}
		if (_window != null) _p.panel.Margin = new Thickness(3);
		switch (container)
		{
			case ContentControl c: c.Content = _p.panel; break;
			case Popup c: c.Child = _p.panel; break;
				//rejected. Rare. Let users add explicitly, like .Also(b => container.Child = b.Panel).
				//		case Decorator c: c.Child=_p.panel; break;
				//		case Panel c: c.Children.Add(_p.panel); break;
				//		case ItemsControl c: c.Items.Add(_p.panel); break;
				//		case TextBlock c: c.Inlines.Add(_p.panel); break;
				//		default: throw new NotSupportedException("Unsupported container type");
		}
		if (setProperties)
		{
			if (_window != null)
			{
				if (panelType != GBPanelType.Canvas)
				{
					if (externalContainer)
					{
						_window.SizeToContent = (double.IsNaN(_window.Width) ? SizeToContent.Width : 0) | (double.IsNaN(_window.Height) ? SizeToContent.Height : 0);
					}
					else
					{
						_window.SizeToContent = SizeToContent.WidthAndHeight;
					}
				}
				_window.SnapsToDevicePixels = true; //workaround for black line at bottom, for example when there is single CheckBox in Grid.
													//				_window.UseLayoutRounding=true; //not here. Makes many controls bigger by 1 pixel when resizing window with grid. OK if in _Add (for each non-panel element).
				_window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
				if (WinTopmost) _window.Topmost = true;
				if (!WinWhite) _window.Background = SystemColors.ControlBrush;
			}
		}
		s_cwt.Add(_p.panel, this);
	}

	/// <summary>
	/// Shows dialog.
	/// </summary>
	/// <param name="owner"><see cref="Window.Owner"/>.</param>
	/// <exception cref="InvalidOperationException">
	/// - Container is not Window.
	/// - Missing End() for a StartX() panel.
	/// </exception>
	/// <remarks>
	/// Calls <see cref="End"/>, sets <see cref="Window.Owner"/> and calls <see cref="Window.ShowDialog"/>.
	/// You can instead call these functions directly. Or call <see cref="Window.Show"/> to show as non-modal window, ie don't wait. Or add <see cref="Panel"/> to some container window or other element, etc.
	/// </remarks>
	public bool ShowDialog(Window owner = null)
	{
		_ThrowIfNotWindow();
		if (_IsNested) throw new InvalidOperationException("Missing End() for a StartX() panel");
		End();
		_window.Owner = owner;
		return true == _window.ShowDialog();
	}

	/// <summary>
	/// Sets window's width and/or height or/and min/max width/height.
	/// </summary>
	/// <param name="width">Width or/and min/max width.</param>
	/// <param name="height">Height or/and min/max height.</param>
	/// <exception cref="InvalidOperationException">
	/// - Container is not Window.
	/// - Cannot be after last End().
	/// </exception>
	/// <remarks>
	/// Use WPF logical device-independent units, not real pixels.
	/// </remarks>
	/// <seealso cref="WinSaveRestore"/>
	public AGuiBuilder WinSize(GBLength? width = null, GBLength? height = null)
	{
		_ThrowIfNotWindow();
		if (_IsWindowEnded) throw new InvalidOperationException("WinSize() cannot be after last End()"); //although currently could be anywhere
		var u = _window.SizeToContent;
		if (width != null) { var v = width.Value; v.ApplyTo(_window, false); u &= ~SizeToContent.Width; }
		if (height != null) { var v = height.Value; v.ApplyTo(_window, true); u &= ~SizeToContent.Height; }
		_window.SizeToContent = u;
		return this;
	}

	/// <summary>
	/// Sets window's location when it is loaded.
	/// </summary>
	/// <param name="x">X position in <i>screen</i>. If default - center. You also can use <see cref="Coord.Reverse"/> etc.</param>
	/// <param name="y">Y position in <i>screen</i>. If default - center. You also can use <see cref="Coord.Reverse"/> etc.</param>
	/// <param name="screen"></param>
	/// <param name="workArea">Use the work area, not whole screen. Default true.</param>
	/// <param name="ensureInside">If part of window's rectangle is not in screen, move and/or resize it so that entire rectangle would be in screen. Default true.</param>
	/// <exception cref="InvalidOperationException">Container is not Window.</exception>
	/// <exception cref="NotSupportedException">Window is loaded.</exception>
	/// <remarks>
	/// With this function use real pixels, not WPF logical device-independent units.
	/// Alternatively use <see cref="Window.Left"/> and <b>Window.Top</b> properties of <see cref="Window"/>.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// .WinXY(Coord.Max);
	/// ]]></code>
	/// </example>
	/// <seealso cref="WinSaveRestore"/>
	public AGuiBuilder WinXY(Coord x = default, Coord y = default, AScreen screen = default, bool workArea = true, bool ensureInside = true)
	{
		WinXY(WindowStartupLocation.Manual);
		_window.WindowState = WindowState.Normal;
		_window.Loaded += (o, _) => {
			//			_window.Hwnd().MoveInScreen(x, y, screen, workArea, ensureInside);
			var w = _window.Hwnd();
			RECT r = _Move();
			_window.Dispatcher.InvokeAsync(() => { if (w.Rect != r) _Move(); });
			//repeat if: A. Rescaled when moved to a screen with different DPI. B. Applied Width/Height etc after this.
			//	Would be more efficient to set Left/Top etc in that screen, but difficult because WPF uses different units.

			RECT _Move()
			{
				RECT r = w.Rect;
				r.MoveInScreen(x, y, screen, workArea, ensureInside);
				w.MoveLL(r);
				//				AOutput.Write("_Move", r, w.IsVisible, _window.Height, _window.ActualHeight, _window.DesiredSize, _window.RenderSize);
				return r;
			}
		};
		//		_window.SizeChanged+=(o, e)=> { //called before resizing native window
		//			AOutput.Write("SizeChanged", e.PreviousSize, e.NewSize, _window.Hwnd());
		//		};

		return this;
	}

	/// <summary>
	/// Sets window's <see cref="WindowStartupLocation"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">Container is not Window.</exception>
	/// <exception cref="NotSupportedException">Window is loaded.</exception>
	public AGuiBuilder WinXY(WindowStartupLocation startLocation)
	{
		_ThrowIfNotWindow();
		if (_window.IsLoaded) throw new NotSupportedException("WinXY() when window is loaded");
		_window.WindowStartupLocation = startLocation;
		return this;
	}

	/// <summary>
	/// Saves window xy/size/state when closing and restores when opening.
	/// </summary>
	/// <param name="save">Called when closing the window. Receives XML string containing window xy/size/state. Can save it in registry, file, anywhere.</param>
	/// <param name="saved">String that the <i>save</i> action received previously. Can be null or "", usually first time (still not saved).</param>
	/// <exception cref="InvalidOperationException">
	/// - Container is not Window.
	/// - Cannot be after last End().
	/// </exception>
	/// <remarks>
	/// If you use <see cref="WinSize"/>, call it before. It is used if size is still not saved. The same if you set window position or state.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// string rk = @"HKEY_CURRENT_USER\Software\Au\Test", rv = "winSR";
	/// var b = new AGuiBuilder("Window").WinSize(300)
	/// 	.WinSaveRestore(o => Microsoft.Win32.Registry.SetValue(rk, rv, o), Microsoft.Win32.Registry.GetValue(rk, rv, null) as string)
	/// 	.Row(0).Add("Text", out TextBox _)
	/// 	.R.AddOkCancel()
	/// 	.End();
	/// ]]></code>
	/// </example>
	public AGuiBuilder WinSaveRestore(Action<string> save, string saved)
	{
		_ThrowIfNotWindow();
		if (_IsWindowEnded) throw new InvalidOperationException("WinSaveRestoreRectAndState() cannot be after last End()");
		if (!saved.NE())
		{
			var x = XElement.Parse(saved);
			if (x.Attr(out int left, "left")) { _window.Left = left; _window.WindowStartupLocation = WindowStartupLocation.Manual; }
			if (x.Attr(out int top, "top")) { _window.Top = top; _window.WindowStartupLocation = WindowStartupLocation.Manual; }
			if (x.Attr(out int width, "width")) { _window.Width = width; _window.SizeToContent &= ~SizeToContent.Width; }
			if (x.Attr(out int height, "height")) { _window.Height = height; _window.SizeToContent &= ~SizeToContent.Height; }
			if (x.Attr(out int state, "state")) { _window.WindowState = (WindowState)state; }
			_window.Loaded += (o, _) => {
				(o as Window).Hwnd().EnsureInScreen(); //eg if DPI is different than when saving
			};
		}

		_window.Closing += (o, e) => {
			var x = new XElement("winRect");
			x.SetAttributeValue("left", (int)_window.Left);
			x.SetAttributeValue("top", (int)_window.Top);
			var stc = _window.SizeToContent;
			if (!stc.Has(SizeToContent.Width)) x.SetAttributeValue("width", (int)_window.Width);
			if (!stc.Has(SizeToContent.Height)) x.SetAttributeValue("height", (int)_window.Height);
			var ws = _window.WindowState; if (ws != WindowState.Normal) x.SetAttributeValue("state", (int)ws);
			save(x.ToString());
		};
		return this;
	}

	/// <summary>
	/// Changes various window properties.
	/// </summary>
	/// <param name="resizeMode"><see cref="Window.ResizeMode"/></param>
	/// <param name="showActivated"><see cref="Window.ShowActivated"/></param>
	/// <param name="showInTaskbar"><see cref="Window.ShowInTaskbar"/></param>
	/// <param name="topmost"><see cref="Window.Topmost"/></param>
	/// <param name="state"><see cref="Window.WindowState"/></param>
	/// <param name="style"><see cref="Window.WindowStyle"/></param>
	/// <param name="icon"><see cref="Window.Icon"/>. Example: <c>.WinProperties(icon: BitmapFrame.Create(new Uri(@"d:\icons\file.ico")))</c>.</param>
	/// <param name="whiteBackground">Set background color = <b>SystemColors.WindowBrush</b> (normally white) if true or <b>SystemColors.ControlBrush</b> (dialog color) if false. See also <see cref="WinWhite"/>, <see cref="Brush"/>.</param>
	/// <exception cref="InvalidOperationException">Container is not Window.</exception>
	/// <remarks>
	/// The function uses only non-null parameters.
	/// Or you can change <see cref="Window"/> properties directly, for example <c>.Also(b => { b.Window.Topmost = true; })</c>.
	/// </remarks>
	public AGuiBuilder WinProperties(ResizeMode? resizeMode = null, bool? showActivated = null, bool? showInTaskbar = null, bool? topmost = null, WindowState? state = null, WindowStyle? style = null, ImageSource icon = null, bool? whiteBackground = null)
	{
		_ThrowIfNotWindow();
		//		if(_IsWindowEnded) throw new InvalidOperationException("WinProperties() cannot be after last End()"); //no, can be anywhere
		if (resizeMode.HasValue) _window.ResizeMode = resizeMode.Value;
		if (showActivated.HasValue) _window.ShowActivated = showActivated.Value;
		if (showInTaskbar.HasValue) _window.ShowInTaskbar = showInTaskbar.Value;
		if (topmost.HasValue) _window.Topmost = topmost.Value;
		if (state.HasValue) _window.WindowState = state.Value;
		if (style.HasValue) _window.WindowStyle = style.Value;
		if (whiteBackground.HasValue) _window.Background = whiteBackground.Value ? SystemColors.WindowBrush : SystemColors.ControlBrush;
		if (icon != null) _window.Icon = icon;
		return this;
	}

	#endregion

	#region properties

	/// <summary>
	/// Gets the top-level window.
	/// Returns null if container is not <b>Window</b>.
	/// </summary>
	public Window Window => _window;

	/// <summary>
	/// Gets current <see cref="Grid"/> or <see cref="StackPanel"/> or etc.
	/// </summary>
	public Panel Panel => _p.panel;

	/// <summary>
	/// Gets the last child or descendant element added in current panel. Before that returns current panel.
	/// </summary>
	/// <remarks>
	/// The "set properties of last element" functions set properties of this element.
	/// </remarks>
	public FrameworkElement Last => _p.lastAdded;

	//	not useful
	//	/// <summary>
	//	/// Gets the last direct child element added in current panel. Before that returns current panel or its parent <b>GroupBox</b>.
	//	/// </summary>
	//	public FrameworkElement LastDirect => _p.LastDirect;

	#endregion

	#region static

	/// <summary>
	/// <see cref="Grid.ShowGridLines"/> of grid panels created afterwards.
	/// To be used at design time only.
	/// </summary>
	public static bool GridLines { get; set; }

	/// <summary>
	/// <see cref="Window.Topmost"/> of windows created afterwards.
	/// Usually used at design time only, to make always on top of editor window.
	/// </summary>
	public static bool WinTopmost { get; set; }

	/// <summary>
	/// If true, constructor does not change color of windows created afterwards; then color normally is white.
	/// If false constructor sets standard color of dialogs, usually light gray.
	/// Default value depends on application's theme and usually is true if using custom theme.
	/// </summary>
	//	public static bool WinWhite { get; set; } = _IsCustomTheme(); //no, called too early
	public static bool WinWhite { get => s_winWhite ??= _IsCustomTheme(); set { s_winWhite = value; } }
	static bool? s_winWhite;

	//	/// <summary>
	//	/// Default modifyPadding option value. See <see cref="Options"/>.
	//	/// </summary>
	//	public static bool ModifyPadding { get; set; }

	#endregion

	#region add

	/// <summary>
	/// Changes some options for elements added afterwards.
	/// </summary>
	/// <param name="modifyPadding">Let <b>Add</b> adjust the <b>Padding</b> property of some controls to align better when using default theme. Default value of this option depends on application's theme.</param>
	/// <param name="rightAlignLabels">Right-align <b>Label</b> controls in grid cells.</param>
	/// <param name="margin">Default margin of elements. If not set, default marging is 3 in all sides. Default margin of nested panels is 0; this option is not used.</param>
	public AGuiBuilder Options(bool? modifyPadding = null, bool? rightAlignLabels = null, Thickness? margin = null)
	{
		if (modifyPadding != null) _opt_modifyPadding = modifyPadding.Value;
		if (rightAlignLabels != null) _opt_rightAlignLabels = rightAlignLabels.Value;
		if (margin != null) _opt_margin = margin.Value;
		return this;
	}
	bool _opt_modifyPadding = !_IsCustomTheme();
	bool _opt_rightAlignLabels;
	Thickness _opt_margin = new Thickness(3);
	//string _opt_radioGroup; //rejected. Radio buttons have problems with high DPI and should not be used. Or can put groups in panels.
	//	double _opt_checkMargin=3; //rejected

	/// <summary>
	/// Creates and adds element of type <i>T</i> (control etc of any type).
	/// </summary>
	/// <param name="variable">
	/// Receives element's variable. The function creates element of variable's type. You can use the variable to set element's properties before showing window or/and to get value after.
	/// Examples: <c>.Add(out CheckBox c1, "Text")</c>, <c>.Add(out _textBox1)</c>. If don't need a variable: <c>.Add(out Label _, "Text")</c> or <c>.Add&lt;Label>("Text")</c>.
	/// </param>
	/// <param name="text">
	/// Text, header or other content. Supported element types (or base types):
	/// <see cref="TextBox"/> - sets <b>Text</b> property.
	/// <see cref="ComboBox"/> - sets <b>Text</b> property (see also <see cref="Items"/>).
	/// <see cref="TextBlock"/> - sets <b>Text</b> property (see also <see cref="Text"/>).
	/// <see cref="PasswordBox"/> - sets <b>Password</b> property.
	/// <see cref="HeaderedContentControl"/>, <see cref="HeaderedItemsControl"/> - sets <b>Header</b> property.
	/// <see cref="ContentControl"/> except above two - sets <b>Content</b> property (can be string, other element, etc).
	/// <see cref="RichTextBox"/> - calls <b>AppendText</b> (see also <see cref="Load"/>).
	/// </param>
	/// <param name="flags"></param>
	/// <exception cref="NotSupportedException">The function does not support non-null <i>text</i> or flag <i>childOfLast</i> for this element type.</exception>
	public AGuiBuilder Add<T>(out T variable, object text = null, GBAdd flags = 0) where T : FrameworkElement, new()
	{
		_p.BeforeAdd(flags);
		variable = new T();
		_Add(variable, text, flags, true);
		return this;
	}

	void _Add(FrameworkElement e, object text, GBAdd flags, bool add)
	{
		bool childOfLast = flags.Has(GBAdd.ChildOfLast);
		if (!flags.Has(GBAdd.DontSetProperties))
		{
			if (e is Control c)
			{
				//rejected: modify padding etc through XAML. Not better than this.
				//rejected: use _opt_modifyPadding only if font Segoe UI. Tested with several fonts.
				switch (c)
				{
					case Label _:
						if (_opt_modifyPadding) c.Padding = new Thickness(1, 2, 1, 1); //default 5
						if (_opt_rightAlignLabels) c.HorizontalAlignment = HorizontalAlignment.Right;
						break;
					case TextBox _:
					case PasswordBox _:
						if (_opt_modifyPadding) c.Padding = new Thickness(2, 1, 1, 2); //default padding 0, height 18
						break;
					case Button _:
						if (_opt_modifyPadding) c.Padding = new Thickness(5, 1, 5, 2); //default 1
						break;
					case ToggleButton _:
						c.HorizontalAlignment = HorizontalAlignment.Left; //default stretch

						//partial workaround for squint CheckBox/RadioButton when High DPI.
						//	Without it, check mark size/alignment is different depending on control's xy.
						//	With it at least all controls are equal, either bad (eg DPI 125%) or good (DPI 150%).
						//	When bad, normal CheckBox check mark now still looks good. Only third state and RadioButtons look bad, but it is better than when controls look differently.
						//	But now at 150% DPI draws thick border.
						//					c.UseLayoutRounding=true;
						//					c.SnapsToDevicePixels=true; //does not help
						break;
					case ComboBox cb:
						//Change padding because default Windows font Segoe UI is badly centered vertically. Too big space above text, and too big control height.
						//Tested: changed padding isn't the reason of different control heights or/and arrows when high DPI.
						if (cb.IsEditable)
						{
							if (_opt_modifyPadding) c.Padding = new Thickness(2, 1, 2, 2); //default (2)
						}
						else
						{
							if (_opt_modifyPadding) c.Padding = new Thickness(5, 2, 4, 3); //default (6,3,5,3)
						}
						break;
				}
			}
			else if (e is Image)
			{
				e.UseLayoutRounding = true; //workaround for blurred images
			}

			//workaround for:
			//	1. Blurred images in some cases.
			//	2. High DPI: Different height of controls of same class, eg TextBox, ComboBox.
			//	3. High DPI: Different height/shape/alignment of control parts, eg CheckBox/RadioButton check mark and ComboBox arrow.
			//	Bad: on DPI 150% makes control borders 2-pixel.
			//	Rejected. Thick border is very noticeable, especially TabControl. Different control sizes, v check mark and v arrow aren't so noticeable. Radio buttons and null checkboxes rarely used. Most my tested WPF programs don't use this.
			//			e.UseLayoutRounding=true;
			//			e.SnapsToDevicePixels=true; //does not help

			if (text != null)
			{
				switch (e)
				{
					case HeaderedContentControl u: u.Header = text; break; //GroupBox, Expander
					case HeaderedItemsControl u: u.Header = text; break;
					case ContentControl u: u.Content = text; break; //Label, buttons, etc
					case TextBox u: u.Text = text.ToString(); break;
					case PasswordBox u: u.Password = text.ToString(); break;
					case ComboBox u: u.Text = text.ToString(); break;
					case TextBlock u: u.Text = text.ToString(); break;
					case RichTextBox u: u.AppendText(text.ToString()); break;
					default: throw new NotSupportedException($"Add() cannot set text/content of {e.GetType().Name}.");
				}
			}
		}
		if (!(childOfLast || e is GridSplitter)) e.Margin = _opt_margin;

		if (add)
		{
			_AddToParent(e, childOfLast);
			if (_alsoAll != null)
			{
				_alsoAllArgs ??= new GBAlsoAllArgs();
				if (_p is _Grid g)
				{
					var v = g.NextCell;
					_alsoAllArgs.Column = v.column - 1;
					_alsoAllArgs.Row = v.row;
				}
				else
				{
					_alsoAllArgs.Column = _alsoAllArgs.Row = -1;
				}
				_alsoAll(this, _alsoAllArgs);
			}
		}
	}

	void _AddToParent(FrameworkElement e, bool childOfLast)
	{
		if (childOfLast)
		{ //info: BeforeAdd throws if Last is panel
			switch (Last)
			{
				case ContentControl d: d.Content = e; break;
				case Decorator d: d.Child = e; break;
				//case Panel d: d.Children.Add(e); break; //no, cannot add multiple items because Last becomes the added child
				default: throw new NotSupportedException($"Cannot add child to {Last.GetType().Name}.");
			}
			_p.lastAdded = e;
		}
		else
		{
			_p.Add(e);
		}
	}

	/// <summary>
	/// Creates and adds element of type <i>T</i> (any type). This overload can be used when don't need element's variable.
	/// </summary>
	/// <param name="text">Text, header or other content. More info - see other overload.</param>
	/// <param name="flags"></param>
	/// <exception cref="NotSupportedException">The function does not support non-null <i>text</i> or flag <i>childOfLast</i> for this element type.</exception>
	public AGuiBuilder Add<T>(object text = null, GBAdd flags = 0) where T : FrameworkElement, new() => Add(out T _, text, flags);

	/// <summary>
	/// Adds 2 elements: <see cref="Label"/> and element of type <i>T</i> (control etc of any type).
	/// </summary>
	/// <param name="label">Label text.</param>
	/// <param name="variable">Receives element's variable. More info - see other overload.</param>
	/// <param name="text">Text, header or other content. More info - see other overload.</param>
	/// <exception cref="NotSupportedException">The function does not support non-null <i>text</i> or flag <i>childOfLast</i> for this element type.</exception>
	public AGuiBuilder Add<T>(string label, out T variable, object text = null) where T : FrameworkElement, new()
	{
		Add(out Label la, label);
		Add(out variable, text); //note: no flags
		System.Windows.Automation.AutomationProperties.SetLabeledBy(variable, la);
		return this;
	}

	/// <summary>
	/// Adds an existing element (control etc of any type).
	/// </summary>
	/// <param name="element"></param>
	/// <param name="flags"></param>
	/// <exception cref="NotSupportedException">The function does not support flag <i>childOfLast</i> for this element type.</exception>
	public AGuiBuilder Add(FrameworkElement element, GBAdd flags = 0)
	{
		_p.BeforeAdd(flags);
		_Add(element, null, flags, true);
		return this;
	}

	/// <summary>
	/// Adds button with <see cref="ButtonBase.Click"/> event handler.
	/// </summary>
	/// <param name="text">Text/content (<see cref="ContentControl.Content"/>).</param>
	/// <param name="click">Action to call when the button clicked. Its parameter's property <b>Cancel</b> can be used to prevent closing the window when clicked this OK button. Not called if validation fails.</param>
	/// <param name="flags"></param>
	/// <remarks>
	/// If <i>flags</i> contains <b>OK</b> or <b>Validate</b> and this window contains elements for which was called <see cref="Validation"/>, on click performs validation; if fails, does not call the <i>click</i> action and does not close the window.
	/// </remarks>
	public AGuiBuilder AddButton(object text, Action<GBButtonClickArgs> click, GBBFlags flags = 0/*, Action<GBButtonClickArgs> clickSplit = null*/)
	{
		Add(out Button c, text);
		if (flags.Has(GBBFlags.OK)) c.IsDefault = true;
		if (flags.Has(GBBFlags.Cancel)) c.IsCancel = true;
		if (flags.HasAny(GBBFlags.OK | GBBFlags.Cancel)) { c.MinWidth = 70; c.MinHeight = 21; }
		if (click != null || flags.HasAny(GBBFlags.OK | GBBFlags.Validate)) c.Click += (_, _) => {
			var w = _FindWindow(c);
			if (flags.HasAny(GBBFlags.OK | GBBFlags.Validate) && !_Validate(w, c)) return;
			if (click != null)
			{
				var e = new GBButtonClickArgs { Button = c, Window = w };
				click.Invoke(e);
				if (e.Cancel) return;
			}
			if (flags.Has(GBBFlags.OK)) w.DialogResult = true;
		};
		//		if(clickSplit!=null) c.ClickSplit+=clickSplit;
		//FUTURE: split-button.
		return this;
	}
	//	/// <param name="clickSplit">
	//	/// If not null, creates split-button. Action to call when the arrow part clicked. Example:
	//	/// <para><c>b => { int mi = AMenu.ShowSimple("1 One|2 Two", b, (0, b.Height)); }</c></para>
	//	/// </param>

	/// <summary>
	/// Adds button that closes the window and sets <see cref="ResultButton"/>.
	/// </summary>
	/// <param name="text">Text/content (<see cref="ContentControl.Content"/>).</param>
	/// <param name="result"><see cref="ResultButton"/> value when clicked this button.</param>
	/// <remarks>
	/// When clicked, sets <see cref="ResultButton"/> = <i>result</i>, closes the window, and <see cref="ShowDialog"/> returns true.
	/// </remarks>
	public AGuiBuilder AddButton(object text, int result/*, Action<GBButtonClickArgs> clickSplit = null*/)
	{
		Add(out Button c, text);
		c.Click += (_, _) => { _resultButton = result; _FindWindow(c).DialogResult = true; };
		//		if(clickSplit!=null) c.ClickSplit+=clickSplit;
		return this;
	}
	//	/// <param name="clickSplit">
	//	/// If not null, creates split-button. Action to call when the arrow part clicked. Example:
	//	/// <para><c>b => { int mi = AMenu.ShowSimple("1 One|2 Two", b, (0, b.Height)); }</c></para>
	//	/// </param>

	/// <summary>
	/// If the window closed with an <see cref="AddButton(object, int)"/> button, returns its <i>result</i>. Else returns 0.
	/// </summary>
	public int ResultButton => _resultButton;
	int _resultButton;

	/// <summary>
	/// Adds OK and/or Cancel buttons that will close the window.
	/// </summary>
	/// <param name="ok">Text of OK button. If null, does not add the button.</param>
	/// <param name="cancel">Text of Cancel button. If null, does not add the button.</param>
	/// <param name="clickOk">Action to call when clicked OK button. Its parameter's property <b>Cancel</b> can be used to prevent closing the window when clicked this OK button.</param>
	/// <remarks>
	/// Sets properties of OK/Cancel buttons so that on button click or Enter/Esc the window is closed and <see cref="ShowDialog"/> returns true on OK, false on Cancel.
	/// 
	/// By default adds a right-bottom aligned <see cref="StackPanel"/> and adds buttons in it. If <i>ok</i> or <i>cancel</i> is null, adds single button without panel.
	/// Also does not add panel if already in a stack panel; it can be used to add more buttons. See <see cref="StartOkCancel"/>.
	/// </remarks>
	public AGuiBuilder AddOkCancel(string ok = "OK", string cancel = "Cancel", Action<GBButtonClickArgs> clickOk = null)
	{
		int n = 0; if (ok != null) n++; if (cancel != null) n++;
		if (n == 0) throw new ArgumentNullException(null, "AddOkCancel(null, null)");
		bool stack = n > 1 && !(_p is _StackPanel);
		if (stack) StartOkCancel();
		if (ok != null) AddButton(ok, clickOk, GBBFlags.OK);
		if (cancel != null) AddButton(cancel, null, GBBFlags.Cancel);
		if (stack) End();
		return this;
	}

	/// <summary>
	/// Adds <see cref="Separator"/> control.
	/// </summary>
	/// <param name="vertical">If true, adds vertical separator. If false, horizontal. If null (default), adds vertical if in horizontal stack panel, else adds horizontal.</param>
	/// <remarks>
	/// In <b>Canvas</b> panel separator's default size is 1x1. Need to set size, like <c>.AddSeparator()[0, 50, 100, 1]</c>.
	/// </remarks>
	public AGuiBuilder AddSeparator(bool? vertical = null)
	{
		Add(out Separator c);
		if (vertical ?? (_p.panel is StackPanel p && p.Orientation == Orientation.Horizontal))
		{
			c.Style = _style_VertSep ??= c.FindResource(ToolBar.SeparatorStyleKey) as Style;
		}
		return this;
	}
	Style _style_VertSep;

	/// <summary>
	/// Adds one or more empty cells in current row of current grid.
	/// </summary>
	/// <param name="span">Column count.</param>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	/// <remarks>
	/// Actually just changes column index where next element will be added.
	/// </remarks>
	public AGuiBuilder Skip(int span = 1)
	{
		if (span < 0) throw new ArgumentException();
		var g = _p as _Grid ?? throw new InvalidOperationException("Skip() in non-grid panel");
		g.Skip(span);
		return this;
	}

	/// <summary>
	/// Sets to add next element in the same grid cell as previous element.
	/// </summary>
	/// <param name="width">Width of next element. If negative - width of previous element. Also it adds to the corresponding margin of other element.</param>
	/// <exception cref="InvalidOperationException">In non-grid panel or in a wrong place.</exception>
	/// <remarks>
	/// Can be used to add 2 elements in 1 cell as a cheaper and more concise way than with a <b>StartX</b> function.
	/// Next element will inherit column index and span of previous element but won't inherit row span.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// .Add("File", out TextBox _).And(70).AddButton("Browse...", null)
	/// ]]></code>
	/// </example>
	public AGuiBuilder And(double width)
	{
		var g = _p as _Grid ?? throw new InvalidOperationException("And() in non-grid panel");
		g.And(width);
		return this;
	}

	#endregion

	#region set common properties of last added element

	/// <summary>
	/// Sets column span of the last added element.
	/// </summary>
	/// <param name="columns">Column count. If -1 or too many, will span all remaining columns in current row. If 0, will share 1 column with next element added in current row; to set element positions use <see cref="Margin"/>, <see cref="Width"/> and <see cref="Align"/>; see also <see cref="And"/>.</param>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	/// <remarks>
	/// Also there is an indexer for it. For example, instead of code <c>.Span(2)</c> use code <c>[2]</c>.
	/// </remarks>
	public AGuiBuilder Span(int columns)
	{
		_ParentOfLastAsOrThrow<_Grid>().Span(columns);
		return this;
	}

	/// <summary>
	/// Sets column span of the last added element. Same as <see cref="Span"/>.
	/// </summary>
	/// <param name="spanColumns">Column count. If -1, all remaining columns.</param>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	public AGuiBuilder this[int spanColumns] => Span(spanColumns);

	/// <summary>
	/// Sets row span of the last added element.
	/// </summary>
	/// <param name="rows">Row count.</param>
	/// <exception cref="InvalidOperationException">In non-grid panel.</exception>
	/// <remarks>
	/// In next row(s) use <see cref="Skip"/> to skip cells occupied by this element.
	/// Often it's better to add a nested panel instead. See <see cref="StartGrid"/>.
	/// </remarks>
	public AGuiBuilder SpanRows(int rows)
	{
		var c = _ParentOfLastAsOrThrow<_Grid>().LastDirect;
		Grid.SetRowSpan(c, rows);
		return this;
	}

	/// <summary>
	/// Calls your callback function.
	/// </summary>
	/// <param name="action"></param>
	public AGuiBuilder Also(Action<AGuiBuilder> action)
	{
		action(this);
		return this;
	}

	/// <summary>
	/// Sets callback function to be called by <b>AddX</b> functions for each element added afterwards. Not called by <b>StartX</b> functions for panels.
	/// </summary>
	/// <param name="action">Callback function or null.</param>
	/// <example>
	/// <code><![CDATA[
	/// .AlsoAll((b, e) => {
	/// 	if(b.Last is CheckBox c) { c.IsChecked = true; b.Margin("t1 b1"); }
	/// })
	/// ]]></code>
	/// </example>
	public AGuiBuilder AlsoAll(Action<AGuiBuilder, GBAlsoAllArgs> action)
	{
		_alsoAll = action;
		return this;
	}
	Action<AGuiBuilder, GBAlsoAllArgs> _alsoAll;
	GBAlsoAllArgs _alsoAllArgs;

	/// <summary>
	/// Sets width and height of the last added element. Optionally sets alignment.
	/// </summary>
	/// <param name="width">Width or/and min/max width.</param>
	/// <param name="height">Height or/and min/max height.</param>
	/// <param name="alignX">Horizontal alignment. If not null, calls <see cref="Align(string, string)"/>.</param>
	/// <param name="alignY">Vertical alignment.</param>
	/// <exception cref="ArgumentException">Invalid alignment string.</exception>
	public AGuiBuilder Size(GBLength width, GBLength height, string alignX = null, string alignY = null)
	{
		var c = Last;
		width.ApplyTo(c, false);
		height.ApplyTo(c, true);
		if (alignX != null || alignY != null) Align(alignX, alignY);
		return this;
	}

	/// <summary>
	/// Sets width of the last added element. Optionally sets alignment.
	/// </summary>
	/// <param name="width">Width or/and min/max width.</param>
	/// <param name="alignX">Horizontal alignment. If not null, calls <see cref="Align(string, string)"/>.</param>
	/// <exception cref="ArgumentException">Invalid alignment string.</exception>
	public AGuiBuilder Width(GBLength width, string alignX = null)
	{
		width.ApplyTo(Last, false);
		if (alignX != null) Align(alignX);
		return this;
	}

	/// <summary>
	/// Sets height of the last added element. Optionally sets alignment.
	/// </summary>
	/// <param name="height">Height or/and min/max height.</param>
	/// <param name="alignY">Vertical alignment. If not null, calls <see cref="Align(string, string)"/>.</param>
	/// <exception cref="ArgumentException">Invalid alignment string.</exception>
	public AGuiBuilder Height(GBLength height, string alignY = null)
	{
		height.ApplyTo(Last, true);
		if (alignY != null) Align(null, alignY);
		return this;
	}

	/// <summary>
	/// Sets position of the last added element in <b>Canvas</b> panel. Optionally sets size.
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	/// <param name="width">Width or/and min/max width.</param>
	/// <param name="height">Height or/and min/max height.</param>
	/// <exception cref="InvalidOperationException">Current panel is not <b>Canvas</b>.</exception>
	/// <remarks>
	/// Only in <see cref="Canvas"/> panel you can set position explicitly. In other panel types it is set automatically and can be adjusted with <see cref="Margin"/>, <see cref="Align"/>, container's <see cref="AlignContent"/>, etc.
	/// </remarks>
	public AGuiBuilder XY(double x, double y, GBLength? width = null, GBLength? height = null)
	{
		var c = _ParentOfLastAsOrThrow<_Canvas>().LastDirect;
		Canvas.SetLeft(c, x);
		Canvas.SetTop(c, y);
		width?.ApplyTo(c, false);
		height?.ApplyTo(c, true);
		return this;
	}

	/// <summary>
	/// Calls <see cref="XY"/>.
	/// </summary>
	public AGuiBuilder this[double x, double y, GBLength? width = null, GBLength? height = null] => XY(x, y, width, height);

	/// <summary>
	/// Docks the last added element in <see cref="DockPanel"/>.
	/// </summary>
	/// <param name="dock"></param>
	/// <exception cref="InvalidOperationException">Current panel is not <b>DockPanel</b>.</exception>
	public AGuiBuilder Dock(Dock dock)
	{
		var c = _ParentOfLastAsOrThrow<_DockPanel>().LastDirect;
		DockPanel.SetDock(c, dock);
		return this;
	}

	/// <summary>
	/// Sets horizontal and/or vertical alignment of the last added element.
	/// </summary>
	/// <param name="x">Horizontal alignment.</param>
	/// <param name="y">Vertical alignment.</param>
	/// <exception cref="InvalidOperationException">Current panel is <b>Canvas</b>.</exception>
	public AGuiBuilder Align(HorizontalAlignment? x = null, VerticalAlignment? y = null)
	{
		var c = Last;
		if (c.Parent is Canvas) throw new InvalidOperationException("Align() in Canvas panel.");
		if (x != null) c.HorizontalAlignment = x.Value;
		if (y != null) c.VerticalAlignment = y.Value;
		return this;
	}

	/// <summary>
	/// Sets horizontal and/or vertical alignment of the last added element.
	/// </summary>
	/// <param name="x">Horizontal alignment. String that starts with one of these letters, uppercase or lowercase: L (left), R (right), C (center), S (stretch).</param>
	/// <param name="y">Vertical alignment. String that starts with one of these letters, uppercase or lowercase: T (top), B (bottom), C (center), S (stretch).</param>
	/// <exception cref="InvalidOperationException">Current panel is <b>Canvas</b>.</exception>
	/// <exception cref="ArgumentException">Invalid alignment string.</exception>
	public AGuiBuilder Align(string x = null, string y = null) => Align(_AlignmentFromStringX(x), _AlignmentFromStringY(y));

	HorizontalAlignment? _AlignmentFromStringX(string s, [CallerMemberName] string caller = null) => s.NE() ? default(HorizontalAlignment?) : (char.ToUpperInvariant(s[0]) switch { 'L' => HorizontalAlignment.Left, 'C' => HorizontalAlignment.Center, 'R' => HorizontalAlignment.Right, 'S' => HorizontalAlignment.Stretch, _ => throw new ArgumentException(caller + "(x)") });
	VerticalAlignment? _AlignmentFromStringY(string s, [CallerMemberName] string caller = null) => s.NE() ? default(VerticalAlignment?) : (char.ToUpperInvariant(s[0]) switch { 'T' => VerticalAlignment.Top, 'C' => VerticalAlignment.Center, 'B' => VerticalAlignment.Bottom, 'S' => VerticalAlignment.Stretch, _ => throw new ArgumentException(caller + "(y)") });

	/// <summary>
	/// Sets content alignment of the last added element.
	/// </summary>
	/// <param name="x">Horizontal alignment.</param>
	/// <param name="y">Vertical alignment.</param>
	/// <exception cref="InvalidOperationException">The last added element is not <b>Control</b>.</exception>
	public AGuiBuilder AlignContent(HorizontalAlignment? x = null, VerticalAlignment? y = null)
	{
		var c = _LastAsControlOrThrow();
		if (x != null) c.HorizontalContentAlignment = x.Value;
		if (y != null) c.VerticalContentAlignment = y.Value;
		return this;
	}

	/// <summary>
	/// Sets content alignment of the last added element.
	/// </summary>
	/// <param name="x">Horizontal alignment. String like with <see cref="Align(string, string)"/>.</param>
	/// <param name="y">Vertical alignment.</param>
	/// <exception cref="InvalidOperationException">The last added element is not <b>Control</b>.</exception>
	/// <exception cref="ArgumentException">Invalid alignment string.</exception>
	public AGuiBuilder AlignContent(string x = null, string y = null) => AlignContent(_AlignmentFromStringX(x), _AlignmentFromStringY(y));

	/// <summary>
	/// Sets margin of the last added element.
	/// </summary>
	public AGuiBuilder Margin(Thickness margin)
	{
		Last.Margin = margin;
		return this;
	}

	/// <summary>
	/// Sets margin of the last added element.
	/// </summary>
	public AGuiBuilder Margin(double? left = null, double? top = null, double? right = null, double? bottom = null)
	{
		var c = Last;
		var p = c.Margin;
		left ??= p.Left;
		top ??= p.Top;
		right ??= p.Right;
		bottom ??= p.Bottom;
		c.Margin = new Thickness(left.Value, top.Value, right.Value, bottom.Value);
		return this;
	}

	/// <summary>
	/// Sets margin of the last added element.
	/// </summary>
	/// <param name="margin">
	/// String containing uppercase or lowercase letters for margin sides (L, T, R, B) optionally followed by a number (default 0) and optionally separated by spaces. Or just single number, to set all sides equal.
	/// Examples: "tb" (top 0, bottom 0), "L5 R15" (left 5, right 15), "2" (all sides 2).
	/// </param>
	/// <exception cref="ArgumentException">Invalid string.</exception>
	public AGuiBuilder Margin(string margin)
	{
		var c = Last;
		var m = c.Margin;
		_ThicknessFromString(ref m, margin);
		c.Margin = m;
		return this;
	}

	static void _ThicknessFromString(ref Thickness t, string s, [CallerMemberName] string caller = null)
	{
		if (s.NE()) return;
		if (s.ToInt(out int v1, 0, out int e1) && e1 == s.Length)
		{
			t = new Thickness(v1);
			return;
		}

		for (int i = 0; i < s.Length; i++)
		{
			var c = s[i]; if (c == ' ') continue;
			int v = s.ToInt(i + 1, out int end); if (end > 0) i = end - 1; //never mind: should be double. Currently we don't have a function that can recognize and convert part of string to double.
			switch (c)
			{
				case 't': case 'T': t.Top = v; break;
				case 'b': case 'B': t.Bottom = v; break;
				case 'l': case 'L': t.Left = v; break;
				case 'r': case 'R': t.Right = v; break;
				default: throw new ArgumentException(caller + "()");
			}
		}
	}

	/// <summary>
	/// Sets padding of the last added control.
	/// </summary>
	/// <exception cref="InvalidOperationException">The last added element is not <b>Control</b>.</exception>
	public AGuiBuilder Padding(Thickness thickness)
	{
		_LastAsControlOrThrow().Padding = thickness;
		return this;
	}

	/// <summary>
	/// Sets padding of the last added control.
	/// </summary>
	/// <exception cref="InvalidOperationException">The last added element is not <b>Control</b>.</exception>
	public AGuiBuilder Padding(double? left = null, double? top = null, double? right = null, double? bottom = null)
	{
		var c = _LastAsControlOrThrow();
		var p = c.Padding;
		left ??= p.Left;
		top ??= p.Top;
		right ??= p.Right;
		bottom ??= p.Bottom;
		c.Padding = new Thickness(left.Value, top.Value, right.Value, bottom.Value);
		return this;
	}

	/// <summary>
	/// Sets padding of the last added control.
	/// </summary>
	/// <param name="padding">
	/// String containing uppercase or lowercase letters for padding sides (L, T, R, B) optionally followed by a number (default 0) and optionally separated by spaces. Or just single number, to set all sides equal.
	/// Examples: "tb" (top 0, bottom 0), "L5 R15" (left 5, right 15), "2" (all sides 2).
	/// </param>
	/// <exception cref="InvalidOperationException">The last added element is not <b>Control</b>.</exception>
	/// <exception cref="ArgumentException">Invalid string.</exception>
	public AGuiBuilder Padding(string padding)
	{
		var c = _LastAsControlOrThrow();
		var p = c.Padding;
		_ThicknessFromString(ref p, padding);
		c.Padding = p;
		return this;
	}

	/// <summary>
	/// Sets <see cref="UIElement.IsEnabled"/> of the last added element.
	/// </summary>
	/// <param name="disabled">If true (default), sets IsEnabled=false, else sets IsEnabled=true.</param>
	public AGuiBuilder Disabled(bool disabled = true)
	{
		Last.IsEnabled = !disabled;
		return this;
	}

	/// <summary>
	/// Sets <see cref="UIElement.Visibility"/> of the last added element.
	/// </summary>
	/// <param name="hidden">If true (default), sets <see cref="Visibility"/> <b>Hiden</b>; if false - <b>Visible</b>; if null - <b>Collapsed</b>.</param>
	public AGuiBuilder Hidden(bool? hidden = true)
	{
		Last.Visibility = hidden switch { true => Visibility.Hidden, false => Visibility.Visible, _ => Visibility.Collapsed };
		return this;
	}

	/// <summary>
	/// Sets tooltip text/content/object of the last added element. See <see cref="FrameworkElement.ToolTip"/>.
	/// </summary>
	/// Text box with simple tooltip.
	/// <code><![CDATA[
	/// .R.Add("Example", out TextBox _).Tooltip("Tooltip text")
	/// ]]></code>
	/// <example>
	/// Tooltip with content created by another AGuiBuilder.
	/// <code><![CDATA[
	/// var btt = new AGuiBuilder() //creates tooltip content
	/// 	.R.Add<Image>().Image(AIcon.GetStockIconHandle(StockIcon.INFO))
	/// 	.R.Add<TextBlock>().Text("Some ", "<b>text", ".")
	/// 	.End();
	/// 
	/// var b = new AGuiBuilder("Window").WinSize(300) //creates dialog
	/// 	.R.AddButton("Example", null).Tooltip(btt.Panel)
	/// 	.R.AddOkCancel()
	/// 	.End();
	/// if (!b.ShowDialog()) return;
	/// ]]></code>
	/// </example>
	public AGuiBuilder Tooltip(object tooltip)
	{
		Last.ToolTip = tooltip;
		return this;
	}
	//FUTURE: make easier to create tooltip content, eg Inlines of TextBlock. Would be good to create on demand.
	//FUTURE: hyperlinks in tooltip. Now does not work because tooltip closes when mouse leaves the element.

	/// <summary>
	/// Sets background and/or foreground brush (color, gradient, etc) of the last added element.
	/// </summary>
	/// <param name="background">Background brush. See <see cref="Brushes"/>, <see cref="SystemColors"/>. Descendants usually inherit this property.</param>
	/// <param name="foreground">Foreground brush. Usually sets text color. Descendants usually override this property.</param>
	/// <exception cref="NotSupportedException">Last added element must be <b>Control</b>, <b>Panel</b>, <b>Border</b> or <b>TextBlock</b>. With <i>foreground</i> only <b>Control</b> or <b>TextBlock</b>.</exception>
	/// <example>
	/// <code><![CDATA[
	/// .R.Add<Label>("Example1").Brush(Brushes.Cornsilk, Brushes.Green).Border(Brushes.BlueViolet, 1)
	/// .R.Add<Label>("Example2").Brush(new LinearGradientBrush(Colors.Chocolate, Colors.White, 0))
	/// ]]></code>
	/// </example>
	public AGuiBuilder Brush(Brush background = null, Brush foreground = null)
	{ //named not Colors because: 1. Can set other brush than color, eg gradient. 2. Rarely used and in autocompletion lists is above Columns.
		var last = Last;
		if (foreground != null)
		{
			switch (last)
			{
				case Control c: c.Foreground = foreground; break;
				case TextBlock c: c.Foreground = foreground; break;
				default: throw new NotSupportedException("Color(): Last added must be Control or TextBlock, or foreground null");
			}
		}
		if (background != null)
		{
			if (last == _p.panel && !_IsNested && _window != null) last = _window;
			switch (last)
			{
				case Control c: c.Background = background; break;
				case TextBlock c: c.Background = background; break;
				case Border c: c.Background = background; break;
				case Panel c: c.Background = background; break;
				default: throw new NotSupportedException("Color(): Last added must be Control, Panel, Border or TextBlock");
			}
		}
		return this;
	}

	/// <summary>
	/// Sets border properties of the last added element.
	/// </summary>
	/// <param name="color"></param>
	/// <param name="thickness"></param>
	/// <param name="padding"></param>
	/// <param name="cornerRadius"></param>
	/// <exception cref="NotSupportedException">Last added element must be <b>Control</b> or <b>Border</b>. With <i>cornerRadius</i> only <b>Border</b>.</exception>
	/// <example>
	/// <code><![CDATA[
	/// .R.Add<Label>("Example1").Border(Brushes.BlueViolet, 1, new Thickness(5)).Brush(Brushes.Cornsilk, Brushes.Green)
	/// .R.Add<Border>().Border(Brushes.Blue, 2, cornerRadius: 3).Add<Label>("Example2", GBAdd.ChildOfLast)
	/// ]]></code>
	/// </example>
	public AGuiBuilder Border(Brush color, double thickness, Thickness? padding = null, double? cornerRadius = null)
	{
		var thick = new Thickness(thickness);
		switch (Last)
		{
			case Control c:
				if (cornerRadius != null) throw new NotSupportedException("Border(): Last added must be Border, or cornerRadius null");
				c.BorderBrush = color;
				c.BorderThickness = thick;
				if (padding != null) c.Padding = padding.Value;
				break;
			case Border c:
				c.BorderBrush = color;
				c.BorderThickness = thick;
				if (padding != null) c.Padding = padding.Value;
				if (cornerRadius != null) c.CornerRadius = new CornerRadius(cornerRadius.Value);
				break;
			default: throw new NotSupportedException("Border(): Last added must be Control or Border");
		}
		return this;
	}

	/// <summary>
	/// Sets font properties of the last added element and its descendants.
	/// </summary>
	/// <param name="name"></param>
	/// <param name="size"></param>
	/// <param name="bold"></param>
	/// <param name="italic"></param>
	public AGuiBuilder Font(string name = null, double? size = null, bool? bold = null, bool? italic = null)
	{
		var c = Last;
		if (name != null) TextElement.SetFontFamily(c, new FontFamily(name));
		if (size != null) TextElement.SetFontSize(c, size.Value);
		if (bold != null) TextElement.SetFontWeight(c, bold == true ? FontWeights.Bold : FontWeights.Normal);
		if (italic != null) TextElement.SetFontStyle(c, italic == true ? FontStyles.Italic : FontStyles.Normal);
		return this;
		//rejected: FontStretch? stretch=null. Rarely used. Most fonts don't support.

		//not sure is this OK or should set font properties for each supporting class separately.
	}

	/// <summary>
	/// Attempts to set focus to the last added element when it'll become visible.
	/// </summary>
	public AGuiBuilder Focus()
	{
		Last.Focus();
		return this;
	}

	/// <summary>
	/// Sets <see cref="FrameworkElement.DataContext"/> property of the last added element.
	/// Then with <see cref="Bind"/> of this and descendant elements don't need to specify data source object because it is set by this function.
	/// </summary>
	/// <param name="source">Data source object.</param>
	public AGuiBuilder BindingContext(object source)
	{
		Last.DataContext = source;
		return this;
	}

	/// <summary>
	/// Calls <see cref="FrameworkElement.SetBinding(DependencyProperty, string)"/> of the last added element.
	/// </summary>
	/// <param name="property">Element's dependency property, for example <c>TextBox.TextProperty</c>.</param>
	/// <param name="path">Source property name or path, for example <c>nameof(MyData.Property)</c>. Source object should be set with <see cref="BindingContext"/>.</param>
	public AGuiBuilder Bind(DependencyProperty property, string path)
	{
		Last.SetBinding(property, path);
		return this;
	}

	/// <summary>
	/// Calls <see cref="FrameworkElement.SetBinding(DependencyProperty, BindingBase)"/> of the last added element.
	/// </summary>
	/// <param name="property">Element's dependency property, for example <c>TextBox.TextProperty</c>.</param>
	/// <param name="binding">A binding object, for example <c>new Binding(nameof(MyData.Property))</c> or <c>new Binding(nameof(MyData.Property)) { Source = dataObject }</c>. In the first case, source object should be set with <see cref="BindingContext"/>.</param>
	public AGuiBuilder Bind(DependencyProperty property, BindingBase binding)
	{
		Last.SetBinding(property, binding);
		return this;
	}

	/// <summary>
	/// Calls <see cref="FrameworkElement.SetBinding(DependencyProperty, BindingBase)"/> of the last added element and gets its return value.
	/// </summary>
	/// <param name="property">Element's dependency property, for example <c>TextBox.TextProperty</c>.</param>
	/// <param name="binding">A binding object.</param>
	///	<param name="r">The return value of <b>SetBinding</b>.</param>
	public AGuiBuilder Bind(DependencyProperty property, BindingBase binding, out BindingExpressionBase r)
	{
		r = Last.SetBinding(property, binding);
		return this;
	}

	/// <summary>
	/// Calls <see cref="FrameworkElement.SetBinding(DependencyProperty, BindingBase)"/> of the last added element. Creates <see cref="Binding"/> that uses <i>source</i> and <i>path</i>.
	/// </summary>
	/// <param name="property">Element's dependency property, for example <c>TextBox.TextProperty</c>.</param>
	/// <param name="source">Data source object.</param>
	/// <param name="path">Source property name or path, for example <c>nameof(MyData.Property)</c>.</param>
	public AGuiBuilder Bind(DependencyProperty property, object source, string path)
	{
		var binding = new Binding(path) { Source = source };
		Last.SetBinding(property, binding);
		return this;
	}

	/// <summary>
	/// Sets a validation callback function for the last added element.
	/// </summary>
	/// <param name="func">Function that returns an error string if element's value is invalid, else returns null.</param>
	/// <remarks>
	/// The callback function will be called when clicked button OK or a button added with flag <see cref="GBBFlags.Validate"/>.
	/// If it returns a non-null string, the window stays open and button's <i>click</i> callback not called. The string is displayed in a tooltip.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// var b = new AGuiBuilder("Window").WinSize(300)
	/// 	.R.Add("Name", out TextBox tName).Validation(o => string.IsNullOrWhiteSpace(tName.Text) ? "Name cannot be empty" : null)
	/// 	.R.Add("Count", out TextBox tCount).Validation(o => int.TryParse(tCount.Text, out int i1) && i1 >= 0 && i1 <= 100 ? null : "Count must be 0-100")
	/// 	.R.AddOkCancel()
	/// 	.End();
	/// if (!b.ShowDialog()) return;
	/// AOutput.Write(tName.Text, tCount.Text.ToInt());
	/// ]]></code>
	/// </example>
	public AGuiBuilder Validation(Func<FrameworkElement, string> func/*, DependencyProperty property=null*/)
	{
		var c = Last;
		//validate on click of OK or some other button. Often eg text fields initially are empty and must be filled.
		(_validations ??= new List<_Validation>()).Add(new _Validation { e = c, func = func });

		//rejected: also validate on lost focus or changed property value. Maybe in the future.
		//		if(property==null) {
		//			c.LostFocus+=(o,e)=> { AOutput.Write(func(o as FrameworkElement)); };
		//		} else {
		//			var pd = DependencyPropertyDescriptor.FromProperty(property, c.GetType());
		//			pd.AddValueChanged(c, (o,e)=> { AOutput.Write(func(o as FrameworkElement)); });
		//		}
		return this;
	}

	class _Validation
	{
		public FrameworkElement e;
		public Func<FrameworkElement, string> func;
	}

	List<_Validation> _validations;

	bool _Validate(Window w, Button b)
	{
		TextBlock tb = null;
		foreach (var gb in _GetAllGuiBuilders(w))
		{ //find all AGuiBuilders used to build this window
			if (gb._validations == null) continue;
			foreach (var v in gb._validations)
			{
				var e = v.e;
				var s = v.func(e); if (s == null) continue;
				if (tb == null) tb = new TextBlock(); else tb.Inlines.Add(new LineBreak());
				var h = new Hyperlink(new Run(s));
				h.Click += (o, y) => {
					if (_FindAncestorTabItem(e, out var ti)) ti.IsSelected = true; //SHOULDDO: support other cases too, eg other tabcontrol-like control class or tabcontrol in tabcontrol.
					ATimer.After(1, _ => { //else does not focus etc if was in hidden tab page
						try
						{
							e.BringIntoView();
							e.Focus();
						}
						catch { }
						//						catch(Exception e1) { AOutput.Write(e1); }
					});
				};
				tb.Inlines.Add(h);
			}
		}
		if (tb == null) return true;
		var tt = new ToolTip { Content = tb, StaysOpen = false, PlacementTarget = b, Placement = PlacementMode.Bottom };
		//		var tt=new Popup { Child=tb, StaysOpen=false, PlacementTarget=b, Placement= PlacementMode.Bottom }; //works, but black etc
		tt.IsOpen = true;
		//never mind: could add eg red rectangle, like WPF does on binding validation error. Not easy.
		return false;
	}

	static List<AGuiBuilder> _GetAllGuiBuilders(DependencyObject root)
	{
		var a = new List<AGuiBuilder>();
		_Enum(root, 0);
		void _Enum(DependencyObject parent, int level)
		{
			foreach (var o in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
			{
				//				AOutput.Write(new string(' ', level) + o);
				if (o is Panel p && s_cwt.TryGetValue(p, out var gb)) a.Add(gb);
				_Enum(o, level + 1);
			}
		}
		return a;
	}

	static bool _FindAncestorTabItem(DependencyObject e, out TabItem ti)
	{
		ti = null;
		for (; ; )
		{
			switch (e = LogicalTreeHelper.GetParent(e))
			{
				case null: return false;
				case TabItem t: ti = t; return true;
			}
		}
	}

	//FUTURE: ContextMenu. But need a class to build WPF context menus, like AMenu.

	//rejected. Rarely used. If need, users can create an extension method. Name or Uid also is used by UIA, but with this library it is not necessary because can Navigate; also Add(label, ...) sets "labeled by".
	//	/// <summary>
	//	/// Sets name of the last added element.  See <see cref="FrameworkElement.Name"/>.
	//	/// </summary>
	//	public AGuiBuilder Name(string name) {
	//		Last.Name = name;
	//		return this;
	//	}

	#endregion

	#region set type-specific properties of last added element

	/// <summary>
	/// Sets <see cref="ToggleButton.IsChecked"/> and <see cref="ToggleButton.IsThreeState"/> of the last added check box or radio button.
	/// </summary>
	/// <param name="check"></param>
	/// <param name="threeState"></param>
	/// <exception cref="NotSupportedException">The last added element is not <b>ToggleButton</b>.</exception>
	public AGuiBuilder Checked(bool? check = true, bool threeState = false)
	{
		var c = Last as ToggleButton ?? throw new NotSupportedException("Checked(): Last added element must be CheckBox or RadioButton");
		c.IsThreeState = threeState;
		c.IsChecked = check;
		return this;
	}

	/// <summary>
	/// Sets <see cref="ToggleButton.IsChecked"/> of the specified <see cref="RadioButton"/>.
	/// </summary>
	/// <param name="check"></param>
	/// <param name="control"></param>
	/// <remarks>
	/// Unlike other similar functions, does not use <see cref="Last"/>.
	/// </remarks>
	public AGuiBuilder Checked(bool check, RadioButton control)
	{
		control.IsChecked = check;
		return this;
	}

	/// <summary>
	/// Sets <see cref="TextBoxBase.IsReadOnly"/> or <see cref="ComboBox.IsReadOnly"/> of the last added text box or editable combo box.
	/// </summary>
	/// <param name="readOnly"></param>
	/// <exception cref="NotSupportedException">The last added element is not <b>TextBoxBase</b> or <b>ComboBox</b>.</exception>
	public AGuiBuilder Readonly(bool readOnly = true)
	{ //rejected: , bool caretVisible=false. Not useful.
		switch (Last)
		{
			case TextBoxBase c:
				c.IsReadOnly = readOnly;
				//			c.IsReadOnlyCaretVisible=caretVisible;
				break;
			case ComboBox c:
				c.IsReadOnly = readOnly;
				break;
			default: throw new NotSupportedException("Readonly(): Last added must be TextBox, RichTextBox or ComboBox");
		}
		return this;
	}

	/// <summary>
	/// Makes the last added <see cref="TextBox"/> multiline.
	/// </summary>
	/// <param name="height">If not null, sets height or/and min/max height.</param>
	/// <param name="wrap"><see cref="TextBox.TextWrapping"/>.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>TextBox</b>.</exception>
	public AGuiBuilder Multiline(GBLength? height = null, TextWrapping wrap = TextWrapping.WrapWithOverflow)
	{
		var c = Last as TextBox ?? throw new NotSupportedException("Multiline(): Last added must be TextBox");
		c.AcceptsReturn = true;
		c.TextWrapping = wrap;
		c.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		c.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
		height?.ApplyTo(c, true);
		return this;
	}

	/// <summary>
	/// Makes the last added <see cref="ComboBox"/> editable.
	/// </summary>
	/// <exception cref="NotSupportedException">The last added element is not <b>ComboBox</b>.</exception>
	public AGuiBuilder Editable()
	{
		var c = Last as ComboBox ?? throw new NotSupportedException("Editable(): Last added must be ComboBox");
		c.IsEditable = true;
		if (_opt_modifyPadding) c.Padding = new Thickness(2, 1, 2, 2); //default (2) or set by _Add() for non-editable
		return this;
	}

	/// <summary>
	/// Splits string and ads substrings as items to the last added <see cref="ItemsControl"/> (<see cref="ComboBox"/>, etc).
	/// </summary>
	/// <param name="items">String like <c>"One|Two|Three"</c>.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>ItemsControl</b>.</exception>
	/// <remarks>
	/// If it is a non-editable <b>ComboBox</b>, selects the first item. See also <see cref="Select"/>.
	/// </remarks>
	public AGuiBuilder Items(string items) => _Items(items.Split('|'), null);

	/// <summary>
	/// Adds items of any type to the last added <see cref="ItemsControl"/> (<see cref="ComboBox"/>, etc).
	/// </summary>
	/// <param name="items">Items of any type (string, WPF element).</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>ItemsControl</b>.</exception>
	/// <remarks>
	/// If it is a non-editable <b>ComboBox</b>, selects the first item. See also <see cref="Select"/>.
	/// </remarks>
	public AGuiBuilder Items(params object[] items) => _Items(items, null);

	AGuiBuilder _Items(object[] a, System.Collections.IEnumerable e)
	{
		var ic = Last as ItemsControl ?? throw new NotSupportedException("Items(): Last added must be ItemsControl, for example ComboBox");
		if (a != null)
		{
			ic.Items.Clear();
			foreach (var v in a) ic.Items.Add(v);
		}
		else if (e != null)
		{
			ic.ItemsSource = e;
		}
		if (Last is ComboBox cb && !cb.IsEditable && cb.HasItems) cb.SelectedIndex = 0;
		return this;
	}

	/// <summary>
	/// Adds items as <b>IEnumerable</b> to the last added <see cref="ItemsControl"/> (<see cref="ComboBox"/>, etc), with "lazy" option.
	/// </summary>
	/// <param name="items">An <b>IEnumerable</b> that contains items (eg array, List) or generates items (eg returned from a yield-return function).</param>
	/// <param name="lazy">Retrieve items when (if) showing the dropdown part of the <b>ComboBox</b> first time.</param>
	/// <exception cref="NotSupportedException">
	/// - The last added element is not <b>ItemsControl</b>.
	/// - <i>lazy</i> is true and the last added element is not <b>ComboBox</b>.
	/// </exception>
	public AGuiBuilder Items(System.Collections.IEnumerable items, bool lazy = false) => lazy ? Items(true, o => o.ItemsSource = items) : _Items(null, items);

	/// <summary>
	/// Sets callback function that should add items to the last added <see cref="ComboBox"/> later.
	/// </summary>
	/// <param name="once">Call the function once. If false, calls on each drop down.</param>
	/// <param name="onDropDown">Callback function that should add items. Called when (if) showing the dropdown part of the <b>ComboBox</b> first time. Don't need to clear old items.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>ComboBox</b>.</exception>
	public AGuiBuilder Items(bool once, Action<ComboBox> onDropDown)
	{
		var c = Last as ComboBox ?? throw new NotSupportedException("Items(): Last added must be ComboBox");
		EventHandler d = null;
		d = (_, _) => {
			if (once) c.DropDownOpened -= d;
			c.Items.Clear();
			onDropDown(c);
		};
		c.DropDownOpened += d;
		return this;
	}

	/// <summary>
	/// Selects an item of the last added <see cref="Selector"/> (<see cref="ComboBox"/>, etc).
	/// </summary>
	/// <param name="index">0-based item index</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>Selector</b>.</exception>
	/// <seealso cref="Items"/>.
	public AGuiBuilder Select(int index)
	{
		var c = Last as Selector ?? throw new NotSupportedException("Items(): Last added must be Selector, for example ComboBox or ListBox");
		c.SelectedIndex = index;
		return this;
	}

	/// <summary>
	/// Selects an item of the last added <see cref="Selector"/> (<see cref="ComboBox"/>, etc).
	/// </summary>
	/// <param name="item">An added item.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>Selector</b>.</exception>
	/// <seealso cref="Items"/>.
	public AGuiBuilder Select(object item)
	{
		var c = Last as Selector ?? throw new NotSupportedException("Items(): Last added must be Selector, for example ComboBox or ListBox");
		c.SelectedItem = item;
		return this;
	}

	/// <summary>
	/// Adds inlines to the last added <see cref="TextBlock"/>.
	/// </summary>
	/// <param name="inlines">
	/// Arguments of type:
	/// - <see cref="Inline"/> of any type, eg <b>Run</b>, <b>Bold</b>, <b>Hyperlink</b>.
	/// - <b>Action</b> - action to run when the last added <b>Hyperlink</b> clicked (see example).
	/// - string that starts with "&lt;a>", "&lt;b>", "&lt;i>", "&lt;u>", like <c>"&lt;a>link"</c> - adds inline of type <see cref="Hyperlink"/>, <b>Bold</b>, <b>Italic</b>, <b>Underline</b>.
	/// - other string - plain text.
	/// - <see cref="UIElement"/>.
	/// </param>
	/// <exception cref="NotSupportedException">The last added element is not <b>TextBlock</b>.</exception>
	/// <exception cref="ArgumentException">Unsupported argument type.</exception>
	/// <remarks>
	/// Adds inlines to <see cref="TextBlock.Inlines"/>.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// .R.Add<TextBlock>().Text("Text ", "<b>bold ", "<a>link", new Action(() => AOutput.Write("click")), " ", new Run("color") { Foreground=Brushes.Blue, Background=Brushes.Cornsilk, FontSize=20 }, ".")
	/// ]]></code>
	/// </example>
	public AGuiBuilder Text(params object[] inlines)
	{
		var c = Last as TextBlock ?? throw new NotSupportedException("Text(): Last added must be TextBlock");
		var k = c.Inlines;
		k.Clear();
		Hyperlink link = null;
		foreach (var v in inlines)
		{
			Inline n = null; int i;
			switch (v)
			{
				case Hyperlink x:
					n = link = x;
					break;
				case Inline x:
					n = x;
					break;
				case Action x when link != null:
					link.Click += (o, e) => x();
					continue;
				case UIElement x:
					k.Add(x);
					continue;
				case string x:
					if (x.Starts('<') && (i = x.Starts(false, "<a>", "<b>", "<i>", "<u>")) > 0)
					{
						var run = new Run(x[3..]);
						switch (i)
						{
							case 1: n = link = new Hyperlink(run); break;
							case 2: n = new Bold(run); break;
							case 3: n = new Italic(run); break;
							case 4: n = new Underline(run); break;
						}
					}
					else
					{
						k.Add(x);
						continue;
					}
					break;
				default: throw new ArgumentException("Text(): Expected string, Inline, UIElement or hyperlink Action"); //not x.ToString(). Reserve other types for the future.
			}
			k.Add(n);
			//FUTURE: support nested. Now it seems rarely used.
			//FUTURE: add images easier.
		}
		return this;
	}

	/// <summary>
	/// Loads a web page or RTF text from a file or URL into the last added element.
	/// </summary>
	/// <param name="source">File or URL to load. Supported element types and sources:
	/// <see cref="WebBrowser"/>, <see cref="Frame"/> - URL or file path.
	/// <see cref="RichTextBox"/> - path of a local .rtf file.
	/// </param>
	/// <exception cref="NotSupportedException">
	/// - Unsupported element type.
	/// - <b>RichTextBox</b> source does not end with ".rtf".
	/// </exception>
	/// <remarks>
	/// If fails to load, prints warning. See <see cref="AWarning.Write"/>.
	/// </remarks>
	public AGuiBuilder Load(string source)
	{
		var c = Last;
		bool bad = false;
		try
		{
			source = _UriNormalize(source);
			switch (c)
			{
				case WebBrowser u: u.Source = new Uri(source); break;
				case Frame u: u.Source = new Uri(source); break;
				case RichTextBox u when source.Ends(".rtf", true):
					using (var fs = File.OpenRead(source)) { u.Selection.Load(fs, DataFormats.Rtf); }
					//also supports DataFormats.Text,Xaml,XamlPackage. If need HTML, download and try HtmlToXamlConverter. See https://www.codeproject.com/Articles/1097390/Displaying-HTML-in-a-WPF-RichTextBox
					break;
				default: bad = true; break;
			}
		}
		catch (Exception ex) { AWarning.Write(ex.ToString(), -1); }
		if (bad) throw new NotSupportedException($"Load(): Unsupported type of element or source.");
		return this;
	}

	/// <summary>
	/// Loads image into the last added <see cref="System.Windows.Controls.Image"/>.
	/// </summary>
	/// <param name="source"><see cref="Image.Source"/>.</param>
	/// <param name="stretch"><see cref="Image.Stretch"/>.</param>
	/// <param name="stretchDirection"><see cref="Image.StretchDirection"/>.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>Image</b>.</exception>
	public AGuiBuilder Image(ImageSource source, Stretch stretch = Stretch.None, StretchDirection stretchDirection = StretchDirection.DownOnly)
		 => _Image(source, null, stretch, stretchDirection);

	AGuiBuilder _Image(ImageSource source, string file, Stretch stretch, StretchDirection stretchDirection)
	{
		var c = Last as Image ?? throw new NotSupportedException("Image(): Last added must be Image");
		if (file != null) try { source = new BitmapImage(_Uri(file)); } catch (Exception ex) { AWarning.Write(ex.ToString(), -1); }
		c.Stretch = stretch; //default Uniform
		c.StretchDirection = stretchDirection; //default Both
		c.Source = source;
		return this;
	}

	/// <summary>
	/// Loads image from a file or URL into the last added <see cref="System.Windows.Controls.Image"/>.
	/// </summary>
	/// <param name="source">File path or URL. Sets <see cref="Image.Source"/>.</param>
	/// <param name="stretch"><see cref="Image.Stretch"/>.</param>
	/// <param name="stretchDirection"><see cref="Image.StretchDirection"/>.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>Image</b>.</exception>
	/// <remarks>
	/// If fails to load, prints warning. See <see cref="AWarning.Write"/>.
	/// </remarks>
	public AGuiBuilder Image(string source, Stretch stretch = Stretch.None, StretchDirection stretchDirection = StretchDirection.DownOnly)
		=> _Image(null, source, stretch, stretchDirection);

	/// <summary>
	/// Gets file/folder/etc icon with <see cref="AIcon.GetFileIcon"/> and sets <see cref="Image.Source"/> of the last added <see cref="System.Windows.Controls.Image"/>.
	/// </summary>
	/// <param name="hIcon">Native icon handle. See <see cref="AIcon"/>, <see cref="System.Drawing.SystemIcons"/>. Does nothing if <c>default(IntPtr)</c>.</param>
	/// <param name="dispose">Destroy the native icon object. Default true.</param>
	/// <param name="stretch"><see cref="Image.Stretch"/>.</param>
	/// <param name="stretchDirection"><see cref="Image.StretchDirection"/>.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>Image</b>.</exception>
	/// <remarks>
	/// If fails to convert icon to image, prints warning. See <see cref="AWarning.Write"/>.
	/// </remarks>
	public AGuiBuilder Image(IntPtr hIcon, bool dispose = true, Stretch stretch = Stretch.None, StretchDirection stretchDirection = StretchDirection.DownOnly)
	{
		if (hIcon == default) return this;
		BitmapSource source;
		try { source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(hIcon, default, default); }
		catch (Exception ex) { AWarning.Write(ex.ToString(), -1); return this; }
		finally { if (dispose) AIcon.DestroyIconHandle(hIcon); }
		return _Image(source, null, stretch, stretchDirection);
		//FUTURE: option to not DPI-scale. This could work: source.CopyPixels(pixels), Bitmap.Image.Create(pixels, dpiX, dpiY).
	}

	/// <summary>
	/// Sets vertical or horizontal splitter properties of the last added <see cref="GridSplitter"/>.
	/// </summary>
	/// <param name="vertical">If true, resizes columns, else rows.</param>
	/// <param name="span">How many rows spans vertical splitter, or how many columns spans horizontall splitter. Can be more than row/column count.</param>
	/// <param name="thickness">Width of vertical splitter or height of horizontal.</param>
	/// <exception cref="NotSupportedException">The last added element is not <b>GridSplitter</b>.</exception>
	/// <example>
	/// Vertical splitter.
	/// <code><![CDATA[
	/// var b = new AGuiBuilder("Window").WinSize(400)
	/// 	.Columns(30.., 0, -1) //the middle column is for splitter; the 30 is minimal width
	/// 	.R.Add(out TextBox _)
	/// 	.Add<GridSplitter>().Splitter(true, 2).Brush(Brushes.Orange) //add splitter in the middle column
	/// 	.Add(out TextBox _)
	/// 	.R.Add(out TextBox _).Skip().Add(out TextBox _) //skip the splitter's column
	/// 	.R.AddOkCancel()
	/// 	.End();
	/// if (!b.ShowDialog()) return;
	/// ]]></code>
	/// Horizontal splitter.
	/// <code><![CDATA[
	/// var b = new AGuiBuilder("Window").WinSize(300, 300)
	/// 	.Row(27..).Add("Row", out TextBox _)
	/// 	.Add<GridSplitter>().Splitter(false, 2).Brush(Brushes.Orange)
	/// 	.Row(-1).Add("Row", out TextBox _)
	/// 	.R.AddOkCancel()
	/// 	.End();
	/// if (!b.ShowDialog()) return;
	/// ]]></code>
	/// </example>
	public AGuiBuilder Splitter(bool vertical, int span = 1, double thickness = 4)
	{
		var g = _ParentOfLastAsOrThrow<_Grid>();
		var c = Last as GridSplitter ?? throw new NotSupportedException("Splitter(): Last added must be GridSplitter");
		if (vertical)
		{
			c.HorizontalAlignment = HorizontalAlignment.Center;
			c.VerticalAlignment = VerticalAlignment.Stretch;
			c.ResizeDirection = GridResizeDirection.Columns;
			c.Width = thickness;
			if (span != 1) Grid.SetRowSpan(c, span);
		}
		else
		{
			c.HorizontalAlignment = HorizontalAlignment.Stretch;
			c.VerticalAlignment = VerticalAlignment.Center;
			c.ResizeDirection = GridResizeDirection.Rows;
			c.Height = thickness;
			if (span != 1) g.Span(span);
		}
		c.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
		return this;
	}

	//FUTURE: need a numeric input control. This code is for WinForms NumericUpDown.
	//	public AGuiBuilder Number(decimal? value = null, decimal? min = null, decimal? max=null, decimal? increment=null, int? decimalPlaces=null, bool? thousandsSeparator=null, bool? hex =null) {
	//		var c = Last as NumericUpDown ?? throw new NotSupportedException("Number(): Last added must be NumericUpDown");
	//		if(min!=null) c.Minimum=min.Value;
	//		if(max!=null) c.Maximum=max.Value;
	//		if(increment!=null) c.Increment=increment.Value;
	//		if(decimalPlaces!=null) c.DecimalPlaces=decimalPlaces.Value;
	//		if(thousandsSeparator!=null) c.ThousandsSeparator=thousandsSeparator.Value;
	//		if(hex!=null) c.Hexadecimal=hex.Value;
	//		if(value!=null) c.Value=value.Value; else c.Text=null;
	//		return this;
	//	}

	#endregion

	#region nested panel

	AGuiBuilder _Start(_PanelBase p, bool childOfLast)
	{
		_p.BeforeAdd(childOfLast ? GBAdd.ChildOfLast : 0);
		_AddToParent(p.panel, childOfLast);
		_p = p;
		return this;
	}

	AGuiBuilder _Start<T>(_PanelBase p, out T container, object header) where T : HeaderedContentControl, new()
	{
		Add(out container, header);
		container.Content = p.panel;
		if (container is GroupBox) p.panel.Margin = new Thickness(0, 2, 0, 0);
		_p = p;
		return this;
	}

	/// <summary>
	/// Adds <see cref="Grid"/> panel (table) that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="childOfLast">See <see cref="GBAdd.ChildOfLast"/>.</param>
	/// <remarks>
	/// How <see cref="Last"/> changes: after calling this function it is the grid (<see cref="Panel"/>); after adding an element it is the element; finally, after calling <b>End</b> it is the grid if <i>childOfLast</i> false, else its parent. The same with all <b>StartX</b> functions.
	/// </remarks>
	public AGuiBuilder StartGrid(bool childOfLast = false) => _Start(new _Grid(this), childOfLast);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="Grid"/> panel (table) that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="header">Header text/content.</param>
	/// <remarks>
	/// How <see cref="Last"/> changes: after calling this function it is the grid (<see cref="Panel"/>); after adding an element it is the element; finally, after calling <b>End</b> it is the content control (grid's parent). The same with all <b>StartX</b> functions.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// .StartGrid<GroupBox>("Group")
	/// ]]></code>
	/// </example>
	public AGuiBuilder StartGrid<T>(object header) where T : HeaderedContentControl, new() => _Start(new _Grid(this), out T _, header);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="Grid"/> panel (table) that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="container">Receives content control's variable. The function creates new control of the type.</param>
	/// <param name="header">Header text/content.</param>
	/// <example>
	/// <code><![CDATA[
	/// .StartGrid(out Expander g, "Expander").Also(_=> { g.IsExpanded=true; })
	/// ]]></code>
	/// </example>
	public AGuiBuilder StartGrid<T>(out T container, object header) where T : HeaderedContentControl, new() => _Start(new _Grid(this), out container, header);

	/// <summary>
	/// Adds <see cref="Canvas"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="childOfLast">See <see cref="GBAdd.ChildOfLast"/>.</param>
	/// <remarks>
	/// For each added control call <see cref="XY"/> or use indexer like <c>[x, y]</c> or <c>[x, y, width, height]</c>.
	/// </remarks>
	public AGuiBuilder StartCanvas(bool childOfLast = false) => _Start(new _Canvas(this), childOfLast);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="Canvas"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="header">Header text/content.</param>
	public AGuiBuilder StartCanvas<T>(object header) where T : HeaderedContentControl, new() => _Start(new _Canvas(this), out T _, header);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="Canvas"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="container">Receives content control's variable. The function creates new control of the type.</param>
	/// <param name="header">Header text/content.</param>
	public AGuiBuilder StartCanvas<T>(out T container, object header) where T : HeaderedContentControl, new() => _Start(new _Canvas(this), out container, header);

	/// <summary>
	/// Adds <see cref="DockPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="childOfLast">See <see cref="GBAdd.ChildOfLast"/>.</param>
	/// <remarks>
	/// For added elements call <see cref="Dock"/>, maybe except for the last element that fills remaining space.
	/// </remarks>
	public AGuiBuilder StartDock(bool childOfLast = false) => _Start(new _DockPanel(this), childOfLast);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="DockPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="header">Header text/content.</param>
	public AGuiBuilder StartDock<T>(object header) where T : HeaderedContentControl, new() => _Start(new _DockPanel(this), out T _, header);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="DockPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="container">Receives content control's variable. The function creates new control of the type.</param>
	/// <param name="header">Header text/content.</param>
	public AGuiBuilder StartDock<T>(out T container, object header) where T : HeaderedContentControl, new() => _Start(new _DockPanel(this), out container, header);

	/// <summary>
	/// Adds <see cref="StackPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="vertical"></param>
	/// <param name="childOfLast">See <see cref="GBAdd.ChildOfLast"/>.</param>
	public AGuiBuilder StartStack(bool vertical = false, bool childOfLast = false) => _Start(new _StackPanel(this, vertical), childOfLast);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="StackPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="header">Header text/content.</param>
	/// <param name="vertical"></param>
	public AGuiBuilder StartStack<T>(object header, bool vertical = false) where T : HeaderedContentControl, new() => _Start(new _StackPanel(this, vertical), out T _, header);

	/// <summary>
	/// Adds a headered content control (<see cref="GroupBox"/>, <see cref="Expander"/>, etc) with child <see cref="StackPanel"/> panel that will contain elements added with <see cref="Add"/> etc. Finally call <see cref="End"/> to return to current panel.
	/// </summary>
	/// <param name="container">Receives content control's variable. The function creates new control of the type.</param>
	/// <param name="header">Header text/content.</param>
	/// <param name="vertical"></param>
	public AGuiBuilder StartStack<T>(out T container, object header, bool vertical = false) where T : HeaderedContentControl, new() => _Start(new _StackPanel(this, vertical), out container, header);

	/// <summary>
	/// Adds right-bottom-aligned horizontal stack panel (<see cref="StartStack"/>) for adding OK, Cancel and more buttons.
	/// When don't need more buttons, use just <see cref="AddOkCancel"/>.
	/// </summary>
	/// <example>
	/// <code><![CDATA[
	/// .StartOkCancel().AddOkCancel().AddButton("Apply", null).Width(70).End()
	/// ]]></code>
	/// </example>
	public AGuiBuilder StartOkCancel()
	{
		var pa = _p;
		StartStack();
		if (!(pa is _Canvas))
		{
			_p.panel.HorizontalAlignment = HorizontalAlignment.Right;
			_p.panel.VerticalAlignment = VerticalAlignment.Bottom;
			_p.panel.Margin = new Thickness(0, 2, 0, 0);
		}
		return this;
	}

	#endregion

	#region util

	bool _IsNested => _p.parent != null;

	bool _IsWindowEnded => _p.ended && _p.parent == null;

	Window _FindWindow(DependencyObject c) => _window ?? Window.GetWindow(c);

	void _ThrowIfNotWindow([CallerMemberName] string func = null)
	{
		if (_window == null) throw new InvalidOperationException(func + "(): Container is not Window");
	}

	Control _LastAsControlOrThrow([CallerMemberName] string caller = null) => (Last as Control) ?? throw new InvalidOperationException(caller + "(): Last added element is not Control");

	_PanelBase _ParentOfLast => ReferenceEquals(Last, _p.panel) ? _p.parent : _p;

	T _ParentOfLastAsOrThrow<T>([CallerMemberName] string caller = null) where T : _PanelBase
	{
		return _ParentOfLast is T t ? t : throw new InvalidOperationException($"{caller}() not in {typeof(T).Name[1..]} panel.");
	}

	//	void _ThrowIfParentOfLastIs<TControl>([CallerMemberName] string caller = null) where TControl : Panel {
	//		if(Last.Parent is TControl) throw new InvalidOperationException($"{caller}() in {typeof(TControl).Name} panel.");
	//	}

	static string _UriNormalize(string source) => APath.Normalize(source, flags: PNFlags.CanBeUrlOrShell);

	static Uri _Uri(string source) => new Uri(_UriNormalize(source));

	//Returns true if probably using a custom theme. I don't know what is the correct way, but this should work in most cases. Fast.
	static bool _IsCustomTheme()
	{
		if (s_isCustomTheme == null)
		{
			var app = Application.Current;
			s_isCustomTheme = app != null && app.Resources.MergedDictionaries.Count > 0;
		}
		return s_isCustomTheme == true;
	}
	static bool? s_isCustomTheme;

	#endregion
}

namespace Au.Types
{

	/// <summary>
	/// Used with <see cref="AGuiBuilder"/> constructor to specify the type of the root panel.
	/// </summary>
	public enum GBPanelType
	{
		///
		Grid,
		///
		Canvas,
		///
		Dock,
		///
		VerticalStack,
		///
		HorizontalStack,
	}

	/// <summary>
	/// Flags for <see cref="AGuiBuilder.Add"/>.
	/// </summary>
	[Flags]
	public enum GBAdd
	{
		/// <summary>
		/// Add as child of <see cref="AGuiBuilder.Last"/>, which can be of type (or base type):
		/// - <see cref="ContentControl"/>. Adds as its <see cref="ContentControl.Content"/> property. For example you can add a <b>CheckBox</b> in a <b>Button</b>.
		/// - <see cref="Decorator"/>, for example <see cref="Border"/>. Adds as its <see cref="Decorator.Child"/> property.
		/// </summary>
		ChildOfLast = 1,

		/// <summary>
		/// Don't adjust some properties (padding, specified in <see cref="AGuiBuilder.Options"/>, etc) of some control types. Just set default margin, except if <i>ChildOfLast</i>.
		/// </summary>
		DontSetProperties = 2,
	}

	/// <summary>
	/// Used with <see cref="AGuiBuilder"/> functions for width/height parameters. Allows to specify minimal and/or maximal values too.
	/// </summary>
	/// <remarks>
	/// Has implicit conversions from double, Range and tuple (double length, Range minMax).
	/// To specify width or height, pass an integer or double value, like <c>100</c> or <c>15.25</c>.
	/// To specify minimal value, pass a range like <c>100..</c>.
	/// To specify maximal value, pass a range like <c>..100</c>.
	/// To specify minimal and maximal values, pass a range like <c>100..500</c>.
	/// To specify width or height and minimal or/and maximal values, pass a tuple like <c>(100, 50..)</c> or <c>(100, ..200)</c> or <c>(100, 50..200)</c>.
	/// </remarks>
	public struct GBLength
	{
		double _v;
		Range _r;

		GBLength(double v, Range r)
		{
			if (r.Start.IsFromEnd || (r.End.IsFromEnd && r.End.Value != 0)) throw new ArgumentException();
			_v = v; _r = r;
		}

		///
		public static implicit operator GBLength(double v) => new GBLength { _v = v, _r = .. };

		///
		public static implicit operator GBLength(Range v) => new GBLength(double.NaN, v);

		///
		public static implicit operator GBLength((double length, Range minMax) v) => new GBLength(v.length, v.minMax);

		/// <summary>
		/// Gets the width or height value. Returns false if not set.
		/// </summary>
		public bool GetLength(out double value)
		{
			value = _v;
			return !double.IsNaN(_v);
		}

		/// <summary>
		/// Gets the minimal value. Returns false if not set.
		/// </summary>
		public bool GetMin(out int value)
		{
			value = _r.Start.Value;
			return value > 0;
		}

		/// <summary>
		/// Gets the maximal value. Returns false if not set.
		/// </summary>
		public bool GetMax(out int value)
		{
			value = _r.End.Value;
			return !_r.End.IsFromEnd;
		}

		/// <summary>
		/// Sets <b>Width</b> or <b>Height</b> or/and <b>MinWidth</b>/<b>MinHeight</b> or/and <b>MaxWidth</b>/<b>MaxHeight</b> of the element.
		/// </summary>
		/// <param name="e">Element.</param>
		/// <param name="height">Set <b>Height</b>. If false, sets <b>Width</b>.</param>
		public void ApplyTo(FrameworkElement e, bool height)
		{
			if (GetLength(out double d)) { if (height) e.Height = d; else e.Width = d; }
			if (GetMin(out int i)) { if (height) e.MinHeight = i; else e.MinWidth = i; }
			if (GetMax(out i)) { if (height) e.MaxHeight = i; else e.MaxWidth = i; }
		}
	}

	/// <summary>
	/// Used with <see cref="AGuiBuilder"/> functions to specify width/height of columns and rows. Allows to specify minimal and/or maximal values too.
	/// Like <see cref="GBLength"/>, but has functions to create <see cref="ColumnDefinition"/> and <see cref="RowDefinition"/>. Also has implicit conversion from these types.
	/// </summary>
	public struct GBGridLength
	{
		double _v;
		Range _r;
		DefinitionBase _def;

		GBGridLength(double v, Range r)
		{
			if (r.Start.IsFromEnd || (r.End.IsFromEnd && r.End.Value != 0)) throw new ArgumentException();
			_v = v; _r = r; _def = null;
		}

		///
		public static implicit operator GBGridLength(double v) => new GBGridLength { _v = v, _r = .. };

		///
		public static implicit operator GBGridLength((double length, Range minMax) v) => new GBGridLength(v.length, v.minMax);

		///
		public static implicit operator GBGridLength(Range v) => new GBGridLength(-1, v);

		///
		public static implicit operator GBGridLength(DefinitionBase v) => new GBGridLength { _def = v };

		/// <summary>
		/// Creates column definition object from assigned width or/and min/max width values. Or just returns the assigned or previously created object.
		/// </summary>
		public ColumnDefinition Column
		{
			get
			{
				if (_def is ColumnDefinition d) return d;
				d = new ColumnDefinition { Width = _GridLength(_v) };
				if (_r.Start.Value > 0) d.MinWidth = _r.Start.Value;
				if (!_r.End.IsFromEnd) d.MaxWidth = _r.End.Value;
				_def = d;
				return d;
			}
		}

		/// <summary>
		/// Creates row definition object from assigned height or/and min/max height values. Or just returns the assigned or previously created object.
		/// </summary>
		public RowDefinition Row
		{
			get
			{
				if (_def is RowDefinition d) return d;
				d = new RowDefinition { Height = _GridLength(_v) };
				if (_r.Start.Value > 0) d.MinHeight = _r.Start.Value;
				if (!_r.End.IsFromEnd) d.MaxHeight = _r.End.Value;
				_def = d;
				return d;
			}
		}

		GridLength _GridLength(double d)
		{
			if (d > 0) return new GridLength(d, GridUnitType.Pixel);
			if (d < 0) return new GridLength(-d, GridUnitType.Star);
			return new GridLength();
		}
	}

	/// <summary>
	/// Arguments for <see cref="AGuiBuilder.AlsoAll"/> callback function.
	/// </summary>
	public class GBAlsoAllArgs
	{
		/// <summary>
		/// Gets 0-based column index of last added control, or -1 if not in grid.
		/// </summary>
		public int Column { get; internal set; }

		/// <summary>
		/// Gets 0-based row index of last added control, or -1 if not in grid.
		/// </summary>
		public int Row { get; internal set; }
	}

	/// <summary>
	/// Arguments for <see cref="AGuiBuilder.AddButton"/> callback function.
	/// </summary>
	public class GBButtonClickArgs : CancelEventArgs
	{
		/// <summary>
		/// Gets the button.
		/// </summary>
		public Button Button { get; internal set; }

		/// <summary>
		/// Gets the window.
		/// </summary>
		public Window Window { get; internal set; }
	}

	/// <summary>
	/// Flags for <see cref="AGuiBuilder.AddButton"/>.
	/// </summary>
	[Flags]
	public enum GBBFlags
	{
		/// <summary>It is OK button (<see cref="Button.IsDefault"/>, closes window, validates, etc).</summary>
		OK = 1,

		/// <summary>It is Cancel button (<see cref="Button.IsCancel"/>, closes window).</summary>
		Cancel = 2,

		/// <summary>Perform validation like OK button. For example if it is Apply button.</summary>
		Validate = 4,
	}

//rejected. Unsafe etc. For example, when assigning to object, uses CheckBool whereas the user may expect bool.
//	/// <summary>
//	/// <see cref="CheckBox"/> that can be used like bool.
//	/// For example instead of <c>if(c.IsChecked == true)</c> can be used <c>if(c)</c>.
//	/// </summary>
//	public class CheckBool : CheckBox
//	{
//		///
//		public CheckBool()
//		{
//			this.SetResourceReference(StyleProperty, typeof(CheckBox));
//		}
//
//		/// <summary>
//		/// Returns true if <see cref="ToggleButton.IsChecked"/> == true.
//		/// </summary>
//		public static implicit operator bool(CheckBool c) => c.IsChecked.GetValueOrDefault();
//	}
}