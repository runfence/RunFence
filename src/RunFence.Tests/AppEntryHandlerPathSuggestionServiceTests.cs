using RunFence.Apps;
using RunFence.Apps.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class AppEntryHandlerPathSuggestionServiceTests
{
    [Fact]
    public void TrySuggest_NonExecutableSelection_SkipsSearch()
    {
        var reader = new TrackingTargetReader();
        var service = new AppEntryHandlerPathSuggestionService(reader, new FakeIconProbe(new()
        {
            [@"C:\Apps\readme.txt"] = HandlerPathIconPresence.HasIcon
        }));

        var success = service.TrySuggest(@"C:\Apps\readme.txt", null, out _);

        Assert.False(success);
        Assert.False(reader.Called);
    }

    [Fact]
    public void TrySuggest_PassesTargetAccountSid_ToReader()
    {
        const string TargetAccountSid = "S-1-5-21-1000";
        var reader = new TrackingTargetReader(
            _ => new[]
            {
                new HandlerCommandTarget(
                    @"C:\Apps\MyApp\Source\Target.exe",
                    HandlerCommandTargetRegistryScope.TargetAccount,
                    ".exe",
                    @"""C:\Apps\MyApp\Source\Target.exe""",
                    null,
                    null)
            });
        var service = new AppEntryHandlerPathSuggestionService(reader, new FakeIconProbe(new()
        {
            [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.HasIcon
        }));

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", TargetAccountSid, out var suggestion);

        Assert.True(success);
        Assert.Equal(@"C:\Apps\MyApp\Source\Target.exe", suggestion.ReplacementPath);
        Assert.True(reader.Called);
        Assert.Equal(TargetAccountSid, reader.LastSid);
    }

    [Fact]
    public void TrySuggest_SelectsScriptTargetWithoutExecutableSubsystemChecks()
    {
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\Scripts\HelperScript.ps1",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".ps1",
                        @"""C:\Apps\Scripts\HelperScript.ps1"" %1",
                        null,
                        null)
                }),
            new FakeIconProbe(new()
            {
                [@"C:\Apps\Scripts\app.ps1"] = HandlerPathIconPresence.HasIcon,
                [@"C:\Apps\Scripts\HelperScript.ps1"] = HandlerPathIconPresence.HasIcon
            }));

        var success = service.TrySuggest(@"C:\Apps\Scripts\app.ps1", null, out var suggestion);

        Assert.True(success);
        Assert.Equal(@"C:\Apps\Scripts\HelperScript.ps1", suggestion.ReplacementPath);
    }

    [Fact]
    public void TrySuggest_ReturnsFalseWhenSelectedIconIsUnknown()
    {
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\Target.exe",
                        null,
                        null)
                }),
            new FakeIconProbe(new()
            {
                [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.Unknown,
                [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.HasIcon
            }));

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out _);

        Assert.False(success);
    }

    [Fact]
    public void TrySuggest_UsesResolvedPathProbe_WhenNoExplicitDefaultIconExists()
    {
        var probe = new FakeIconProbe(new()
        {
            [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.NoIcon
        });
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\Target.exe",
                        null,
                        null)
                }),
            probe);

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out _);

        Assert.False(success);
        Assert.Equal(1, probe.CallCount.GetValueOrDefault(@"C:\Apps\MyApp\Source\Target.exe"));
    }

    [Fact]
    public void TrySuggest_SameExecutableNameUnderSelectedFolder_DoesNotRequireMatchingIconPresence()
    {
        var probe = new FakeIconProbe(new()
        {
            [@"C:\Users\Vlad\AppData\Local\Postman\postman.exe"] = HandlerPathIconPresence.NoIcon
        });
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        "postman",
                        @"""C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe"" ""%1""",
                        null,
                        null)
                }),
            probe);

        var success = service.TrySuggest(
            @"C:\Users\Vlad\AppData\Local\Postman\postman.exe",
            null,
            out var suggestion);

        Assert.True(success);
        Assert.Equal(
            @"C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe",
            suggestion.ReplacementPath);
        Assert.Equal(0, probe.CallCount.GetValueOrDefault(
            @"C:\Users\Vlad\AppData\Local\Postman\app-12.7.6\Postman.exe"));
    }

    [Fact]
    public void TrySuggest_UsesExplicitDefaultIconProbe()
    {
        var probe = new FakeIconProbe(new()
        {
            [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Icons\Target.ico"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.NoIcon
        });
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\Target.exe",
                        @"""C:\Icons\Target.ico"",1",
                        @"C:\Icons\Target.ico")
                    {
                        HasExplicitDefaultIcon = true
                    }
                }),
            probe);

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out var suggestion);

        Assert.True(success);
        Assert.Equal(@"C:\Apps\MyApp\Source\Target.exe", suggestion.ReplacementPath);
        Assert.Equal(1, probe.CallCount.GetValueOrDefault(@"C:\Icons\Target.ico"));
        Assert.Equal(0, probe.CallCount.GetValueOrDefault(@"C:\Apps\MyApp\Source\Target.exe"));
    }

    [Fact]
    public void TrySuggest_DoesNotProbeCommandTarget_WhenExplicitDefaultIconIsUnknown()
    {
        var probe = new FakeIconProbe(new()
        {
            [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.HasIcon
        });
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\Target.exe",
                        @"""C:\Icons\Missing.ico""",
                        @"C:\Icons\Missing.ico")
                    {
                        HasExplicitDefaultIcon = true
                    }
                }),
            probe);

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out _);

        Assert.False(success);
        Assert.Equal(0, probe.CallCount.GetValueOrDefault(@"C:\Apps\MyApp\Source\Target.exe"));
    }

    [Fact]
    public void TrySuggest_ReturnsFalseWhenNoMatchingExtensionTargets()
    {
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Helper.txt",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".txt",
                        @"C:\Apps\MyApp\Helper.txt",
                        null,
                        null)
                }),
            new FakeIconProbe(new()
            {
                [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
                [@"C:\Apps\MyApp\Helper.txt"] = HandlerPathIconPresence.HasIcon
            }));

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out _);

        Assert.False(success);
    }

    [Fact]
    public void TrySuggest_IgnoresTargetsOutsideFolderAndSamePath()
    {
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"D:\Other\App\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"D:\Other\App\Target.exe",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\App.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\App.exe",
                        null,
                        null)
                }),
            new FakeIconProbe(new()
            {
                [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
                [@"D:\Other\App\Target.exe"] = HandlerPathIconPresence.HasIcon
            }));

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out _);

        Assert.False(success);
    }

    [Fact]
    public void TrySuggest_ReturnsFalseForMultipleValidCandidates()
    {
        var probe = new FakeIconProbe(new()
        {
            [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\A\TargetA.exe"] = HandlerPathIconPresence.HasIcon,
            [@"C:\Apps\MyApp\Source\B\TargetB.exe"] = HandlerPathIconPresence.HasIcon
        });
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\A\TargetA.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\A\TargetA.exe",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\B\TargetB.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\B\TargetB.exe",
                        null,
                        null)
                }),
            probe);

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out var suggestion);

        Assert.Equal(default(HandlerPathSuggestion), suggestion);
        Assert.False(success);
        Assert.Equal(1, probe.CallCount.GetValueOrDefault(@"C:\Apps\MyApp\Source\A\TargetA.exe"));
        Assert.Equal(1, probe.CallCount.GetValueOrDefault(@"C:\Apps\MyApp\Source\B\TargetB.exe"));
    }

    [Fact]
    public void TrySuggest_FiltersToOneCandidateFromManyThenSuggests()
    {
        var service = new AppEntryHandlerPathSuggestionService(
            new TrackingTargetReader(
                _ => new[]
                {
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\Target.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\Target.exe",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\TargetWrong.txt",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".txt",
                        @"C:\Apps\MyApp\Source\TargetWrong.txt",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Other\App\TargetNoIcon.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Other\App\TargetNoIcon.exe",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\TargetDuplicate.exe",
                        HandlerCommandTargetRegistryScope.Hklm,
                        ".exe",
                        @"C:\Apps\MyApp\Source\TargetDuplicate.exe",
                        null,
                        null),
                    new HandlerCommandTarget(
                        @"C:\Apps\MyApp\Source\TargetDuplicate.exe",
                        HandlerCommandTargetRegistryScope.InteractiveUser,
                        ".exe",
                        @"C:\Apps\MyApp\Source\TargetDuplicate.exe",
                        null,
                        null)
                }),
            new FakeIconProbe(new()
            {
                [@"C:\Apps\MyApp\Source\App.exe"] = HandlerPathIconPresence.HasIcon,
                [@"C:\Apps\MyApp\Source\Target.exe"] = HandlerPathIconPresence.HasIcon,
                [@"C:\Apps\MyApp\Source\TargetDuplicate.exe"] = HandlerPathIconPresence.NoIcon,
                [@"C:\Other\App\TargetNoIcon.exe"] = HandlerPathIconPresence.NoIcon
            }));

        var success = service.TrySuggest(@"C:\Apps\MyApp\Source\App.exe", null, out var suggestion);

        Assert.True(success);
        Assert.Equal(@"C:\Apps\MyApp\Source\Target.exe", suggestion.ReplacementPath);
    }

    private sealed class TrackingTargetReader(Func<string?, IReadOnlyList<HandlerCommandTarget>>? targets = null) : IHandlerCommandTargetReader
    {
        private readonly Func<string?, IReadOnlyList<HandlerCommandTarget>> _getTargets =
            targets ?? (_ => Array.Empty<HandlerCommandTarget>());

        public bool Called { get; private set; }

        public string? LastSid { get; private set; }

        public IReadOnlyList<HandlerCommandTarget> ReadTargets(string? targetAccountSid)
        {
            Called = true;
            LastSid = targetAccountSid;
            return _getTargets(targetAccountSid);
        }
    }

    private sealed class FakeIconProbe(Dictionary<string, HandlerPathIconPresence>? states = null) : IHandlerPathIconProbe
    {
        private readonly Dictionary<string, HandlerPathIconPresence> _states = states
            ?? new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> CallCount { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HandlerPathIconPresence GetIconPresence(string path)
        {
            CallCount[path] = CallCount.GetValueOrDefault(path, 0) + 1;
            return _states.GetValueOrDefault(path, HandlerPathIconPresence.Unknown);
        }
    }
}
