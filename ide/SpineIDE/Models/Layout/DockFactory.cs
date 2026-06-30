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
        private readonly ErrorPanelVM _errorPanel;

        public DockFactory(OutputPanelVM outputPanel, ErrorPanelVM errorPanel)
        {
            _outputPanel = outputPanel;
            _errorPanel = errorPanel;
        }

        // exposed so the Views menu can show/hide these tools and the error auto-focus can target them.
        // Everything stays non-collapsable so Dock never auto-removes a dock from under us; this factory
        // reclaims space itself by removing the container docks (and their splitters) when emptied.
        public VariableExplorerVM VariableExplorer { get; private set; } = null!;
        public ToolDock VariablesToolDock { get; private set; } = null!;
        public ToolDock OutputToolDock { get; private set; } = null!;
        public OutputPanelVM OutputPanel => _outputPanel;
        public ErrorPanelVM ErrorPanel => _errorPanel;

        // the left column (hosts the variable explorer) and the two proportional containers it lives in,
        // so a closed panel's space can be reclaimed by detaching its container
        private ProportionalDock _leftPanel = null!;
        private ProportionalDock _editorLayout = null!;
        private ProportionalDock _rootLayout = null!;

        /// <summary>Raised when a tool is actually shown or hidden, so the Views-menu checkmarks stay in sync.</summary>
        public event Action<IDockable, bool>? ToolVisibilityChanged;

        public override IRootDock CreateLayout()
        {
            var variableExplorer = new VariableExplorerVM { Id = "Variables", Title = "Variable Explorer" };
            var scriptEditor = new ScriptEditorVM("New", null, "");

            var variablesToolDock = new ToolDock
            {
                ActiveDockable = variableExplorer,
                VisibleDockables = CreateList<IDockable>(variableExplorer),
                IsCollapsable = false
            };
            var outputToolDock = new ToolDock
            {
                ActiveDockable = _outputPanel,
                VisibleDockables = CreateList<IDockable>(_outputPanel, _errorPanel),
                IsExpanded = false,
                IsCollapsable = false
            };

            VariableExplorer = variableExplorer;
            VariablesToolDock = variablesToolDock;
            OutputToolDock = outputToolDock;
            var documentDock = new DocumentDock { Id = "Scripts", ActiveDockable = scriptEditor, VisibleDockables = CreateList<IDockable>(scriptEditor) };

            //variablesToolDock.Proportion = 0.5;
            _outputPanel.Id = "Output";
            _outputPanel.Title = "Output";
            _errorPanel.Id = "Errors";
            _errorPanel.Title = "Errors";

            var leftVerticalPanel = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                Proportion = 0.25,
                IsCollapsable = false,
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
                IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>
                (
                    documentDock
                )
            };

            // main layout — panels start hidden; Views menu re-adds them via ShowTool
            var proportionalDock = new ProportionalDock
            {
                Orientation = Orientation.Vertical,
                IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>
                (
                    editorLayout
                )
            };

            _leftPanel = leftVerticalPanel;
            _editorLayout = editorLayout;
            _rootLayout = proportionalDock;

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
                ["Errors"]    = () => layout,
                ["Editor"]    = () => layout
            };

            base.InitLayout(layout);
        }

        private bool IsManaged(IDockable dockable) =>
            ReferenceEquals(dockable, VariableExplorer)
            || ReferenceEquals(dockable, _outputPanel)
            || ReferenceEquals(dockable, _errorPanel);

        /// <summary>True when the tool currently occupies space in the layout.</summary>
        public bool IsToolVisible(IDockable tool) => ReferenceEquals(tool, VariableExplorer)
            ? _editorLayout.VisibleDockables?.Contains(_leftPanel) == true
            : OutputToolDock.VisibleDockables?.Contains(tool) == true
              && _rootLayout.VisibleDockables?.Contains(OutputToolDock) == true;

        public void ShowTool(IDockable tool)
        {
            if (IsToolVisible(tool))
            {
                SetActiveDockable(tool);
                return;
            }

            if (ReferenceEquals(tool, VariableExplorer))
            {
                // the left column only hosts the variable explorer, so showing it just re-attaches the column
                EnsureContainer(_editorLayout, _leftPanel, atStart: true);
            }
            else
            {
                EnsureTab(OutputToolDock, tool);
                EnsureContainer(_rootLayout, OutputToolDock, atStart: false);
            }

            SetActiveDockable(tool);
            ToolVisibilityChanged?.Invoke(tool, true);
        }

        public void HideTool(IDockable tool)
        {
            if (!IsToolVisible(tool))
                return;

            if (ReferenceEquals(tool, VariableExplorer))
            {
                // detach the whole left column to reclaim its space; the tool stays inside it for later
                RemoveContainer(_editorLayout, _leftPanel, atStart: true);
            }
            else
            {
                RemoveTab(OutputToolDock, tool);
                // once the bottom strip has no tools left, detach it so the editor reclaims the height
                if (OutputToolDock.VisibleDockables is not { Count: > 0 })
                    RemoveContainer(_rootLayout, OutputToolDock, atStart: false);
            }

            ToolVisibilityChanged?.Invoke(tool, false);
        }

        // routes a tab's close button through the same space-reclaiming hide, so closed tools stay reopenable
        public override void CloseDockable(IDockable dockable)
        {
            if (IsManaged(dockable))
                HideTool(dockable);
            else
                base.CloseDockable(dockable);
        }

        private void EnsureContainer(IDock parent, IDock container, bool atStart)
        {
            var list = parent.VisibleDockables;
            if (list is null || list.Contains(container))
                return;

            if (atStart)
            {
                InsertDockable(parent, container, 0);
                InsertDockable(parent, new ProportionalDockSplitter(), 1);
            }
            else
            {
                AddDockable(parent, new ProportionalDockSplitter());
                AddDockable(parent, container);
            }
        }

        private void RemoveContainer(IDock parent, IDock container, bool atStart)
        {
            var list = parent.VisibleDockables;
            if (list is null)
                return;

            int index = list.IndexOf(container);
            if (index < 0)
                return;

            // drop the splitter adjacent to the container (after it when leading, before it when trailing)
            int splitterIndex = atStart ? index + 1 : index - 1;
            IProportionalDockSplitter? splitter = splitterIndex >= 0 && splitterIndex < list.Count
                ? list[splitterIndex] as IProportionalDockSplitter
                : null;

            RemoveDockable(container, false);
            if (splitter is not null)
                RemoveDockable(splitter, false);
        }

        private void EnsureTab(IDock dock, IDockable tool)
        {
            if (dock.VisibleDockables?.Contains(tool) != true)
                AddDockable(dock, tool);
        }

        private void RemoveTab(IDock dock, IDockable tool)
        {
            if (dock.VisibleDockables?.Contains(tool) == true)
                RemoveDockable(tool, false);
        }
    }