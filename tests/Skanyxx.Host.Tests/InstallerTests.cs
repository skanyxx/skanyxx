using System.Text.RegularExpressions;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The desktop installers and the release workflow (D231). They only run in CI and on people's machines, so the rules
/// that broke them once are pinned here as text: the launchers start the Host in its install folder, a fresh config holds
/// no secret and is readable, and no workflow input reaches a shell script.
/// </summary>
public sealed class InstallerTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Repo.Path(parts)).ReplaceLineEndings("\n");

    /// <summary>The Host reads appsettings.json and modules/ from its working directory: run anywhere else it crashed at
    /// the AllowedHosts guard, and a folder with a modules/ DLL in it would have run that DLL.</summary>
    [Fact]
    public void EveryLauncher_StartsTheHostInItsInstallFolder()
    {
        var mac = Read("installer", "mac", "scripts", "postinstall");
        Assert.Contains("cd \"$INSTALL_DIR\" || exit 1\n[ \"\\$#\" -gt 0 ] || set -- open\nexec ./Skanyxx.Host \"\\$@\"\n", mac);
        Assert.DoesNotContain("ln -s", mac);
        // Unquoted, so $INSTALL_DIR and $PORT are written into the launcher and the plist, not left as text.
        Assert.Contains("<<LAUNCHER\n", mac);
        Assert.Contains("<<PLIST\n", mac);
        Assert.Contains("<key>WorkingDirectory</key>  <string>$INSTALL_DIR</string>", mac);

        Assert.Contains("cd /usr/share/skanyxx || exit 1\n[ \"$#\" -gt 0 ] || set -- open\nexec ./Skanyxx.Host \"$@\"\n",
            Read("installer", "linux", "usr", "bin", "skanyxx"));
        var open = Read("installer", "linux", "usr", "share", "skanyxx", "open-in-browser");
        Assert.Contains("cd /usr/share/skanyxx || exit 1", open);
        Assert.Contains("url=$(./Skanyxx.Host url 2>\"$err\")", open);
        Assert.Contains("exec xdg-open \"$url\"", open);
        var desktop = Read("installer", "linux", "usr", "share", "applications", "skanyxx.desktop");
        Assert.Contains("Exec=/usr/share/skanyxx/open-in-browser\n", desktop);
        Assert.Contains("Path=/usr/share/skanyxx\n", desktop);
        Assert.Contains("WorkingDirectory=/usr/share/skanyxx\n", Read("installer", "linux", "usr", "lib", "systemd", "system", "skanyxx.service"));

        var release = Read(".github", "workflows", "release.yml");
        Assert.Contains("install -D -m 755 installer/linux/usr/bin/skanyxx \"$PKG_DIR/usr/bin/skanyxx\"", release);
        Assert.Contains("installer/linux/usr/share/skanyxx/open-in-browser \"$APP/\"", release);
        Assert.DoesNotContain("/usr/local/bin", release);

        var iss = Read("installer", "windows", "skanyxx.iss");
        // The Start Menu and desktop shortcuts and the post-install launch.
        Assert.Equal(3, Regex.Count(iss, "Filename: \"\\{app\\}\\\\\\{#AppExeName\\}\";.*WorkingDir: \"\\{app\\}\""));
    }

    /// <summary>Port 5282 is shared by every account on a machine: one that binds it first could serve a lookalike sign-in.
    /// The launchers open RemoteUrl through <c>Skanyxx.Host open</c> instead and listen nowhere (D234).</summary>
    [Fact]
    public void TheLaunchers_OpenTheRemoteUrlDirectly_NotAPortAnotherAccountCanTake()
    {
        var open = Read("installer", "linux", "usr", "share", "skanyxx", "open-in-browser");
        Assert.DoesNotContain("5282", open);
        Assert.DoesNotContain("ss ", open);
        Assert.Contains("set -- open", Read("installer", "linux", "usr", "bin", "skanyxx"));

        var iss = Read("installer", "windows", "skanyxx.iss");
        Assert.Equal(3, Regex.Count(iss, "Parameters: \"open\""));
        // localhost stays only in the RemoteUrl rule (http:// is allowed to a loopback host): no shortcut serves a port.
        Assert.DoesNotContain("--urls", iss);
        Assert.DoesNotContain("AppPort", iss);
        Assert.DoesNotContain("localhost:5282", iss);
        Assert.DoesNotContain("localhost:{#", iss);
        Assert.False(File.Exists(Repo.Path("installer", "windows", "open-browser.cmd")));
        Assert.DoesNotContain("Source: \"open-browser.cmd\"", iss);
        Assert.Contains("Type: files; Name: \"{app}\\open-browser.cmd\"", iss);

        Assert.DoesNotContain("iproute2", Read("installer", "linux", "DEBIAN", "control"));

        var files = Directory.EnumerateFiles(Repo.Path("installer"), "*", SearchOption.AllDirectories)
            .Append(Repo.Path(".github", "workflows", "release.yml"));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(text, @"--urls\s+http://localhost"), $"{file} serves localhost");
            Assert.DoesNotContain("<string>http://localhost", text);
        }
    }

    /// <summary>A fresh install is Remote mode, which never uses a bootstrap token: none is generated, so the config holds
    /// no secret and every account that starts the launcher can read it.</summary>
    [Fact]
    public void FreshInstalls_WriteNoSecret_AndAConfigEveryAccountCanRead()
    {
        foreach (var script in new[] { Read("installer", "mac", "scripts", "postinstall"), Read("installer", "linux", "DEBIAN", "postinst") })
        {
            Assert.DoesNotContain("/dev/urandom", script);
            Assert.DoesNotContain("BOOTSTRAP_TOKEN", script);
            Assert.Contains("chmod 644 \"$CONFIG_TMP\"", script);
            // A planted symlink at the config path is never followed by these root scripts.
            Assert.Contains("[[ ! -e \"$CONFIG\" && ! -L \"$CONFIG\" ]]", script);
        }
    }

    /// <summary>"**/bin/" ignores build output, and would silently leave the package's /usr/bin/skanyxx out of a commit:
    /// the tagged checkout would then fail these tests and the deb build.</summary>
    [Fact]
    public void TheLinuxLauncher_IsNotGitIgnored()
    {
        var gitignore = Read(".gitignore");
        Assert.True(gitignore.IndexOf("!/installer/linux/usr/bin/\n", StringComparison.Ordinal)
            > gitignore.IndexOf("**/bin/\n", StringComparison.Ordinal));
    }

    [Fact]
    public void LinuxUpgrades_KeepTheServiceRunning_AndPurgeRemovesWhatTheInstallCreated()
    {
        Assert.Contains("if [[ \"$1\" == \"remove\" ]]", Read("installer", "linux", "DEBIAN", "prerm"));
        Assert.Contains("systemctl try-restart skanyxx", Read("installer", "linux", "DEBIAN", "postinst"));
        Assert.Contains("if [[ \"$1\" == \"purge\" ]]", Read("installer", "linux", "DEBIAN", "postrm"));
        Assert.Contains("libicu72", Read("installer", "linux", "DEBIAN", "control"));
        // .NET resolves the user profile through $HOME only; systemd sets it only with User=.
        Assert.Contains("Environment=HOME=/root\n", Read("installer", "linux", "usr", "lib", "systemd", "system", "skanyxx.service"));
    }

    /// <summary>Root writes into /usr/local on macOS: only where every folder on the way is root's alone.</summary>
    [Fact]
    public void TheMacInstaller_WritesOnlyIntoFoldersRootAloneOwns()
    {
        var postinstall = Read("installer", "mac", "scripts", "postinstall");
        Assert.Contains("for dir in /usr/local /usr/local/share \"$INSTALL_DIR\"; do", postinstall);
        Assert.Contains("if root_only \"$BIN_DIR\"; then", postinstall);
        Assert.DoesNotContain("mv -f \"", postinstall);
        // Before the payload too, the install folder included: a symlink there is refused, not followed.
        Assert.Contains("for dir in /usr/local /usr/local/share \"$INSTALL_DIR\"; do\n    if [[ -e \"$dir\" || -L \"$dir\" ]] && ! root_only \"$dir\"; then",
            Read("installer", "mac", "scripts", "preinstall"));
    }

    [Fact]
    public void Upgrades_StartModulesEmpty_SoARemovedModuleNeverLoads()
    {
        var preinstall = Read("installer", "mac", "scripts", "preinstall");
        Assert.Contains("INSTALL_DIR=/usr/local/share/skanyxx\n", preinstall);
        // Only inside a real folder that root alone owns: never through a symlink another account planted.
        Assert.Contains("if root_only \"$INSTALL_DIR\"; then\n    rm -rf \"$INSTALL_DIR/modules\"", preinstall);
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\modules\"", Read("installer", "windows", "skanyxx.iss"));
    }

    /// <summary>A tag or a dispatch title may hold <c>$( )</c>: workflow expressions that carry them are only ever env or
    /// action inputs, never text inside a <c>run:</c> script.</summary>
    [Fact]
    public void TheReleaseWorkflow_NeverPutsAnInputIntoAScript()
    {
        var lines = Read(".github", "workflows", "release.yml").Split('\n');
        var untrusted = new Regex(@"\$\{\{\s*(inputs\.|secrets\.|github\.ref|github\.head_ref|github\.event\.|needs\.|steps\.[^}]*\.outputs\.)");
        var runIndent = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart().Length;
            if (runIndent >= 0 && line.Trim().Length > 0 && indent <= runIndent)
                runIndent = -1;
            if (runIndent >= 0)
                Assert.False(untrusted.IsMatch(line), $"release.yml:{i + 1} puts an input into a script: {line.Trim()}");
            var run = Regex.Match(line, @"^(\s*)(- )?run:\s*[|>]");
            if (run.Success)
                runIndent = run.Groups[1].Length + run.Groups[2].Length;
            else if (Regex.IsMatch(line, @"^\s*(- )?run:\s*\S"))
                Assert.False(untrusted.IsMatch(line), $"release.yml:{i + 1} puts an input into a script: {line.Trim()}");
        }
    }

    [Fact]
    public void TheReleaseWorkflow_WritesOnlyInTheReleaseJob_WithPinnedActions_AndNoStoredCredentials()
    {
        var release = Read(".github", "workflows", "release.yml");
        var jobs = release.IndexOf("\njobs:", StringComparison.Ordinal);
        Assert.Contains("\npermissions:\n  contents: read\n", release[..jobs]);
        Assert.Equal(1, Regex.Count(release, "contents: write"));
        Assert.True(release.IndexOf("contents: write", StringComparison.Ordinal) > release.IndexOf("\n  release:", StringComparison.Ordinal));

        foreach (Match uses in Regex.Matches(release, @"uses:\s*(\S+)"))
            Assert.Matches(@"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", uses.Groups[1].Value);

        Assert.Equal(Regex.Count(release, "uses: actions/checkout@"), Regex.Count(release, "persist-credentials: false"));
        // Only a run from the tag itself publishes, never a branch; an existing release is never added to.
        Assert.Contains("if [ \"$REF\" != \"refs/tags/$TAG\" ]; then", release);
        Assert.Contains("gh release create \"$TAG\" --verify-tag", release);

        // Signing secrets live only in jobs that run no package code: no job with a secret restores, builds or publishes.
        foreach (var job in Regex.Split(release[jobs..], @"\n(?=  [a-z][a-z-]*:\n)"))
            if (job.Contains("${{ secrets."))
            {
                Assert.DoesNotContain("dotnet ", job);
                Assert.Contains("environment: signing", job);
            }
    }

    /// <summary>A pfx with its chain imports several certificates; and a stray artifact or file must never be published.</summary>
    [Fact]
    public void TheReleaseWorkflow_SignsWithTheOneKeyedCertificate_AndPublishesExactlyTheFourInstallers()
    {
        var release = Read(".github", "workflows", "release.yml");
        Assert.Contains("$keyed = @($imported | Where-Object HasPrivateKey)", release);
        Assert.Contains("if ($keyed.Count -ne 1)", release);
        Assert.Contains("foreach ($c in $imported)", release);
        Assert.DoesNotContain("$cert.Thumbprint", release);

        Assert.DoesNotContain("merge-multiple", release);
        Assert.DoesNotContain("pattern:", release);
        foreach (var name in new[] { "windows-installer", "macos-arm64-installer", "macos-x86_64-installer", "linux-deb-installer" })
            Assert.Contains($"          name: {name}\n          path: in/{name}\n", release);
        Assert.Contains("must hold exactly", release);
    }
}
