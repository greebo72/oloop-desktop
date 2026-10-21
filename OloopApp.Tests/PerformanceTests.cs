using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Input;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using Xunit.Abstractions;

namespace OloopApp.Tests;

/// <summary>
/// Timing run against a large data root made by scripts/gen-testdata.py.
/// Does nothing unless OLOOP_PERF_ROOT is set, so the normal test run is
/// unaffected. It writes to that folder (closes and reopens one task).
///
///   OLOOP_PERF_ROOT=/path/to/data dotnet test --filter PerformanceTests
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(PerformanceTests).Assembly);

    private static string? Root => Environment.GetEnvironmentVariable("OLOOP_PERF_ROOT");

    private static async Task<T> OnUiThread<T>(Func<T> action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private double Time(string label, Action action, int runs = 3)
    {
        var times = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        output.WriteLine($"{label,-52} median {times[times.Count / 2],8:F0} ms  (min {times[0]:F0}, max {times[^1]:F0})");
        return times[times.Count / 2];
    }

    private static OloopSettings Settings(string root, bool showClosed) => new()
    {
        FilePath = Path.Combine(Path.GetTempPath(), "oloop-perf-settings-" + Guid.NewGuid().ToString("N") + ".json"),
        DataRoot = root,
        Actor = "perf",
        ShowClosed = showClosed,
    };

    [Fact]
    public async Task Measure_Against_Large_Data_Root()
    {
        var root = Root;
        if (string.IsNullOrEmpty(root))
            return;

        // --- Core: scan + parse, no UI ---
        OloopSnapshot snapshot = null!;
        var store = new OloopStore(root);
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            snapshot = await store.LoadAsync();
            output.WriteLine($"{"OloopStore.LoadAsync",-52} run {i + 1}: {sw.Elapsed.TotalMilliseconds,8:F0} ms");
        }

        output.WriteLine($"{snapshot.Projects.Count} projects, {snapshot.Tasks.Count} tasks, {snapshot.Unreadable.Count} unreadable");

        // --- Shell refresh (load + dashboard + list rebuild) ---
        var shell = await OnUiThread(() => new MainViewModel(Settings(root, showClosed: false)));
        for (var i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            await shell.RefreshAsync();
            output.WriteLine($"{"MainViewModel.RefreshAsync (dashboard)",-52} run {i + 1}: {sw.Elapsed.TotalMilliseconds,8:F0} ms");
        }

        // --- Biggest project: list, grouping, search ---
        var big = snapshot.Projects.OrderByDescending(p => p.TaskCount).First();
        output.WriteLine($"biggest project: {big.Name}, {big.TaskCount} tasks ({big.OpenTaskCount} open)");

        await OnUiThread(() =>
        {
            Time("TaskListViewModel ctor (open only)", () => _ = new TaskListViewModel(big, shell, Settings(root, false)));
            var list = new TaskListViewModel(big, shell, Settings(root, true));
            Time("TaskListViewModel ctor (with closed)", () => _ = new TaskListViewModel(big, shell, Settings(root, true)));
            Time("list.Refresh() (with closed)", list.Refresh);

            foreach (var mode in new[] { "Created", "Modified", "Type", "Status" })
            {
                var grouped = new TaskListViewModel(big, shell, new OloopSettings
                {
                    FilePath = Path.GetTempFileName(), DataRoot = root, Actor = "perf", ShowClosed = true, Grouping = mode,
                });
                Time($"list.Refresh() grouped by {mode}", grouped.Refresh);
            }

            // One keystroke = one Refresh of every list that listens to the shell.
            Time("search keystroke (list with closed)", () =>
            {
                shell.SearchText = "x";
                shell.SearchText = "";
            });
        });

        // --- Real window: open project, render, type into search ---
        await OnUiThread(() =>
        {
            var window = new MainWindow { DataContext = shell };
            window.Show();
            Time("dashboard layout+render", () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }, 1);

            var card = shell.Dashboard.Projects.First(c => c.Project.Id == big.Id);
            var sw = Stopwatch.StartNew();
            shell.OpenProjectCommand.Execute(card);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            output.WriteLine($"{"open biggest project (VM + layout)",-52} {sw.Elapsed.TotalMilliseconds,8:F0} ms");

            var tl = shell.Split!.TaskList;
            tl.ShowClosed = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            output.WriteLine($"rows in list: {tl.Groups.Sum(g => g.Rows.Count)}");

            Time("search keystroke incl. layout (window)", () =>
            {
                shell.SearchText = shell.SearchText == "a" ? "ab" : "a";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }, 5);
            shell.SearchText = "";

            window.Close();
        });

        // --- Write path: one close + reopen, each does a full RefreshAsync ---
        var task = snapshot.Tasks.First(t => t.ProjectId == big.Id && !t.IsClosed);
        await Session.Dispatch<bool>(async () =>
        {
            var sw = Stopwatch.StartNew();
            var ok = await shell.SetTaskClosedAsync(task, big, true);
            output.WriteLine($"{"SetTaskClosedAsync (write + full reload)",-52} {sw.Elapsed.TotalMilliseconds,8:F0} ms ok={ok}");

            var closed = shell.Tasks.First(t => t.Id == task.Id);
            sw.Restart();
            ok = await shell.SetTaskClosedAsync(closed, big, false);
            output.WriteLine($"{"reopen (write + full reload)",-52} {sw.Elapsed.TotalMilliseconds,8:F0} ms ok={ok}");
            return true;
        }, CancellationToken.None);
    }
}
