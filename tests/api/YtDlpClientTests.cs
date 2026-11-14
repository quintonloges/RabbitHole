using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class YtDlpClientTests
{
    private const string Channel = "UC4sdJIrKWKxymZ5lwVSBzTw";
    private sealed class Script : IDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rabbithole-ytdlp-test-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Folder, "fixture.py");
        public Script(string code) { Directory.CreateDirectory(Folder); File.WriteAllText(Path, code); }
        public YtDlpClient Client(TimeSpan? timeout = null, string? executable = null) => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["YouTube:YtDlpPython"] = executable ?? (OperatingSystem.IsWindows() ? "python" : "python3") }).Build(), Path, timeout);
        public void Dispose() { try { Directory.Delete(Folder, true); } catch (IOException) { } }
    }
    private static string PageScript(string mutate = "") => """
        import json,sys,os
        page={"schemaVersion":1,"version":"2026.10.26","channelId":"UC4sdJIrKWKxymZ5lwVSBzTw","start":1,"nextStart":2,"exhausted":False,
          "entries":[{"id":"fixture0001","title":"TEST FIXTURE video","channelId":"UC4sdJIrKWKxymZ5lwVSBzTw","author":"TEST FIXTURE author",
          "description":"Source description","thumbnail":None,"uploadDate":None,"availability":"public","isLive":False,"isUpcoming":False,"index":1}],"warnings":[]}
        """ + "\n" + mutate + "\nprint(json.dumps(page))\n";

    [Fact]
    public async Task Real_child_uses_fixed_arguments_isolated_python_and_no_inherited_application_secrets()
    {
        var previous = Environment.GetEnvironmentVariable("RABBITHOLE_YTDLP_TEST_SECRET");
        Environment.SetEnvironmentVariable("RABBITHOLE_YTDLP_TEST_SECRET", "TEST FIXTURE SECRET");
        try
        {
            using var script = new Script(PageScript("""
                assert sys.flags.isolated == 1
                assert sys.flags.utf8_mode == 1
                assert sys.argv[1:] == ['--channel','UC4sdJIrKWKxymZ5lwVSBzTw','--start','1','--limit','2']
                assert 'RABBITHOLE_YTDLP_TEST_SECRET' not in os.environ
                assert 'HTTP_PROXY' not in os.environ and 'PYTHONPATH' not in os.environ
                """));
            var page = await script.Client().FetchAsync(Channel, 1, 2, default);
            Assert.Equal("2026.10.26", page.Version); Assert.Equal(Channel, page.ChannelId);
            Assert.Equal("fixture0001", Assert.Single(page.Entries).Id); Assert.Null(page.Entries[0].UploadDate);
        }
        finally { Environment.SetEnvironmentVariable("RABBITHOLE_YTDLP_TEST_SECRET", previous); }
    }

    [Fact]
    public async Task Redirected_child_preserves_international_titles_as_utf8()
    {
        using var script = new Script(PageScript("page['entries'][0]['title']='Curiosité — 日本語 🐇'")
            .Replace("print(json.dumps(page))", "print(json.dumps(page,ensure_ascii=False))"));
        var page = await script.Client().FetchAsync(Channel, 1, 2, default);
        Assert.Equal("Curiosité — 日本語 🐇", Assert.Single(page.Entries).Title);
    }

    [Theory]
    [InlineData("other", 1, 2)]
    [InlineData(Channel, 0, 2)]
    [InlineData(Channel, 200001, 2)]
    [InlineData(Channel, 1, 0)]
    [InlineData(Channel, 1, 51)]
    public async Task Invalid_channel_or_range_is_rejected_before_launch(string channel, int start, int limit)
    {
        using var script = new Script("raise Exception('Must not launch')");
        await Assert.ThrowsAsync<ArgumentException>(() => script.Client().FetchAsync(channel, start, limit, default));
    }

    [Fact]
    public async Task Missing_executable_and_helper_have_distinct_safe_messages()
    {
        using var script = new Script(PageScript());
        var executable = System.IO.Path.Combine(script.Folder, "TEST-FIXTURE-PRIVATE-python.exe");
        var missingPython = await Assert.ThrowsAsync<YtDlpException>(() => script.Client(executable: executable).FetchAsync(Channel, 1, 2, default));
        Assert.Equal("executable-missing", missingPython.Code); Assert.DoesNotContain(script.Folder, missingPython.Message);
        File.Delete(script.Path);
        var missingHelper = await Assert.ThrowsAsync<YtDlpException>(() => script.Client().FetchAsync(Channel, 1, 2, default));
        Assert.Equal("helper-missing", missingHelper.Code); Assert.DoesNotContain(script.Folder, missingHelper.Message);
    }

    [Theory]
    [InlineData("dependency-missing")]
    [InlineData("version-mismatch")]
    [InlineData("blocked")]
    [InlineData("rate-limited")]
    [InlineData("network")]
    [InlineData("extractor")]
    [InlineData("request-limit")]
    [InlineData("invalid-output")]
    public async Task Helper_failure_codes_are_safe_and_never_echo_stderr(string code)
    {
        using var script = new Script("import json,sys\nprint(json.dumps({'schemaVersion':1,'version':'unavailable','errorCode':'" + code + "'}))\nsys.stderr.write('TEST FIXTURE SECRET private/path api_key=value')\nsys.exit(1)");
        var error = await Assert.ThrowsAsync<YtDlpException>(() => script.Client().FetchAsync(Channel, 1, 2, default));
        Assert.Equal(code, error.Code); Assert.Contains("yt-dlp", error.Message);
        if (code == "version-mismatch") Assert.Equal("yt-dlp version does not match this release. Rebuild the API image or reinstall the pinned helper dependency.", error.Message);
        if (code == "request-limit") Assert.Equal("yt-dlp reached this release’s archive scan limit. Automatic archive checks are paused; Check YouTube or Find older now can retry.", error.Message);
        Assert.DoesNotContain("SECRET", error.Message); Assert.DoesNotContain("api_key", error.Message);
    }

    [Theory]
    [InlineData("page['schemaVersion']=2")]
    [InlineData("page['channelId']='UCwrongChannel00000000000'")]
    [InlineData("page['start']=2")]
    [InlineData("page['nextStart']=9")]
    [InlineData("page['nextStart']=1")]
    [InlineData("page['entries'][0]['index']=2")]
    [InlineData("page['entries'][0]['uploadDate']='20260230'")]
    [InlineData("page['entries'][0]['title']='x'*1001")]
    [InlineData("del page['entries'][0]['isLive']")]
    [InlineData("page['warnings']=['x'*201]")]
    [InlineData("page['version']='TEST FIXTURE SECRET'")]
    public async Task Protocol_and_bounds_fail_without_returning_untrusted_metadata(string mutate)
    {
        using var script = new Script(PageScript(mutate));
        var error = await Assert.ThrowsAsync<YtDlpException>(() => script.Client().FetchAsync(Channel, 1, 2, default));
        Assert.Equal("invalid-output", error.Code); Assert.DoesNotContain("SECRET", error.Message);
    }

    [Fact]
    public async Task Case_insensitive_contract_and_exhausted_empty_page_are_supported()
    {
        using var script = new Script(PageScript("page['entries']=[];page['nextStart']=1;page['exhausted']=True;page={key.upper():value for key,value in page.items()}"));
        var page = await script.Client().FetchAsync(Channel, 1, 2, default);
        Assert.True(page.Exhausted); Assert.Equal(1, page.NextStart); Assert.Empty(page.Entries);
    }

    [Theory]
    [InlineData("stdout", 500001)]
    [InlineData("stderr", 16385)]
    public async Task Output_overflow_kills_child_and_returns_bounded_safe_error(string stream, int length)
    {
        using var script = new Script($"import sys,time\nsys.{stream}.write('x'*{length})\nsys.{stream}.flush()\ntime.sleep(30)");
        var elapsed = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<YtDlpException>(() => script.Client().FetchAsync(Channel, 1, 2, default));
        Assert.Equal("output-limit", error.Code); Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Timeout_kills_the_entire_child_tree_and_releases_global_gate()
    {
        using var script = new Script("""
            import os,sys,subprocess,time
            child=subprocess.Popen([sys.executable,'-I','-c','import time;time.sleep(30)'])
            with open(os.path.join(os.path.dirname(__file__),'pids.txt'),'w') as output: output.write(str(os.getpid())+','+str(child.pid))
            time.sleep(30)
            """);
        var error = await Assert.ThrowsAsync<YtDlpException>(() => script.Client(TimeSpan.FromSeconds(2)).FetchAsync(Channel, 1, 2, default));
        Assert.Equal("timeout", error.Code);
        var pids = File.ReadAllText(System.IO.Path.Combine(script.Folder, "pids.txt")).Split(',').Select(int.Parse);
        foreach (var pid in pids) AssertStopped(pid);
        using var valid = new Script(PageScript());
        Assert.Single((await valid.Client().FetchAsync(Channel, 1, 2, default)).Entries);
    }

    [Fact]
    public async Task Caller_cancellation_stays_cancellation_and_stops_the_child()
    {
        using var script = new Script("""
            import os,time
            with open(os.path.join(os.path.dirname(__file__),'pid.txt'),'w') as output: output.write(str(os.getpid()))
            time.sleep(30)
            """);
        using var cancel = new CancellationTokenSource();
        var pending = script.Client().FetchAsync(Channel, 1, 2, cancel.Token);
        var pidFile = System.IO.Path.Combine(script.Folder, "pid.txt");
        for (var attempt = 0; !File.Exists(pidFile) && attempt < 100; attempt++) await Task.Delay(50);
        Assert.True(File.Exists(pidFile));
        var pid = int.Parse(File.ReadAllText(pidFile)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); AssertStopped(pid);
    }
    [Fact]
    public async Task Independent_clients_serialize_helper_processes_globally()
    {
        using var script = new Script(PageScript("""
            import time
            lock=os.path.join(os.path.dirname(__file__),'active.lock')
            fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY)
            time.sleep(0.4)
            os.close(fd);os.unlink(lock)
            """));
        var first = script.Client().FetchAsync(Channel, 1, 2, default);
        var second = script.Client().FetchAsync(Channel, 1, 2, default);
        var pages = await Task.WhenAll(first, second);
        Assert.All(pages, page => Assert.Single(page.Entries));
    }

    [Fact]
    public async Task Malformed_stdout_does_not_expose_raw_provider_or_process_details()
    {
        using var script = new Script("print('TEST FIXTURE SECRET api_key=value /private/path')");
        var error = await Assert.ThrowsAsync<YtDlpException>(() => script.Client().FetchAsync(Channel, 1, 2, default));
        Assert.Equal("invalid-output", error.Code); Assert.DoesNotContain("SECRET", error.Message);
        Assert.DoesNotContain("api_key", error.Message); Assert.DoesNotContain("/private", error.Message);
    }

    private static void AssertStopped(int pid)
    {
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited, "Fixture child is still running."); }
        catch (ArgumentException) { }
    }
}
