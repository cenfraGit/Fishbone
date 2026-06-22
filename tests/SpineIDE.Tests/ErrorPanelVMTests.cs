using Dock.Model.Controls;
using Dock.Model.Core;
using SpineIDE.Models.Layout;
using SpineIDE.Panels;
using SpineIDE.Services;

namespace SpineIDE.Tests;

public class ErrorPanelVMTests
{
    [Fact]
    public void Errors_ReflectSharedServiceAdditionsAndClearing()
    {
        var errorService = new ErrorService();
        var panel = new ErrorPanelVM(errorService);
        var changedProperties = new List<string?>();
        panel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        errorService.AddError(new ScriptExecutionError("Failure"));

        Assert.Same(errorService.Errors, panel.Errors);
        Assert.Single(panel.Errors);
        Assert.Equal("Failure", panel.Errors[0].ExMessage);
        Assert.Equal("Failure", panel.PanelText);
        Assert.Contains(nameof(TextPanelVM.PanelText), changedProperties);

        changedProperties.Clear();
        errorService.ClearErrors();

        Assert.Empty(panel.Errors);
        Assert.Equal(string.Empty, panel.PanelText);
        Assert.Contains(nameof(TextPanelVM.PanelText), changedProperties);
    }

    [Fact]
    public void Errors_WithLocation_RenderLineAndColumn()
    {
        var errorService = new ErrorService();
        var panel = new ErrorPanelVM(errorService);

        errorService.AddError(new ScriptExecutionError("Undefined variable \"x\".", 3, 5));
        errorService.AddError(new ScriptExecutionError("Object is not callable.", 10, null));
        errorService.AddError(new ScriptExecutionError("No location error.", null, null));

        Assert.Equal(3, panel.Errors[0].Line);
        Assert.Equal(5, panel.Errors[0].Column);
        Assert.True(panel.Errors[0].HasLocation);
        Assert.Equal("Line 3, column 5", panel.Errors[0].LocationDisplay);

        Assert.True(panel.Errors[1].HasLocation);
        Assert.Equal("Line 10", panel.Errors[1].LocationDisplay);

        Assert.False(panel.Errors[2].HasLocation);
        Assert.Equal(string.Empty, panel.Errors[2].LocationDisplay);

        Assert.Contains("Line 3, column 5: Undefined variable \"x\".", panel.PanelText);
        Assert.Contains("Line 10: Object is not callable.", panel.PanelText);
        Assert.Contains("No location error.", panel.PanelText);
    }

    [Fact]
    public void CreateLayout_PlacesOutputAndErrorsInCollapsedBottomToolDock()
    {
        var output = new OutputPanelVM();
        var errors = new ErrorPanelVM(new ErrorService());
        var layout = new DockFactory(output, errors).CreateLayout();

        var bottomDock = FindDockContaining(layout, errors);

        Assert.NotNull(bottomDock);
        Assert.Same(output, bottomDock.ActiveDockable);
        Assert.Equal(new IDockable[] { output, errors }, bottomDock.VisibleDockables);
        Assert.False(bottomDock.IsExpanded);
    }

    private static IToolDock? FindDockContaining(IDockable dockable, IDockable target)
    {
        if (dockable is IToolDock toolDock && toolDock.VisibleDockables?.Contains(target) == true)
            return toolDock;

        if (dockable is not IDock dock || dock.VisibleDockables is null)
            return null;

        foreach (var child in dock.VisibleDockables)
        {
            var result = FindDockContaining(child, target);
            if (result is not null)
                return result;
        }

        return null;
    }
}