using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;
using ConsoleToSvg.Configuration;

namespace ConsoleToSvg.Tests.Configuration;

public sealed class DocumentStoreTests
{
    [Test]
    public void CheckedInSchemasExposeVersionedModelsAndDescriptions()
    {
        var root = FindRepositoryRoot();
        using var config = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "schema", "ConsoleToSvg.ConfigDocument.v1.json"))
        );
        using var scenario = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "schema", "ConsoleToSvg.ScenarioDocument.v1.json"))
        );

        config.RootElement.GetProperty("$id").GetString()
            .ShouldBe("ConsoleToSvg.ConfigDocument.v1.json");
        scenario.RootElement.GetProperty("$id").GetString()
            .ShouldBe("ConsoleToSvg.ScenarioDocument.v1.json");

        var colorSchema = config
            .RootElement.GetProperty("properties")
            .GetProperty("options")
            .GetProperty("properties")
            .GetProperty("environment")
            .GetProperty("properties")
            .GetProperty("color");
        colorSchema.GetProperty("description").GetString()
            .ShouldBe("Whether console2svg overwrites color-related environment variables.");
        config.RootElement.GetProperty("properties").TryGetProperty("$schema", out _).ShouldBeTrue();

        scenario
            .RootElement.GetProperty("properties")
            .GetProperty("scenario")
            .GetProperty("description")
            .GetString()
            .ShouldBe("The Scenario definition to execute.");
    }

    [Test]
    public void OptionsMergePreservesUnspecifiedValuesAndReplacesExplicitEmptyArrays()
    {
        var merged = OptionsMerger.Merge(
            new ConsoleOptions
            {
                Interactive = new InteractiveOptions { Mouse = true },
                Appearance = new AppearanceOptions { Theme = ["nord"], Opacity = 0.8 },
                Render = new RenderOptions { Masking = new MaskingOptions { Strings = ["secret"] } },
            },
            new ConsoleOptions
            {
                Interactive = new InteractiveOptions { Mouse = false },
                Appearance = new AppearanceOptions { Theme = [], Opacity = 0 },
                Render = new RenderOptions { Masking = new MaskingOptions { Strings = [] } },
            }
        );

        merged.Interactive!.Mouse.ShouldBe(false);
        merged.Appearance!.Theme.ShouldBe([]);
        merged.Appearance.Opacity.ShouldBe(0);
        merged.Render!.Masking!.Strings.ShouldBe([]);
    }

    [Test]
    public void ConfigLoadingMergesLocalAndExplicitYamlAndIgnoresUnknownFields()
    {
        using var directory = new TemporaryDirectory();
        var localPath = Path.Combine(directory.Path, "console2svg.config.yaml");
        var explicitPath = Path.Combine(directory.Path, "selected.yaml");
        File.WriteAllText(
            localPath,
            """
            $version: 1
            options:
              terminal:
                width: 100
                height: 30
              appearance:
                theme: [nord]
              future-section:
                enabled: true
            """
        );
        File.WriteAllText(
            explicitPath,
            """
            $version: 1
            options:
              terminal:
                height: 40
              appearance:
                theme: []
            """
        );

        var options = DocumentStore.LoadResolvedSettings(explicitPath, directory.Path).Options;

        options.Terminal!.Width.ShouldBe(100);
        options.Terminal.Height.ShouldBe(40);
        options.Appearance!.Theme.ShouldBe([]);
    }

    [Test]
    public void ConfigLoadingUsesYamlExtensionForAnExtensionlessExplicitPath()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "selected");
        File.WriteAllText(
            path + ".yaml",
            """
            $version: 1
            options:
              terminal:
                width: 111
            """
        );

        var options = DocumentStore.LoadConfigOptions(path, directory.Path);

        options.Terminal!.Width.ShouldBe(111);
    }

    [Test]
    public void ScenarioYamlLoadsStructuredLaunchAndValidatesDocument()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "scenario.yaml");
        File.WriteAllText(
            path,
            """
            $version: 1
            options:
              terminal:
                width: 80
            scenario:
              launch:
                executable: vim
                args: [hello.txt]
                options:
                  terminal:
                    width: 120
                    height: 36
              execute:
                - type: send
                  inputs:
                    - text: hello
                    - keys: Enter
                - type: wait
                  args:
                    text: hello
                    until: present
            """
        );

        var document = DocumentStore.LoadScenario(path);

        document.Options!.Terminal!.Width.ShouldBe(80);
        document.Scenario!.Launch!.Executable.ShouldBe("vim");
        document.Scenario.Launch.Args.ShouldBe(["hello.txt"]);
        document.Scenario.Launch.Options!.Terminal!.Width.ShouldBe(120);
        document.Scenario.Execute![0].Inputs![1].Keys.ShouldBe("Enter");
    }

    [Test]
    public void ScenarioJsonRejectsInvalidKnownValues()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "scenario.json");
        File.WriteAllText(
            path,
            """
            {
              "$version": 1,
              "options": {
                "environment": {
                  "color": "sometimes"
                }
              },
              "scenario": {
                "launch": {
                  "executable": "vim"
                }
              }
            }
            """
        );

        var error = Should.Throw<InvalidDataException>(() => DocumentStore.LoadScenario(path));

        error.Message.ShouldContain("environment.color");
    }

    [Test]
    public void ScenarioSendInputMustSpecifyExactlyOneInputKind()
    {
        var errors = DocumentValidator.Validate(
            new ScenarioDocument
            {
                Scenario = new ScenarioDefinition
                {
                    Launch = new ScenarioLaunch { Executable = "demo" },
                    Execute =
                    [
                        new ScenarioStep
                        {
                            Type = "send",
                            Inputs = [new ScenarioInput { Text = "hello", Keys = "Enter" }],
                        },
                    ],
                },
            }
        );

        errors.ShouldContain(
            "scenario.execute[0].inputs[0] must specify exactly one of text, keys, paste, or raw-hex."
        );
    }

    [Test]
    public async Task ScenarioDocumentsRoundTripThroughYamlAndJson()
    {
        using var directory = new TemporaryDirectory();
        var document = new ScenarioDocument
        {
            Options = new ConsoleOptions
            {
                Terminal = new TerminalOptions { Width = 120 },
                Capture = new CaptureOptions
                {
                    Video = new VideoCaptureOptions { Coalesce = "7.5" },
                },
            },
            Scenario = new ScenarioDefinition
            {
                WorkingDir = new WorkingDirectoryOptions { Temporary = true },
                Launch = new ScenarioLaunch { Executable = "vim", Args = ["notes.txt"] },
                Execute =
                [
                    new ScenarioStep
                    {
                        Type = "send",
                        Inputs =
                        [
                            new ScenarioInput { Text = "hello" },
                            new ScenarioInput { Paste = "pasted" },
                        ],
                    },
                ],
            },
        };

        foreach (var extension in new[] { ".yaml", ".json", "" })
        {
            var path = Path.Combine(directory.Path, $"scenario{extension}");
            await DocumentStore.SaveScenarioAsync(path, document);
            var expectedPath = extension.Length == 0 ? path + ".yaml" : path;
            var savedText = File.ReadAllText(expectedPath);

            savedText.ShouldContain("$version");
            savedText.ShouldContain("working-dir");
            var loaded = DocumentStore.LoadScenario(path);
            loaded.Options!.Terminal!.Width.ShouldBe(120);
            loaded.Options.Capture!.Video!.Coalesce.ShouldBe("7.5");
            loaded.Scenario!.Launch!.Executable.ShouldBe("vim");
            loaded.Scenario.Execute![0].Inputs![0].Text.ShouldBe("hello");
            loaded.Scenario.Execute![0].Inputs![1].Paste.ShouldBe("pasted");
        }
    }

    [Test]
    public void ExplicitConfigurationPathMustExist()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing.yaml");

        var error = Should.Throw<FileNotFoundException>(() =>
            DocumentStore.LoadConfigOptions(path, directory.Path)
        );

        error.Message.ShouldContain(path);
    }

    [Test]
    public void MalformedKnownValuesReportTheDocumentPath()
    {
        using var directory = new TemporaryDirectory();
        foreach (var extension in new[] { ".yaml", ".json" })
        {
            var path = Path.Combine(directory.Path, $"invalid{extension}");
            var content = extension == ".json"
                ? """{"$version":1,"options":{"terminal":{"width":"wide"}}}"""
                : "$version: 1\noptions:\n  terminal:\n    width: wide\n";
            File.WriteAllText(path, content);

            var error = Should.Throw<InvalidDataException>(() =>
                DocumentStore.LoadConfigOptions(path, directory.Path)
            );

            error.Message.ShouldContain(path);
        }
    }

    [Test]
    public void CheckedInExampleConfigurationCanBeLoadedExplicitly()
    {
        var sample = Path.Combine(FindRepositoryRoot(), "console2svg.example.yaml");

        var options = DocumentStore.LoadConfigOptions(sample, Path.GetTempPath());

        options.Terminal!.Width.ShouldBe(160);
        options.Terminal.Height.ShouldBe(32);
        options.Capture!.Mode.ShouldBe("video");
    }

    [Test]
    public void NumericVideoCoalesceIsAcceptedAsMilliseconds()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "config.yaml");
        File.WriteAllText(
            path,
            """
            $version: 1
            options:
              capture:
                video:
                  coalesce: 7.5
            """
        );

        var options = DocumentStore.LoadConfigOptions(path, directory.Path);

        options.Capture!.Video!.Coalesce.ShouldBe("7.5");
    }

    [Test]
    public void ConfigDocumentsUseKebabCaseForYamlAndJsonPropertyNames()
    {
        using var directory = new TemporaryDirectory();
        var yamlPath = Path.Combine(directory.Path, "config.yaml");
        File.WriteAllText(
            yamlPath,
            """
            $version: 1
            options:
              live-server:
                host: ":9090"
              converter:
                svg-converter: resvg
              appearance:
                pc-padding: 12
                header:
                  with-command: true
            """
        );

        var yamlOptions = DocumentStore.LoadConfigOptions(yamlPath, directory.Path);

        yamlOptions.LiveServer!.Host.ShouldBe(":9090");
        yamlOptions.Converter!.SvgConverter.ShouldBe("resvg");
        yamlOptions.Appearance!.PcPadding.ShouldBe(12);
        yamlOptions.Appearance.Header!.WithCommand.ShouldBe(true);

        var jsonPath = Path.Combine(directory.Path, "config.json");
        File.WriteAllText(
            jsonPath,
            """
            {
              "$version": 1,
              "options": {
                "live-server": {
                  "host": ":9091"
                },
                "converter": {
                  "svg-converter": "rsvg"
                },
                "capture": {
                  "video": {
                    "coalesce": 7.5
                  }
                }
              }
            }
            """
        );

        var jsonOptions = DocumentStore.LoadConfigOptions(jsonPath, directory.Path);

        jsonOptions.LiveServer!.Host.ShouldBe(":9091");
        jsonOptions.Converter!.SvgConverter.ShouldBe("rsvg");
        jsonOptions.Capture!.Video!.Coalesce.ShouldBe("7.5");
    }

    [Test]
    public void ConfigApplicationAppliesDefaultsBeforeExplicitCommandLineValues()
    {
        var options = new AppOptions
        {
            Workflow = Workflow.Capture,
            Width = 90,
            IsWidthExplicit = true,
            VideoFps = 30,
            IsVideoFpsExplicit = true,
        };

        ResolvedSettings.Resolve(
            new ConsoleOptions
            {
                Terminal = new TerminalOptions { Width = 160, Height = 32 },
                Environment = new EnvironmentOptions { Color = "preserve", Ci = "strip" },
                Capture = new CaptureOptions
                {
                    Mode = "video",
                    Video = new VideoCaptureOptions { Fps = 12, Loop = false },
                },
                Appearance = new AppearanceOptions { Theme = [], Opacity = 0.75 },
            }
        ).ApplyTo(options);

        options.Width.ShouldBe(90);
        options.Height.ShouldBe(32);
        options.NoColorEnv.ShouldBeTrue();
        options.NoDeleteEnvs.ShouldBeFalse();
        options.Mode.ShouldBe(OutputMode.Video);
        options.VideoFps.ShouldBe(30);
        options.Loop.ShouldBeFalse();
        options.Themes.ShouldBe([]);
        options.IsThemesExplicit.ShouldBeTrue();
        options.Opacity.ShouldBe(0.75);

        var explicitEmptyTheme = new AppOptions
        {
            Workflow = Workflow.Capture,
            IsThemesExplicit = true,
        };
        ResolvedSettings.Resolve(
            new ConsoleOptions
            {
                Appearance = new AppearanceOptions { Theme = ["nord"] },
            }
        ).ApplyTo(explicitEmptyTheme);
        explicitEmptyTheme.Themes.ShouldBe([]);
    }

    [Test]
    public void ResolvedSettingsApplyLayersInOrderAndReturnDefensiveSnapshots()
    {
        var resolved = ResolvedSettings.Resolve(
            global: new ConsoleOptions
            {
                Terminal = new TerminalOptions { Width = 80, Height = 24 },
                Capture = new CaptureOptions
                {
                    Mode = "image",
                    Video = new VideoCaptureOptions { Fps = 12 },
                },
            },
            local: new ConsoleOptions { Terminal = new TerminalOptions { Height = 30 } },
            explicitConfiguration: new ConsoleOptions
            {
                Terminal = new TerminalOptions { Width = 100 },
            },
            scenario: new ConsoleOptions
            {
                Terminal = new TerminalOptions { Height = 40 },
                Capture = new CaptureOptions { Mode = "video" },
            },
            launch: new ConsoleOptions { Terminal = new TerminalOptions { Width = 120 } },
            capture: new ConsoleOptions
            {
                Capture = new CaptureOptions
                {
                    Video = new VideoCaptureOptions { Fps = 24 },
                },
            }
        );

        var snapshot = resolved.Options;
        snapshot.Terminal!.Width.ShouldBe(120);
        snapshot.Terminal.Height.ShouldBe(40);
        snapshot.Capture!.Mode.ShouldBe("video");
        snapshot.Capture.Video!.Fps.ShouldBe(24);

        snapshot.Terminal.Width = 1;
        resolved.Options.Terminal!.Width.ShouldBe(120);

        var invocation = new AppOptions
        {
            Workflow = Workflow.Capture,
            Width = 160,
            IsWidthExplicit = true,
            VideoFps = 30,
            IsVideoFpsExplicit = true,
        };
        resolved.ApplyTo(invocation);

        invocation.Width.ShouldBe(160);
        invocation.Height.ShouldBe(40);
        invocation.Mode.ShouldBe(OutputMode.Video);
        invocation.VideoFps.ShouldBe(30);
    }

    [Test]
    public void ConfigApplicationMergesImageSizePerDimensionAndPreservesExplicitSessionCrop()
    {
        var captureOptions = new AppOptions
        {
            Workflow = Workflow.Capture,
            SizeWidth = 720,
            IsSizeWidthExplicit = true,
        };

        ResolvedSettings.Resolve(
            new ConsoleOptions
            {
                Appearance = new AppearanceOptions
                {
                    Size = new ImageSizeOptions { Width = 800, Height = 400 },
                },
            }
        ).ApplyTo(captureOptions);

        captureOptions.SizeWidth.ShouldBe(720);
        captureOptions.SizeHeight.ShouldBe(400);

        var sessionOptions = new AppOptions
        {
            Workflow = Workflow.Session,
            RequestedSessionAction = SessionAction.Capture,
            CropTop = "7ch",
            IsCropTopExplicit = true,
        };
        ResolvedSettings.Resolve(
            new ConsoleOptions
            {
                Capture = new CaptureOptions
                {
                    Crop = new CropOptions { Top = "2ch", Right = "3ch" },
                },
            }
        ).ApplyTo(sessionOptions);

        sessionOptions.CropTop.ShouldBe("7ch");
        sessionOptions.CropRight.ShouldBe("3ch");
    }

    [Test]
    public void TmuxLiveServerReceivesSharedInteractiveAndEndpointOptions()
    {
        var options = new AppOptions
        {
            Workflow = Workflow.Tmux,
            RequestedTmuxAction = TmuxAction.LiveServer,
        };

        ResolvedSettings.Resolve(
            new ConsoleOptions
            {
                Interactive = new InteractiveOptions { Mouse = false },
                LiveServer = new LiveServerOptions { Host = ":9090" },
            }
        ).ApplyTo(options);

        options.Mouse.ShouldBeFalse();
        options.ListenAddress.ShouldBeNull();
        options.LiveServerPort.ShouldBe(9090);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"console2svg-test-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(
                Path.Combine(directory.FullName, "schema", "ConsoleToSvg.ConfigDocument.v1.json")
            )
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
