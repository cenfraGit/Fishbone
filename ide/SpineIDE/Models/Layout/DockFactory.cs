using System;
using System.Collections.Generic;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using SpineIDE.Panels;
using SpineIDE.Views.Editor;

namespace SpineIDE.Models.Layout;

public class DockFactory : Factory
    {
        public override IRootDock CreateLayout()
        {
            var variableExplorer = new VariableExplorerVM { Id = "Variables", Title = "Variable Explorer" };
            var imagePreview = new ImagePreviewVM { Id = "Preview", Title = "Image Preview" };
            var scriptEditor = new ScriptEditorVM("New", null, "");

            //var previewToolDock = new ToolDock { ActiveDockable = imagePreview, VisibleDockables = CreateList<IDockable>(imagePreview) };
            var variablesToolDock = new ToolDock { ActiveDockable = variableExplorer, VisibleDockables = CreateList<IDockable>(variableExplorer) };
            var documentDock = new DocumentDock { Id = "Scripts", ActiveDockable = scriptEditor, VisibleDockables = CreateList<IDockable>(scriptEditor) };

            //previewToolDock.Proportion = 0.5;
            //variablesToolDock.Proportion = 0.5;

            var leftVerticalPanel = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                Proportion = 0.25,
                VisibleDockables = CreateList<IDockable>
                (
                    //previewToolDock,
                    //new ProportionalDockSplitter(),
                    variablesToolDock
                )
            };

            documentDock.Proportion = 0.75;

            // main layout
            var proportionalDock = new ProportionalDock
            {
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>
                (
                    leftVerticalPanel, // nested vertical
                    new ProportionalDockSplitter(),
                    documentDock
                )
            };

            // root view
            var rootDock = CreateRootDock();
            rootDock.IsCollapsable = false;
            rootDock.ActiveDockable = proportionalDock;
            rootDock.DefaultDockable = proportionalDock;
            rootDock.VisibleDockables = CreateList<IDockable>(proportionalDock);

            return rootDock;
        }

        public override void InitLayout(IDockable layout)
        {
            ContextLocator = new Dictionary<string, Func<object?>>
            {
                ["Variables"] = () => layout,
                ["Preview"]   = () => layout,
                ["Editor"]    = () => layout
            };

            base.InitLayout(layout);
        }
    }