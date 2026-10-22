using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A shader layer's uniform list: what the shader declares, joined to what layout.yaml binds.
public class ShaderLayerModelTests
{
    const string Shader = "uniform float heat; uniform float2 drift; half4 main(float2 p) { return half4(heat, drift, 1); }";

    static TempTheme Theme(string uniforms)
    {
        var theme = TempTheme.With(
            "canvas: { orientation: landscape }\n" +
            $"background:\n  - shader: shaders/glow.sksl\n{uniforms}" +
            "widgets: []\n");
        Directory.CreateDirectory(Path.Combine(theme.Folder, "shaders"));
        File.WriteAllText(Path.Combine(theme.Folder, "shaders", "glow.sksl"), Shader);
        return theme;
    }

    static readonly YamlPath Layer = YamlPath.Parse("background[0]");

    [Fact]
    public void Declared_uniforms_are_listed_with_their_bindings_and_an_unused_one_after()
    {
        using var theme = Theme("    uniforms:\n      heat: \"cpu.load / 100\"\n      gone: 1\n");
        using var session = EditorSession.Open(theme.Folder);

        var model = ShaderLayerModel.Read(session, Layer);

        Assert.Null(model.Problem);
        Assert.Equal(
            [
                new UniformRow("heat", 1, ["cpu.load / 100"], Declared: true),
                new UniformRow("drift", 2, [], Declared: true),
                new UniformRow("gone", 1, ["1"], Declared: false),
            ],
            model.Rows, UniformRowComparer.Instance);
    }

    [Fact]
    public void A_binding_round_trips_and_a_broken_expression_is_refused()
    {
        using var theme = Theme("");
        using var session = EditorSession.Open(theme.Folder);

        session.Apply(new SetUniform(Layer, "drift", ["0.5", "gpu.load / 100"]));
        Assert.Equal(["0.5", "gpu.load / 100"], ShaderLayerModel.Read(session, Layer).Rows.Single(r => r.Name == "drift").Values);
        Assert.Null(session.LoadError);
        Assert.Contains("[0.5, \"gpu.load / 100\"]", session.Document.Text);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("Set uniform", session.History.NextUndo?.Label);

        var before = session.Document.Text;
        Assert.NotNull(ShaderLayerModel.Check("cpu.load +"));
        Assert.Throws<ArgumentException>(() => session.Apply(new SetUniform(Layer, "heat", ["cpu.load +"])));
        Assert.Equal(before, session.Document.Text);
        Assert.Equal(1, session.History.Count);
    }

    [Fact]
    public void An_unused_binding_is_removed()
    {
        using var theme = Theme("    uniforms:\n      gone: 1\n");
        using var session = EditorSession.Open(theme.Folder);
        var before = session.Document.Text;

        session.Apply(new RemoveUniform(Layer, "gone"));

        Assert.DoesNotContain(ShaderLayerModel.Read(session, Layer).Rows, r => r.Name == "gone");
        Assert.Null(session.LoadError);
        Assert.Equal(1, session.History.Count);
        Assert.True(session.Undo());
        Assert.Equal(before, session.Document.Text);
    }

    [Fact]
    public void The_cost_hint_compares_a_frame_with_the_budget_at_the_themes_fps()
    {
        using var theme = Theme("");
        using var session = EditorSession.Open(theme.Folder);

        var hint = ShaderLayerModel.Read(session, Layer).Cost(new(1920, 480), fps: 30);

        Assert.Equal(TimeSpan.FromSeconds(1.0 / 30), hint.Budget);
        Assert.False(hint.OverBudget);
        Assert.Contains("33 ms budget at 30 fps", hint.Text);
    }

    [Theory]
    [InlineData("    uniforms: { heat: 1, gone: 2 } # keep\n")]
    [InlineData("    uniforms:\n      heat: 1 # keep\n      gone: 2\n")]
    public void Replacing_a_binding_keeps_other_bytes_and_selection_and_undo_restores_it(string uniforms)
    {
        using var theme = Theme(uniforms);
        using var session = theme.Open();
        session.Select([Layer]);
        var before = session.Document.Text;

        session.Apply(new SetUniform(Layer, "heat", ["cpu.load / 100"]));

        Assert.Equal(before.Replace("heat: 1", "heat: \"cpu.load / 100\""), session.Document.Text);
        Assert.Equal(Layer, session.SelectedLayer);
        Assert.Equal(1, session.History.Count);
        var after = session.Document.Text;
        Assert.True(session.Undo());
        Assert.Equal(before, session.Document.Text);
        Assert.Equal(Layer, session.SelectedLayer);
        Assert.True(session.Redo());
        Assert.Equal(after, session.Document.Text);
    }

    [Theory]
    [InlineData("background:\n  - shader: shaders/glow.sksl\n")]
    [InlineData("background: [{ shader: shaders/glow.sksl }]\n")]
    public void Creating_the_uniform_map_and_removing_a_declared_binding_are_undoable(string background)
    {
        using var theme = Theme("");
        var layout = "canvas: { orientation: landscape }\n" + background + "# leave this alone\nwidgets: []\n";
        File.WriteAllText(theme.LayoutPath, layout);
        using var session = theme.Open();

        session.Apply(new SetUniform(Layer, "heat", ["0.50"]));
        Assert.Contains("heat: 0.50", session.Document.Text);
        Assert.Contains("# leave this alone\nwidgets: []\n", session.Document.Text);
        Assert.True(session.Undo());
        Assert.Equal(layout, session.Document.Text);
        Assert.True(session.Redo());
        var bound = session.Document.Text;

        session.Apply(new RemoveUniform(Layer, "heat"));
        Assert.Empty(ShaderLayerModel.Read(session, Layer).Rows.Single(r => r.Name == "heat").Values);
        Assert.True(session.Undo());
        Assert.Equal(bound, session.Document.Text);
    }

