using Shouldly;
using Fuse.Admin.Components.Forms;

namespace Fuse.Web.Tests;

public sealed class ContentSectionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("<p></p>", false)]
    [InlineData("<p><br></p>", false)]
    [InlineData("<p>&nbsp;</p>", false)]
    [InlineData("<p>Lovely <b>tour</b></p>", true)]
    public void Empty_editor_html_does_not_count_as_text(string? html, bool expected) =>
        ContentSection.HasVisibleText(html).ShouldBe(expected);
}
