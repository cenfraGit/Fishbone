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
        private readonly OutputPanelVM _outputPanel;

        public DockFactory(OutputPanelVM outputPanel)
        {
            _outputPanel = outputPanel;
        }

        public override IRootDock CreateLayout()
        {
            var variableExplorer = new VariableExplorerVM { Id = "Variables", Title = "Variable Explorer" };
            var scriptEditor = new ScriptEditorVM("New", null, "");

            var variablesToolDock = new ToolDock { ActiveDockable = variableExplorer, VisibleDockables = CreateList<IDockable>(variableExplorer) };
            var outputToolDock = new ToolDock { ActiveDockable = _outputPanel, VisibleDockables = CreateList<IDockable>(_outputPanel) };
            var documentDock = new DocumentDock { Id = "Scripts", ActiveDockable = scriptEditor, VisibleDockables = CreateList<IDockable>(scriptEditor) };

            //variablesToolDock.Proportion = 0.5;
            _outputPanel.Id = "Output";
            _outputPanel.Title = "Output";

            var leftVerticalPanel = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                Proportion = 0.25,
                VisibleDockables = CreateList<IDockable>
                (
                    variablesToolDock
                )
            };

            documentDock.Proportion = 0.75;
            outputToolDock.Proportion = 0.25;

            var editorLayout = new ProportionalDock
            {
                Orientation = Orientation.Horizontal,
                Proportion = 0.75,
                VisibleDockables = CreateList<IDockable>
                (
                    leftVerticalPanel, // nested vertical
                    new ProportionalDockSplitter(),
                    documentDock
                )
            };

            // main layout
            var proportionalDock = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                VisibleDockables = CreateList<IDockable>
                (
                    editorLayout,
                    new ProportionalDockSplitter(),
                    outputToolDock
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
                ["Output"]    = () => layout,
                ["Editor"]    = () => layout
            };

            base.InitLayout(layout);
        }
    }