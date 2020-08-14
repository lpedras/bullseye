#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable IDE0009 // Member access should be qualified.
namespace Bullseye.Internal
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

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
            await this.writer.WriteAsync(view.ToString(GetPrefix(), p)).Tax();
            await this.writer.WriteLineAsync(Message(p.Failed, $"Failed!", targets)).Tax();
        }

        public async Task Succeeded(List<string> targets)
        {
            await this.writer.WriteAsync(view.ToString(GetPrefix(), p)).Tax();
            await this.writer.WriteLineAsync(Message(p.Succeeded, $"Succeeded", targets)).Tax();
        }

        public Task Starting(string target)
        {
            var targetView = view.Update(target, TargetState.Starting, null);
            return this.writer.WriteLineAsync(Message(p.Default, "Starting...", targetView));
        }

        public Task Error(string target, Exception ex) =>
            this.writer.WriteLineAsync(Message(p.Failed, ex.ToString(), target));

        public Task Failed(string target, Exception ex, TimeSpan? duration)
        {
            var targetView = view.Update(target, TargetState.Failed, duration);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed! {ex.Message}", targetView));
        }

        public Task Failed(string target)
        {
            var targetView = view.Update(target, TargetState.Failed, null);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed!", targetView));
        }

        public Task Succeeded(string target, TimeSpan? duration = null)
        {
            var targetView = view.Update(target, TargetState.Succeeded, duration);
            return this.writer.WriteLineAsync(Message(p.Succeeded, "Succeeded", targetView));
        }

        public Task Starting<TInput>(string target, TInput input, Guid inputId)
        {
            var (targetView, inputView) = view.Update(target, State.Starting, null, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Default, "Starting...", targetView, inputView));
        }

        public Task Error<TInput>(string target, TInput input, Exception ex) =>
            this.writer.WriteLineAsync(Message(p.Failed, ex.ToString(), target, input));

        public Task Failed<TInput>(string target, TInput input, Exception ex, TimeSpan? duration, Guid inputId)
        {
            var (targetView, inputView) = view.Update(target, State.Failed, duration, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Failed, $"Failed! {ex.Message}", targetView, inputView));
        }

        public Task Succeeded<TInput>(string target, TInput input, TimeSpan? duration, Guid inputId)
        {
            var (targetView, inputView) = view.Update(target, State.Succeeded, duration, input, inputId);
            return this.writer.WriteLineAsync(Message(p.Succeeded, "Succeeded", targetView, inputView));
        }

        public Task NoInputs(string target)
        {
            var targetView = view.Update(target, TargetState.NoInputs, null);
            return this.writer.WriteLineAsync(Message(p.Warning, "No inputs!", targetView));
        }

        private string Message(string color, string text) => $"{GetPrefix()}{color}{text}{p.Reset}";

        private string Message(Stack<string> targets, string color, string text) => $"{GetPrefix(targets)}{color}{text}{p.Reset}";

        private string Message(string color, string text, List<string> targets) =>
            $"{GetPrefix()}{color}{text}{p.Reset} {p.Target}({targets.Spaced()}){p.Reset}{GetSuffix(false, view.Duration)}{p.Reset}";

        private string Message(string color, string text, string target) =>
            $"{GetPrefix(target)}{color}{text}{p.Reset}";

        private string Message(string color, string text, TargetView target) =>
            $"{GetPrefix(target.Name)}{color}{text}{p.Reset}{GetSuffix(true, target.Duration)}{p.Reset}";

        private string Message<TInput>(string color, string text, string target, TInput input) =>
            $"{GetPrefix(target, input)}{color}{text}{p.Reset}";

        private string Message(string color, string text, TargetView target, InputView input) =>
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
    }
}
