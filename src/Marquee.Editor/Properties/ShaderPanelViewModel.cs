using System.Windows.Threading;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

/// Keeps component editors in place across writes. Cost probes are debounced, run on a worker, and discarded when stale.
public sealed class ShaderPanelViewModel : ObservableObject, IDisposable
{
    readonly PropertiesViewModel _owner;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    CancellationTokenSource? _probe;
    string? _costKey;
    string? _problem;
    CostHint? _cost;
    bool _disposed;
    IReadOnlyList<UniformRowViewModel> _uniforms = [];

    internal ShaderPanelViewModel(PropertiesViewModel owner, YamlPath layer)
    {
        _owner = owner;
        Layer = layer;
    }

    public YamlPath Layer { get; }
    internal PropertiesViewModel Owner => _owner;
    public ShaderInfo? Info { get; private set; }
    public bool IsAnnotated => Info is { Controls.Count: > 0 };
    public bool IsUnannotated => !IsAnnotated;
    internal ValueSources Values => _owner.Workspace!.Values;
    public string? Problem { get => _problem; private set => Set(ref _problem, value); }
    public IReadOnlyList<UniformRowViewModel> Uniforms { get => _uniforms; private set => Set(ref _uniforms, value); }
    public CostHint? Cost { get => _cost; private set { if (Set(ref _cost, value)) { Raise(nameof(CostText)); Raise(nameof(OverBudget)); } } }
    public bool OverBudget => Cost?.OverBudget == true;
    public string? CostText => Cost is not { } cost ? null : cost.OverBudget
        ? Strings.ShaderCostOver(cost.PerFrame.TotalMilliseconds, cost.Budget.TotalMilliseconds, 1 / cost.Budget.TotalSeconds)
        : Strings.ShaderCostWithin(cost.PerFrame.TotalMilliseconds, cost.Budget.TotalMilliseconds, 1 / cost.Budget.TotalSeconds);
    public bool IsEditable => !_disposed && _owner.CanEdit && _owner.Session?.SelectedLayer == Layer &&
        _owner.Model?.Rows.All(row => row.IsEditable) == true;

    internal bool Set(string name, IReadOnlyList<string> values) => IsEditable && _owner.Write(() => new SetUniform(Layer, name, values));
    internal void Remove(string name)
    {
        if (IsEditable) _owner.Write(() => new RemoveUniform(Layer, name));
    }

    internal void Refresh()
    {
        if (_disposed || _owner.Session is not { } session) return;
        var model = ShaderLayerModel.Read(session, Layer);
        Problem = model.Problem;
        Info = model.Info;
        Raise(nameof(Info)); Raise(nameof(IsAnnotated)); Raise(nameof(IsUnannotated));
        if (Uniforms.Count == model.Rows.Count && Uniforms.Zip(model.Rows).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.Components.Count == pair.Second.Components && pair.First.IsUnused == !pair.Second.Declared))
        {
            foreach (var (view, row) in Uniforms.Zip(model.Rows)) view.Update(row);
        }
        else Uniforms = model.Rows.Select(row => new UniformRowViewModel(this, row)).ToList();
        Raise(nameof(Uniforms));

        session.Document.TryGetScalar(Layer.Child("shader"), out var file);
        session.Document.TryGetScalar(Layer.Child("scale"), out var scale);
        var size = session.CanvasSize;
        var fps = session.Theme?.Spec.Canvas.Fps ?? 30;
        // File watcher theme refreshes must also invalidate a probe for a file edited in place.
        var stamp = Problem is not null || file is null ? 0 : File.GetLastWriteTimeUtc(Path.Combine(session.Folder, file)).Ticks;
        var key = $"{file}|{stamp}|{scale}|{size}|{fps}|{Problem}";
        if (_costKey == key) return;
        _costKey = key;
        CancelProbe();
        Cost = null;
        if (Problem is not null) return;
        _probe = new CancellationTokenSource();
        _ = Probe(model, size, fps, _probe.Token);
    }

    async Task Probe(ShaderLayerModel model, SkiaSharp.SKSizeI size, int fps, CancellationToken cancel)
    {
        try
        {
            await Task.Delay(300, cancel).ConfigureAwait(false);
            var cost = await Task.Run(() => model.Cost(size, fps), cancel).ConfigureAwait(false);
            if (cancel.IsCancellationRequested) return;
            await _ui.InvokeAsync(() => { if (!cancel.IsCancellationRequested && !_disposed) Cost = cost; });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if (cancel.IsCancellationRequested) return;
            await _ui.InvokeAsync(() => { if (!cancel.IsCancellationRequested && !_disposed) Problem = ex.Message; });
        }
    }

    void CancelProbe()
    {
        _probe?.Cancel();
        _probe?.Dispose();
        _probe = null;
    }

    public void Dispose()
    {
        _disposed = true;
        CancelProbe();
    }
}
