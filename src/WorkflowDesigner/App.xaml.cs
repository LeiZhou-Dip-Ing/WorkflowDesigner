using System.Windows;
using Prism.DryIoc;
using Prism.Ioc;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Services;
using WorkflowCore.WpfDemo.Services.Editing;
using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowCore.WpfDemo.Services.Drafts;
using WorkflowCore.WpfDemo.Services.Ui;
using WorkflowCore.WpfDemo.Services.Workspace;
using WorkflowCore.WpfDemo.Services.Runtime;
using WorkflowCore.WpfDemo.Services.Scripting;
using WorkflowCore.WpfDemo.Services.Projects;
using WorkflowCore.WpfDemo.ViewModels;
using WorkflowCore.WpfDemo.Views;
using WorkflowRuntime.ScriptCompiler;
using WorkflowDesigner.WpfSdk;

namespace WorkflowCore.WpfDemo;

public partial class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        var designerRegistry = new WorkflowDesignerRegistry();
        WorkflowDesignerRegistryHost.Initialize(designerRegistry);
        BuiltInDesignerRegistration.Register(designerRegistry);

        var designerPluginLoader = new DesignerPluginLoader(designerRegistry);
        var designerPluginDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "designer-plugins");
        foreach (var result in designerPluginLoader.LoadDirectory(designerPluginDirectory))
        {
            if (!result.Loaded && !string.IsNullOrWhiteSpace(result.Error))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Designer plugin load failed: {result.AssemblyPath}: {result.Error}");
            }
        }

        containerRegistry.RegisterInstance<IWorkflowDesignerRegistry>(designerRegistry);
        containerRegistry.RegisterInstance(designerPluginLoader);
        containerRegistry.RegisterSingleton<WorkflowEditorJsonSerializer>();
        containerRegistry.RegisterSingleton<SharpScriptReferenceProvider>();
        containerRegistry.RegisterSingleton<ISharpScriptCompiler, SharpScriptCompiler>();
        containerRegistry.RegisterSingleton<ISharpScriptLocalRunner, SharpScriptLocalRunner>();
        containerRegistry.RegisterSingleton<ISharpScriptLibraryCache, SharpScriptLibraryCache>();
        containerRegistry.RegisterSingleton<ISharpScriptLibraryManagerDialog, SharpScriptLibraryManagerDialog>();
        containerRegistry.RegisterSingleton<ISharpScriptTemplateFactory, SharpScriptTemplateFactory>();
        containerRegistry.RegisterSingleton<EditorSession>();
        containerRegistry.RegisterSingleton<IEditorDocumentPersistence, JsonEditorDocumentPersistence>();
        containerRegistry.RegisterSingleton<IRuntimeApiClient, RuntimeApiClient>();
        containerRegistry.RegisterSingleton<IEditorActionCatalog, EditorActionCatalog>();
        containerRegistry.RegisterSingleton<ILocalDraftStore, LocalDraftStore>();
        containerRegistry.RegisterSingleton<IEditorDialogs, WindowsEditorDialogs>();
        containerRegistry.RegisterSingleton<IEditorFileDialogs, WindowsEditorFileDialogs>();
        containerRegistry.RegisterSingleton<IProtectedWorkflowPresentation, ProtectedWorkflowPresentation>();
        containerRegistry.RegisterSingleton<IProtectedWorkflowImportService, ProtectedWorkflowImportService>();
        containerRegistry.RegisterSingleton<IRecentProjectRepository, JsonRecentProjectRepository>();
        containerRegistry.RegisterSingleton<IWorkflowProjectFileService, WorkflowProjectFileService>();
        containerRegistry.RegisterSingleton<IProjectWorkspaceFactory, ProjectWorkspaceFactory>();
        containerRegistry.RegisterInstance(TimeProvider.System);
        containerRegistry.RegisterSingleton<IUiDispatcher, UiThreadDispatcher>();
        containerRegistry.RegisterSingleton<IUiTimerFactory, UiThreadTimerFactory>();
        containerRegistry.RegisterSingleton<IActionLogWindowService, ActionLogWindowService>();
        containerRegistry.RegisterSingleton<IWorkflowThemeService, WorkflowThemeService>();
        containerRegistry.RegisterSingleton<IMethodLineEditor, MethodLineEditor>();
        containerRegistry.RegisterSingleton<IVariableEditor, VariableEditor>();
        containerRegistry.RegisterSingleton<IActionPropertyEditor, ActionPropertyEditor>();
        containerRegistry.RegisterSingleton<EditorDocumentWorkspace>();
        containerRegistry.RegisterSingleton<LocalDraftAutosave>();
        containerRegistry.RegisterSingleton<ActionRunLog>();
        containerRegistry.RegisterSingleton<RuntimeRunSession>();
        containerRegistry.RegisterSingleton<RuntimeWorkspaceSync>();
        containerRegistry.RegisterSingleton<RuntimeDeployment>();
        containerRegistry.RegisterSingleton<IViewModelFactory, PrismViewModelFactory>();
        containerRegistry.RegisterSingleton<IMethodEditorViewModelFactory, PrismMethodEditorViewModelFactory>();
        containerRegistry.RegisterSingleton<ICSharpScriptEditorViewModelFactory, PrismCSharpScriptEditorViewModelFactory>();
        containerRegistry.Register<MainWindowViewModel>();
        containerRegistry.RegisterSingleton<ApplicationShellViewModel>();
        containerRegistry.Register<MethodEditorViewModel>();
        containerRegistry.Register<CSharpScriptEditorViewModel>();
        containerRegistry.Register<MethodEditorView>();
        containerRegistry.Register<CSharpScriptEditorView>();
    }
}
