using System.Linq;
using Goose2Client;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class NoticeFeedModelTests
{
    [Fact]
    public void Add_AppendsNewestLast()
    {
        var model = new NoticeFeedModel();
        model.Add(NoticeType.Info, "a");
        model.Add(NoticeType.Info, "b");
        model.Add(NoticeType.Info, "c");

        Assert.Equal(3, model.Entries.Count);
        Assert.Equal("a", model.Entries[0].Text);
        Assert.Equal("b", model.Entries[1].Text);
        Assert.Equal("c", model.Entries[2].Text);
        Assert.Equal(0.0, model.Entries[0].Age);
    }

    [Fact]
    public void Add_SeventhEvictsOldest()
    {
        var model = new NoticeFeedModel();
        for (int i = 1; i <= 7; i++)
            model.Add(NoticeType.Info, i.ToString());

        Assert.Equal(NoticeFeedModel.MaxEntries, model.Entries.Count);
        var texts = model.Entries.Select(e => e.Text).ToArray();
        Assert.Equal(new[] { "2", "3", "4", "5", "6", "7" }, texts);
    }

    [Fact]
    public void Tick_RemovesEntryAtLifetime()
    {
        var model = new NoticeFeedModel();
        model.Add(NoticeType.Info, "x");

        model.Tick(4.99);
        Assert.Single(model.Entries);

        model.Tick(0.01);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void Tick_ExpiresOnlyOldEntries()
    {
        var model = new NoticeFeedModel();
        model.Add(NoticeType.Info, "A");
        model.Tick(3.0);
        model.Add(NoticeType.Info, "B");
        model.Tick(2.5);

        Assert.Single(model.Entries);
        Assert.Equal("B", model.Entries[0].Text);
        Assert.Equal(2.5, model.Entries[0].Age, 10);
    }

    [Fact]
    public void Alpha_SolidThenLinearFade()
    {
        var model = new NoticeFeedModel();
        var e = model.Add(NoticeType.Info, "x");

        Assert.Equal(1f, NoticeFeedModel.Alpha(e));
        model.Tick(4.0);
        Assert.Equal(1f, NoticeFeedModel.Alpha(e));
        model.Tick(0.5);
        Assert.Equal(0.5f, NoticeFeedModel.Alpha(e), 3);
        model.Tick(0.5);
        Assert.Equal(0f, NoticeFeedModel.Alpha(e));
    }
}
