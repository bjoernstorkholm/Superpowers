using Microsoft.AspNetCore.Components;

namespace Applications.Components;

public partial class Paragraph
{
    [Parameter, EditorRequired] public WordParagraph Value { get; set; } = default!;
}
