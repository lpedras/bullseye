#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable IDE0009 // Member access should be qualified.
namespace Bullseye.Internal
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using static System.Math;

    public partial class Logger
    {
        private readonly View view = new View();
        private readonly TextWriter writer;
        private readonly string prefix;
        private readonly bool skipDependencies;
        private readonly bool dryRun;
        private readonly bool parallel;
        private readonly Palette p;
        private readonly bool verbose;

        public Logger(TextWriter writer, string prefix, bool skipDependencies, bool dryRun, bool parallel, Palette palette, bool verbose)
        {
            this.writer = writer;
            this.prefix = prefix;
            this.skipDependencies = skipDependencies;
            this.dryRun = dryRun;
            this.parallel = parallel;
            this.p = palette;
            this.verbose = verbose;
        }

        public async Task Version(Func<string> getVersion)
        {
            if (this.verbose)
            {
                await this.writer.WriteLineAsync(Message(p.Verbose, $"Bullseye version: {getVersion()}")).Tax();
            }
        }

        public Task Error(string message) => this.writer.WriteLineAsync(Message(p.Failed, message));

        public async Task Verbose(Func<string> getMessage)
        {
            if (this.verbose)
            {
                await this.writer.WriteLineAsync(Message(p.Verbose, getMessage())).Tax();
            }
        }

        public async Task Verbose(Stack<string> targets, string message)
        {
            if (this.verbose)
            {
                await this.writer.WriteLineAsync(Message(targets, p.Verbose, message)).Tax();
            }
        }

        public Task Starting(List<string> targets) =>
            this.writer.WriteLineAsync(Message(p.Default, $"Starting...", targets));

        public async Task Failed(List<string> targets)
        {
            await this.WriteView().Tax();
            await this.writer.WriteLineAsync(Message(p.Failed, $"Failed!", targets)).Tax();
        }

        public async Task Succeeded(List<string> targets)
        {
            await this.WriteView().Tax();
            await this.writer.WriteLineAsync(Message(p.Succeeded, $"Succeeded.", targets)).Tax();
        }

        public Task Starting(string target)
        {
            var targetView = view.Record(target, State.Starting, null);
            return this.writer.WriteLineAsync(Message(p.Default, "Starting...", targetView));
        }

        public Task Error(string target, Exception ex) =>
            this.writer.WriteLineAsync(Message(p.Failed, ex.ToString(), target));

        public Task Failed(string target, Exception ex, TimeSpan? duration)
        {
            var targetView = view.Record(target, State.Failed, duration);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed! {ex.Message}", targetView));
        }

        public Task Failed(string target)
        {
            var targetView = view.Record(target, State.Failed, null);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed!", targetView));
        }

        public Task Succeeded(string target, TimeSpan? duration = null)
        {
            var targetView = view.Record(target, State.Succeeded, duration);
            return this.writer.WriteLineAsync(Message(p.Succeeded, "Succeeded.", targetView));
        }

        public Task Starting<TInput>(string target, TInput input, Guid inputId)
        {
            var (targetView, inputView) = view.Record(target, State.Starting, null, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Default, "Starting...", targetView, inputView));
        }

        public Task Error<TInput>(string target, TInput input, Exception ex) =>
            this.writer.WriteLineAsync(Message(p.Failed, ex.ToString(), target, input));

        public Task Failed<TInput>(string target, TInput input, Exception ex, TimeSpan? duration, Guid inputId)
        {
            var (targetView, inputView) = view.Record(target, State.Failed, duration, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed! {ex.Message}", targetView, inputView));
        }

        public Task Succeeded<TInput>(string target, TInput input, TimeSpan? duration, Guid inputId)
        {
            var (targetView, inputView) = view.Record(target, State.Succeeded, duration, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Succeeded, "Succeeded.", targetView, inputView));
        }

        public Task NoInputs(string target)
        {
            var targetView = view.Record(target, State.NoInputs, null);
            return this.writer.WriteLineAsync(Message(p.Warning, "No inputs!", targetView));
        }

        private async Task WriteView()
        {
            // whitespace (e.g. can change to '·' for debugging)
            var ws = ' ';

            var rows = new List<SummaryRow> { new SummaryRow { TargetOrInput = $"{p.Default}Target{p.Reset}", State = $"{p.Default}Outcome{p.Reset}", Duration = $"{p.Default}Duration{p.Reset}", Percentage = "" } };

            foreach (var targetView in view.Targets)
            {
                var target = $"{p.Target}{targetView.Name}{p.Reset}";

                var state = targetView.State == State.Failed
                    ? $"{p.Failed}Failed!{p.Reset}"
                    : targetView.State == State.NoInputs
                        ? $"{p.Warning}No inputs!{p.Reset}"
                        : $"{p.Succeeded}Succeeded{p.Reset}";

                var duration = targetView.Duration.HasValue
                    ? $"{p.Timing}{targetView.Duration.Humanize()}{p.Reset}"
                    : "";

                var percentage = targetView.Duration.HasValue && view.Duration.HasValue && view.Duration.Value > TimeSpan.Zero
                    ? $"{p.Timing}{100 * targetView.Duration.Value.TotalMilliseconds / view.Duration.Value.TotalMilliseconds:N1}%{p.Reset}"
                    : "";

                rows.Add(new SummaryRow { TargetOrInput = target, State = state, Duration = duration, Percentage = percentage });

                var index = 0;

                var inputs = targetView.Inputs.ToList();
                foreach (var inputView in inputs)
                {
                    var input = $"{ws}{ws}{p.Input}{inputView.Input}{p.Reset}";

                    var inputState = inputView.State == State.Failed ? $"{p.Failed}Failed!{p.Reset}" : $"{p.Succeeded}Succeeded{p.Reset}";

                    var inputDuration = inputView.Duration.HasValue
                        ? $"{(index < inputs.Count - 1 ? p.TreeFork : p.TreeCorner)}{p.Timing}{inputView.Duration.Humanize()}{p.Reset}"
                        : "";

                    var inputPercentage = inputView.Duration.HasValue && view.Duration.HasValue && view.Duration.Value > TimeSpan.Zero
                        ? $"{(index < inputs.Count - 1 ? p.TreeFork : p.TreeCorner)}{p.Timing}{100 * inputView.Duration.Value.TotalMilliseconds / view.Duration.Value.TotalMilliseconds:N1}%{p.Reset}"
                        : "";

                    rows.Add(new SummaryRow { TargetOrInput = input, State = inputState, Duration = inputDuration, Percentage = inputPercentage });

                    ++index;
                }
            }

            // target or input column width
            var tarW = rows.Max(row => Palette.StripColours(row.TargetOrInput).Length);

            // outcome column width
            var outW = rows.Max(row => Palette.StripColours(row.State).Length);

            // duration column width
            var durW = rows.Count > 1 ? rows.Skip(1).Max(row => Palette.StripColours(row.Duration).Length) : 0;

            // percentage column width
            var perW = rows.Max(row => Palette.StripColours(row.Percentage).Length);

            // timing column width (duration and percentage)
            var timW = Max(Palette.StripColours(rows[0].Duration).Length, durW + 2 + perW);

            // expand percentage column width to ensure time and percentage are as wide as duration
            perW = Max(timW - durW - 2, perW);

            // summary start separator
            await this.writer.WriteLineAsync($"{GetPrefix()}{p.Default}{"".Prp(tarW + 2 + outW + 2 + timW, p.Dash)}{p.Reset}").Tax();

            // header
            await this.writer.WriteLineAsync($"{GetPrefix()}{rows[0].TargetOrInput.Prp(tarW, ws)}{ws}{ws}{rows[0].State.Prp(outW, ws)}{ws}{ws}{rows[0].Duration.Prp(timW, ws)}").Tax();

            // header separator
            await this.writer.WriteLineAsync($"{GetPrefix()}{p.Default}{"".Prp(tarW, p.Dash)}{p.Reset}{ws}{ws}{p.Default}{"".Prp(outW, p.Dash)}{p.Reset}{ws}{ws}{p.Default}{"".Prp(timW, p.Dash)}{p.Reset}").Tax();

            // targets
            foreach (var row in rows.Skip(1))
            {
                await this.writer.WriteLineAsync($"{GetPrefix()}{row.TargetOrInput.Prp(tarW, ws)}{p.Reset}{ws}{ws}{row.State.Prp(outW, ws)}{p.Reset}{ws}{ws}{row.Duration.Prp(durW, ws)}{p.Reset}{ws}{ws}{row.Percentage.Prp(perW, ws)}{p.Reset}").Tax();
            }

            // summary end separator
            await this.writer.WriteLineAsync($"{GetPrefix()}{p.Default}{"".Prp(tarW + 2 + outW + 2 + timW, p.Dash)}{p.Reset}").Tax();
        }

        private string Message(string color, string text) => $"{GetPrefix()}{color}{text}{p.Reset}";

        private string Message(Stack<string> targets, string color, string text) => $"{GetPrefix(targets)}{color}{text}{p.Reset}";

        private string Message(string color, string text, List<string> targets) =>
            $"{GetPrefix()}{color}{text}{p.Reset} {p.Target}({targets.Spaced()}){p.Reset}{GetSuffix(false, view.Duration)}{p.Reset}";

        private string Message(string color, string text, string target) =>
            $"{GetPrefix(target)}{color}{text}{p.Reset}";

        private string Message(string color, string text, View.TargetView target) =>
            $"{GetPrefix(target.Name)}{color}{text}{p.Reset}{GetSuffix(true, target.Duration)}{p.Reset}";

        private string Message<TInput>(string color, string text, string target, TInput input) =>
            $"{GetPrefix(target, input)}{color}{text}{p.Reset}";

        private string Message(string color, string text, View.TargetView target, View.TargetView.InputView input) =>
            $"{GetPrefix(target.Name, input.Input)}{color}{text}{p.Reset}{GetSuffix(true, input.Duration)}{p.Reset}";

        private string GetPrefix() =>
            $"{p.Prefix}{prefix}:{p.Reset} ";

        private string GetPrefix(Stack<string> targets) =>
            $"{p.Prefix}{prefix}:{p.Reset} {p.Target}{string.Join($"{p.Default}/{p.Target}", targets.Reverse())}{p.Default}:{p.Reset} ";

        private string GetPrefix(string target) =>
            $"{p.Prefix}{prefix}:{p.Reset} {p.Target}{target}{p.Default}:{p.Reset} ";

        private string GetPrefix<TInput>(string target, TInput input) =>
            $"{p.Prefix}{prefix}:{p.Reset} {p.Target}{target}{p.Default}/{p.Input}{input}{p.Default}:{p.Reset} ";

        private string GetSuffix(bool specific, TimeSpan? duration) =>
            (!specific && this.dryRun ? $" {p.Option}(dry run){p.Reset}" : "") +
                (!specific && this.parallel ? $" {p.Option}(parallel){p.Reset}" : "") +
                (!specific && this.skipDependencies ? $" {p.Option}(skip dependencies){p.Reset}" : "") +
                (duration.HasValue ? $" {p.Timing}({duration.Humanize()}){p.Reset}" : "");

        private class SummaryRow
        {
            public string TargetOrInput { get; set; }

            public string State { get; set; }

            public string Duration { get; set; }

            public string Percentage { get; set; }
        }
    }
}
