using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Marquee.Host.Tests.TestSupport;
using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly ListLog log = new();

    public void Dispose() => key.Dispose();

    UpdateChecker Checker(FakeHttpHandler handler, string running = "0.1.0+4393804e1c2b", TimeSpan? timeout = null, ECDsa? releaseKey = null) =>
        new(handler.Client(), running, releaseKey ?? key, log, timeout ?? UpdateChecker.DefaultTimeout);

    static FakeHttpHandler Serving(string tag) => new(_ => FakeHttpHandler.Json(ReleaseJson.Release(tag).ToJsonString()));

    [Fact]
    public async Task A_newer_release_is_offered()
    {
        var offer = await Checker(Serving("v0.2.0")).CheckAsync(CancellationToken.None);

        Assert.NotNull(offer);
        Assert.Equal(new Version(0, 2, 0), offer.Version);
        Assert.Equal("Marquee-0.2.0-x64.msi", offer.Installer.Name);
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task It_asks_GitHub_for_the_latest_release_saying_who_it_is_and_nothing_more()
    {
        var handler = Serving("v0.2.0");

        await Checker(handler, running: "0.1.0+4393804e1c2b").CheckAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.github.com/repos/madjagstudios/marquee/releases/latest"), request.Uri);
        Assert.Equal("Marquee/0.1.0", request.UserAgent);
        Assert.Equal("application/vnd.github+json", request.Accept);
        Assert.False(request.HasAuthorization);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task The_same_or_an_older_release_is_not_offered(string tag)
    {
        Assert.Null(await Checker(Serving(tag)).CheckAsync(CancellationToken.None));
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_prerelease_build_is_offered_its_release()
    {
        var offer = await Checker(Serving("v0.2.0"), running: "0.2.0-rc.1+4393804e1c2b").CheckAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 2, 0), offer?.Version);
    }

    [Fact]
    public async Task Rate_limiting_is_no_offer_logged_once()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = FakeHttpHandler.Json("""{"message": "API rate limit exceeded"}""", HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Limit", "60");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "1792411200");
            return response;
        });

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task Being_asked_to_slow_down_is_rate_limiting_too()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = FakeHttpHandler.Status(HttpStatusCode.Forbidden);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return response;
        });

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Fact]
    public void The_trays_client_leaves_timeouts_to_the_checker_and_the_installer()
    {
        using var client = UpdateHttp.CreateClient();

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limiting_too()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.TooManyRequests));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task No_release_yet_is_no_offer_logged_once()
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"message": "Not Found"}""", HttpStatusCode.NotFound));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("info: ", Assert.Single(log.Entries));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Another_error_is_no_offer_and_one_warning(HttpStatusCode status)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(status));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("warn: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task No_network_is_no_offer_and_one_warning()
    {
        var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("No such host is known."));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.Contains("No such host is known.", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_server_that_doesnt_answer_in_time_is_no_offer_and_one_warning()
    {
        var handler = new FakeHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        var offer = await Checker(handler, timeout: TimeSpan.FromMilliseconds(100)).CheckAsync(CancellationToken.None);

        Assert.Null(offer);
        Assert.StartsWith("warn: ", Assert.Single(log.Entries));
    }

    [Fact]
    public void The_timeout_is_15_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), UpdateChecker.DefaultTimeout);
    }

    [Fact]
    public async Task Cancelling_the_check_cancels_it_rather_than_reporting_a_failure()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new FakeHttpHandler(async (_, cancellationToken) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Checker(handler).CheckAsync(cancel.Token));
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"tag_name": "v0.2.0", "draft": false, "prerelease": false, "assets": []}""")]
    public async Task A_release_it_cant_use_is_no_offer_and_one_warning(string json)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(json));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("warn: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task An_answer_over_1_MB_is_not_read()
    {
        var release = ReleaseJson.Release();
        release["body"] = new string('x', 1024 * 1024);
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json(release.ToJsonString()));

        Assert.Null(await Checker(handler).CheckAsync(CancellationToken.None));
        Assert.StartsWith("warn: ", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task With_the_placeholder_key_built_in_it_doesnt_check_at_all()
    {
        using var placeholder = PlaceholderKey.Open();
        var handler = Serving("v0.2.0");

        Assert.Null(await Checker(handler, releaseKey: placeholder).CheckAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task Without_a_version_of_its_own_it_doesnt_check()
    {
        var handler = Serving("v0.2.0");

        Assert.Null(await Checker(handler, running: "dev").CheckAsync(CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task The_answer_is_read_as_UTF8()
    {
        var release = ReleaseJson.Release();
        release["name"] = "Marquee 0.2.0 — Größer";
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(release.ToJsonString())),
        });

        var offer = await Checker(handler).CheckAsync(CancellationToken.None);

        Assert.Equal("Marquee 0.2.0 — Größer", offer?.Title);
    }

    [Fact]
    public async Task Find_says_when_this_marquee_is_the_latest()
    {
        var result = await Checker(Serving("v0.1.0")).FindAsync(CancellationToken.None);

        Assert.Null(result.Offer);
        Assert.Null(result.Problem);
        Assert.True(result.IsUpToDate);
        Assert.Equal("0.1.0", result.Running);
    }

    [Fact]
    public async Task Find_returns_the_offer_of_a_newer_release()
    {
        var result = await Checker(Serving("v0.2.0")).FindAsync(CancellationToken.None);

        Assert.Equal(new Version(0, 2, 0), result.Offer?.Version);
        Assert.Null(result.Problem);
        Assert.False(result.IsUpToDate);
    }

    [Fact]
    public async Task Find_says_why_a_check_failed_in_the_words_it_logged()
    {
        var handler = new FakeHttpHandler((_, _) => throw new HttpRequestException("No such host is known."));

        var result = await Checker(handler).FindAsync(CancellationToken.None);

        Assert.Null(result.Offer);
        Assert.False(result.IsUpToDate);
        Assert.Contains("No such host is known.", result.Problem);
        Assert.Equal($"warn: {result.Problem}", Assert.Single(log.Entries));
    }

    [Fact]
    public async Task Find_says_why_with_the_placeholder_key_and_HasReleaseKey_is_false()
    {
        using var placeholder = PlaceholderKey.Open();
        var checker = Checker(Serving("v0.2.0"), releaseKey: placeholder);

        var result = await checker.FindAsync(CancellationToken.None);

        Assert.False(checker.HasReleaseKey);
        Assert.True(Checker(Serving("v0.2.0")).HasReleaseKey);
        Assert.False(result.IsUpToDate);
        Assert.Equal($"info: {result.Problem}", Assert.Single(log.Entries));
    }
}
