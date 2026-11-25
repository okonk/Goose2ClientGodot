using Goose2Client.Map;
using Xunit;

namespace Goose2Client.Tests;

public class ZoneBookTests
{
    [Fact]
    public void A_zone_expires_once_its_time_is_up()
    {
        var book = new ZoneBook();
        Assert.True(book.Add(7, nowMs: 1000, remainingMs: 8000));

        Assert.Empty(book.TakeExpired(8999));
        Assert.Equal(new[] { 7 }, book.TakeExpired(9000));
        Assert.Empty(book.TakeExpired(20000));
    }

    [Fact]
    public void Re_adding_a_known_id_reports_a_replacement()
    {
        var book = new ZoneBook();
        book.Add(7, 0, 1000);

        Assert.False(book.Add(7, 0, 5000));
        Assert.Empty(book.TakeExpired(1000));
    }

    [Fact]
    public void Remove_reports_whether_the_zone_was_known()
    {
        var book = new ZoneBook();
        book.Add(7, 0, 1000);

        Assert.True(book.Remove(7));
        Assert.False(book.Remove(7));
        Assert.Empty(book.TakeExpired(5000));
    }
}
