using Mechanica.Core.Docs;
using Mechanica.Core.Tests.Data;

namespace Mechanica.Core.Tests.Docs;

public class MarkdownBbcodeTests
{
    [Fact]
    public void HeadingsListsAndInlineFormatting()
    {
        var bbcode = MarkdownBbcode.Convert("""
            # Title
            Some **bold**, *italic* and `code [x]` text with a [link](other.md).
              - item one
            2. second
            """);

        Assert.Equal("""
            [font_size=26][b]Title[/b][/font_size]
            Some [b]bold[/b], [i]italic[/i] and [code]code [lb]x][/code] text with a [url=other.md]link[/url].
              • item one
            2. second

            """.ReplaceLineEndings("\n"), bbcode);
    }

    [Fact]
    public void CodeBlocksAreLiteral()
    {
        var bbcode = MarkdownBbcode.Convert("```json\n{ \"a\": [1, **2**] }\n```\n");

        Assert.Equal("[code]{ \"a\": [lb]1, **2**] }[/code]\n", bbcode);
    }

    [Fact]
    public void TablesBecomeAlignedText()
    {
        var bbcode = MarkdownBbcode.Convert("| Key | Meaning |\n|-----|---------|\n| `id` | The id |\n| mass | kg |\n");

        Assert.Equal("[code][b]Key   Meaning[/b]\nid    The id\nmass  kg[/code]\n", bbcode);
    }

    [Fact]
    public void WrappedLinesJoinIntoOneParagraph()
    {
        var bbcode = MarkdownBbcode.Convert("You start at the\ncrash site.\n\nNext one.\n- A list item that\n  goes on here\n- Second\n# Title\n");

        Assert.Equal("You start at the crash site.\n\nNext one.\n• A list item that goes on here\n• Second\n[font_size=26][b]Title[/b][/font_size]\n", bbcode);
    }

    [Fact]
    public void EveryShippedDocConverts()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(TestMods.RepoRoot, "docs"), "*.md"))
        {
            var bbcode = MarkdownBbcode.Convert(File.ReadAllText(file));
            Assert.Equal(bbcode.Split("[code]").Length, bbcode.Split("[/code]").Length);
        }
    }
}
