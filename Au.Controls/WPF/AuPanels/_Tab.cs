﻿using System;
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
using System.Xml.Linq;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Markup;

using Au;
using Au.Types;

namespace Au.Controls.WPF
{
	public partial class AuPanels
	{
		class _Tab : _Dragable
		{
			TabControl _tc = new();
			List<_Panel> _panels = new();
			int _active;
			bool _vertHeader;

			static Style
				s_styleL = XamlResources.Dictionary["TabItemVerticalLeft"] as Style,
				s_styleR = XamlResources.Dictionary["TabItemVerticalRight"] as Style;

			public _Tab(AuPanels pm, _Node parent, XElement x) : base(pm, parent, x) {
				foreach (var e in x.Elements()) {
					var p = new _Panel(pm, this, e);
					_panels.Add(p);
					var tp = new TabItem { Header = p.Name, Content = p.Elem };
					_tc.Items.Add(tp);
				}
				Debug.Assert(_panels.Count >= 2); //see _AutoUpdateXml
				_active = x.Attr("active", 0); if ((uint)_active >= _panels.Count()) _active = 0;

				_tc.Padding = default;
				_tc.TabStripPlacement = this.CaptionAt;
				_tc.SizeChanged += (_, e) => {
					switch (_tc.TabStripPlacement) { case Dock.Top: case Dock.Bottom: return; }
					bool bigger = e.NewSize.Height > e.PreviousSize.Height; if (bigger == _vertHeader) return;
					_VerticalTabHeader(e.NewSize.Height);
				};
			}

			public override FrameworkElement Elem => _tc;
			public List<_Panel> Panels => _panels;
			public int ActiveIndex => _active;
			public _Panel ActivePanel => _panels[_active];

			void _VerticalTabHeader(double height) {
				var tabs = _tc.Items.Cast<TabItem>();
				bool vert2 = _vertHeader ? tabs.Sum(o => o.ActualHeight) <= height - 15 : tabs.Sum(o => o.ActualWidth) <= height;
				if (vert2 == _vertHeader) return;
				_vertHeader = vert2;
				var dock = _tc.TabStripPlacement;
				foreach (var v in tabs) v.Style = vert2 ? (dock == Dock.Left ? s_styleL : s_styleR) : null;

			}

			void _DockTabHeader(Dock dock) {
				if (dock == _tc.TabStripPlacement) return;
				bool sides = dock == Dock.Left || dock == Dock.Right;
				if (_vertHeader) {
					_vertHeader = false;
					foreach (var v in _tc.Items.Cast<TabItem>()) v.Style = null;
				}
				_tc.TabStripPlacement = dock;
				if (sides) _VerticalTabHeader(_tc.ActualHeight);
			}

			public override void Save(XmlWriter x) {
				x.WriteStartElement("tab");
				base._SaveAttributes(x);
				x.WriteAttributeString("active", _active.ToString());
				foreach (var v in _panels) v.Save(x);
				x.WriteEndElement();
			}
		}
	}
}