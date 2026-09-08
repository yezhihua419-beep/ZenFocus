using ChanJing.Core.Models;
using ChanJing.Core.Services;
using Xunit;

namespace ChanJing.Tests;

/// <summary>本地 SQLite 存储测试（使用临时数据库文件）。</summary>
public class AppDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly AppDatabase _db;

    public AppDatabaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "chanjing-db-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _db = new AppDatabase(_dbPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 忽略清理失败 */ }
    }

    [Fact]
    public void SaveAndGetSessions_RoundTrips()
    {
        var session = new FocusSession
        {
            StartedAt = DateTime.Parse("2026-09-08T10:00:00"),
            EndedAt = DateTime.Parse("2026-09-08T10:25:00"),
            PlannedMinutes = 25,
            ActualMinutes = 25,
            State = FocusSessionState.Completed,
            Wish = "写周报",
            DistractionCount = 0
        };
        _db.SaveFocusSession(session);

        var from = DateTime.Parse("2026-09-08T00:00:00");
        var to = from.AddDays(1);
        var list = _db.GetSessions(from, to);

        var item = Assert.Single(list);
        Assert.Equal(25, item.ActualMinutes);
        Assert.Equal(FocusSessionState.Completed, item.State);
        Assert.Equal("写周报", item.Wish);
    }

    [Fact]
    public void GetSessions_FiltersByDateRange()
    {
        _db.SaveFocusSession(new FocusSession
        {
            StartedAt = DateTime.Parse("2026-09-01T10:00:00"),
            ActualMinutes = 10,
            State = FocusSessionState.Completed
        });
        _db.SaveFocusSession(new FocusSession
        {
            StartedAt = DateTime.Parse("2026-09-08T10:00:00"),
            ActualMinutes = 20,
            State = FocusSessionState.Broken
        });

        var list = _db.GetSessions(DateTime.Parse("2026-09-08T00:00:00"), DateTime.Parse("2026-09-09T00:00:00"));

        var item = Assert.Single(list);
        Assert.Equal(20, item.ActualMinutes);
        Assert.Equal(FocusSessionState.Broken, item.State);
    }

    [Fact]
    public void AddAppUsage_AccumulatesAndStores()
    {
        _db.AddAppUsage("2026-09-08", "chrome", "abc123", 120);
        _db.AddAppUsage("2026-09-08", "chrome", "abc123", 30);

        var usage = _db.GetUsageByDay("2026-09-08");

        Assert.True(usage.ContainsKey("chrome"));
        Assert.Equal(150, usage["chrome"]);
    }

    [Fact]
    public void Settings_RoundTrips()
    {
        Assert.Null(_db.GetSetting("today_wish"));

        _db.SetSetting("today_wish", "读完第三章");

        Assert.Equal("读完第三章", _db.GetSetting("today_wish"));
    }
}
