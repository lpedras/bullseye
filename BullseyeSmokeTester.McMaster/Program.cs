using System.Linq;
using Bullseye;
using McMaster.Extensions.CommandLineUtils;
using static Bullseye.Targets;

using var app = new CommandLineApplication() { UsePagerForHelpText = false };
app.HelpOption();
var foo = app.Option<string>("-f|--foo <foo>", "A value used for something.", CommandOptionType.SingleValue);

// translate from Bullseye to McMaster.Extensions.CommandLineUtils
app.Argument("targets", "A list of targets to run or list. If not specified, the \"default\" target will be run, or all targets will be listed.", true);
foreach (var option in Options.Definitions)
{
    _ = app.Option((option.ShortName != null ? $"{option.ShortName}|" : "") + option.LongName, option.Description, CommandOptionType.NoValue);
}

app.OnExecuteAsync(async _ =>
{
    // translate from McMaster.Extensions.CommandLineUtils to Bullseye
    var targets = app.Arguments[0].Values;
    var options = new Options(Options.Definitions.Select(d => (d.LongName, app.Options.Single(o => "--" + o.LongName == d.LongName).HasValue())));

    Target("build", async () => await System.Console.Out.WriteLineAsync($"foo = {foo.Value()}"));

    Target("default", DependsOn("build"));

    await RunTargetsAndExitAsync(targets, options);
});

return await app.ExecuteAsync(args);
