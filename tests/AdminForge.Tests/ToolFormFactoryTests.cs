using AdminForge.Core.Forms;

namespace AdminForge.Tests;

/// <summary>
/// Form generation. This is the machinery that lets a contributor add a tool without
/// writing markup, so its behaviour is pinned here rather than discovered in review.
/// </summary>
public sealed class ToolFormFactoryTests
{
    private enum Colour
    {
        Red,
        DeepBlue,
        TLSOnly,
    }

    private sealed class SampleInput
    {
        [ToolField("Plain text", Placeholder = "type here", Required = true, MaxLength = 50)]
        public string Text { get; set; } = "default text";

        [ToolField("A number", Min = 1, Max = 10)]
        public int Number { get; set; } = 4;

        [ToolField("A switch")]
        public bool Flag { get; set; } = true;

        [ToolField("From an enum")]
        public Colour Colour { get; set; } = Colour.DeepBlue;

        [ToolField("From a list", Kind = FieldKind.Select, Options = "a,b,c")]
        public string Letter { get; set; } = "b";

        [ToolField("Labelled options", Kind = FieldKind.Select, Options = "1|One,2|Two")]
        public string Numeral { get; set; } = "1";

        [ToolField("A secret", Kind = FieldKind.Password, MaxLength = 200)]
        public string Secret { get; set; } = "should not leak";

        [ToolField("Ordered last", Order = 10)]
        public string Last { get; set; } = "";

        /// <summary>Deliberately unattributed — it must not appear in the form.</summary>
        public string Hidden { get; set; } = "invisible";
    }

    private static readonly ToolFormDescriptor Form = ToolFormFactory.Create(typeof(SampleInput));

    private static ToolFieldDescriptor Field(string name) =>
        Form.Fields.Single(f => f.Name == name);

    [Fact]
    public void Only_attributed_properties_become_fields()
    {
        Assert.DoesNotContain(Form.Fields, f => f.Name == "Hidden");
        Assert.Equal(8, Form.Fields.Count);
    }

    [Fact]
    public void A_null_input_type_produces_an_empty_form()
    {
        ToolFormDescriptor form = ToolFormFactory.Create(null);

        Assert.True(form.IsEmpty);
        Assert.Null(form.InputType);
    }

    [Theory]
    [InlineData("Text", FieldKind.Text)]
    [InlineData("Number", FieldKind.Number)]
    [InlineData("Flag", FieldKind.Checkbox)]
    [InlineData("Colour", FieldKind.Select)]
    [InlineData("Secret", FieldKind.Password)]
    public void Control_kind_is_inferred_from_the_property_type(string name, FieldKind expected) =>
        Assert.Equal(expected, Field(name).Kind);

    [Fact]
    public void Defaults_are_read_from_a_fresh_instance()
    {
        Assert.Equal("default text", Field("Text").DefaultValue);
        Assert.Equal("4", Field("Number").DefaultValue);
        Assert.Equal("true", Field("Flag").DefaultValue);
        Assert.Equal("DeepBlue", Field("Colour").DefaultValue);
    }

    [Fact]
    public void A_password_default_is_never_carried_into_the_form() =>
        Assert.Null(Field("Secret").DefaultValue);

    [Fact]
    public void Enum_options_are_humanised_without_mangling_acronyms()
    {
        IReadOnlyList<FieldOption> options = Field("Colour").Options;

        Assert.Equal(3, options.Count);
        Assert.Equal("Red", options[0].Label);
        Assert.Equal("Deep blue", options[1].Label);

        // A run of capitals is an acronym, not several words.
        Assert.Equal("TLSOnly", options[2].Value);
        Assert.Equal("TLS only", options[2].Label);
    }

    [Fact]
    public void Explicit_options_are_parsed_with_and_without_labels()
    {
        Assert.Equal(["a", "b", "c"], Field("Letter").Options.Select(o => o.Value));
        Assert.Equal(["a", "b", "c"], Field("Letter").Options.Select(o => o.Label));

        Assert.Equal(["1", "2"], Field("Numeral").Options.Select(o => o.Value));
        Assert.Equal(["One", "Two"], Field("Numeral").Options.Select(o => o.Label));
    }

    [Fact]
    public void Constraints_survive_onto_the_descriptor()
    {
        Assert.True(Field("Text").Required);
        Assert.Equal(50, Field("Text").MaxLength);
        Assert.Equal(1, Field("Number").Min);
        Assert.Equal(10, Field("Number").Max);
    }

    [Fact]
    public void Fields_are_ordered_by_order_then_declaration()
    {
        Assert.Equal("Text", Form.Fields[0].Name);
        Assert.Equal("Last", Form.Fields[^1].Name);
    }
}