    [Fact]
    public void A_block_list_and_unused_vector_keep_their_component_text()
    {
        using var theme = Theme("    uniforms:\n      drift:\n        - 0.50\n        - 'gpu.load / 100'\n      gone: [1, \"cpu.load\"]\n");
        using var session = theme.Open();

        var rows = ShaderLayerModel.Read(session, Layer).Rows;

        Assert.Equal(["0.50", "gpu.load / 100"], rows.Single(r => r.Name == "drift").Values);
        Assert.Equal(new UniformRow("gone", 2, ["1", "cpu.load"], Declared: false), rows.Last(), UniformRowComparer.Instance);
    }

    [Fact]
    public void A_broken_shader_reports_its_problem_without_listing_bindings_as_unused()
    {
        using var theme = Theme("    uniforms: { heat: 1 }\n");
        File.WriteAllText(Path.Combine(theme.Folder, "shaders", "glow.sksl"), "this is not a shader");
        using var session = theme.Open();

        var model = ShaderLayerModel.Read(session, Layer);

        Assert.NotNull(model.Problem);
        Assert.Empty(model.Rows);
    }

    [Theory]
    [InlineData("../outside.sksl", "outside the theme folder")]
    [InlineData("shaders/missing.sksl", "could not be read")]
    public void An_unavailable_shader_reports_a_problem(string path, string problem)
    {
        using var theme = Theme("");
        File.WriteAllText(theme.LayoutPath, theme.Text.Replace("shaders/glow.sksl", path));
        using var session = theme.Open();

        var model = ShaderLayerModel.Read(session, Layer);

        Assert.Contains(problem, model.Problem);
        Assert.Empty(model.Rows);
        Assert.Throws<InvalidOperationException>(() => model.Cost(new(1920, 480), 30));
    }

    [Fact]
    public void Cost_compiles_the_current_file_again()
    {
        using var theme = Theme("    scale: 0.5\n");
        using var session = theme.Open();
        var model = ShaderLayerModel.Read(session, Layer);
        File.WriteAllText(Path.Combine(theme.Folder, "shaders", "glow.sksl"), "invalid shader");

        Assert.Throws<InvalidOperationException>(() => model.Cost(new(1920, 480), 30));
    }

    [Fact]
    public void Check_returns_the_expression_parsers_reason()
    {
        var error = Assert.Throws<ExpressionException>(() => Expression.Parse("cpu.load +"));
        Assert.Equal(error.Message, ShaderLayerModel.Check("cpu.load +"));
        Assert.NotNull(ShaderLayerModel.Check(""));
    }

    [Fact]
    public void Cost_and_numbers_use_invariant_culture_and_only_costs_above_budget_are_skipped()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Null(ShaderLayerModel.Check("0.5"));
            Assert.Null(ShaderLayerModel.Check("gpu.load / 100"));
            Assert.NotNull(ShaderLayerModel.Check("0,5"));
            var budget = TimeSpan.FromSeconds(1.0 / 30);
            var within = new CostHint(TimeSpan.FromMilliseconds(6), budget);
            Assert.Equal("About 6 ms a frame at this scale, within the 33 ms budget at 30 fps", within.Text);
            Assert.False(new CostHint(budget, budget).OverBudget);
            var over = new CostHint(TimeSpan.FromMilliseconds(40), budget);
            Assert.True(over.OverBudget);
            Assert.Equal("would take about 40 ms a frame, over the 33 ms budget at 30 fps: the screen skips it. Lower its scale.", over.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Uniform_commands_refuse_a_locked_layer_without_an_undo_step()
    {
        using var theme = Theme("    locked: true\n    uniforms: { heat: 1 }\n");
        using var session = theme.Open();
        var before = session.Document.Text;

        Assert.Throws<LayoutEditException>(() => session.Apply(new SetUniform(Layer, "heat", ["2"])));
        Assert.Throws<LayoutEditException>(() => session.Apply(new RemoveUniform(Layer, "heat")));
        Assert.Equal(before, session.Document.Text);
        Assert.Equal(0, session.History.Count);
    }

    sealed class UniformRowComparer : IEqualityComparer<UniformRow>
    {
        public static UniformRowComparer Instance { get; } = new();

        public bool Equals(UniformRow? x, UniformRow? y) => ReferenceEquals(x, y) ||
            x is not null && y is not null && x.Name == y.Name && x.Components == y.Components &&
            x.Declared == y.Declared && x.Values.SequenceEqual(y.Values);

        public int GetHashCode(UniformRow row)
        {
            var hash = new HashCode();
            hash.Add(row.Name);
            hash.Add(row.Components);
            hash.Add(row.Declared);
            foreach (var value in row.Values) hash.Add(value);
            return hash.ToHashCode();
        }
    }
}
