#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Bullseye.Internal
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using static System.Math;

    public partial class Logger
    {
        private class View
        {
            private readonly ConcurrentDictionary<string, TargetView> targets = new ConcurrentDictionary<string, TargetView>();

            private int targetCount;

            public TargetState State { get; private set; }

            public TimeSpan? Duration { get; private set; }

            public IEnumerable<TargetView> Targets =>
                this.targets.Values.OrderBy(target => target.Ordinal);

            public string ToString(string prefix, Palette p)
            {
                // whitespace (e.g. can change to '·' for debugging)
                var ws = ' ';

                var rows = new List<SummaryRow> { new SummaryRow { TargetOrInput = $"{p.Default}Target{p.Reset}", State = $"{p.Default}Outcome{p.Reset}", Duration = $"{p.Default}Duration{p.Reset}", Percentage = "" } };

                foreach (var targetView in this.Targets)
                {
                    var target = $"{p.Target}{targetView.Name}{p.Reset}";

                    var state = targetView.State == TargetState.Failed
                        ? $"{p.Failed}Failed!{p.Reset}"
                        : targetView.State == TargetState.NoInputs
                            ? $"{p.Warning}No inputs!{p.Reset}"
                            : $"{p.Succeeded}Succeeded{p.Reset}";

                    var duration = targetView.Duration.HasValue
                        ? $"{p.Timing}{targetView.Duration.Humanize(true)}{p.Reset}"
                        : "";

                    var percentage = targetView.Duration.HasValue && this.Duration.HasValue && this.Duration.Value > TimeSpan.Zero
                        ? $"{p.Timing}{100 * targetView.Duration.Value.TotalMilliseconds / this.Duration.Value.TotalMilliseconds:N1}%{p.Reset}"
                        : "";

                    rows.Add(new SummaryRow { TargetOrInput = target, State = state, Duration = duration, Percentage = percentage });

                    var index = 0;

                    var inputs = targetView.Inputs.ToList();
                    foreach (var inputView in inputs)
                    {
                        var input = $"{ws}{ws}{p.Input}{inputView.Input}{p.Reset}";

                        var inputState = inputView.State == InputState.Failed ? $"{p.Failed}Failed!{p.Reset}" : $"{p.Succeeded}Succeeded{p.Reset}";

                        var inputDuration = inputView.Duration.HasValue
                            ? $"{(index < inputs.Count - 1 ? p.TreeFork : p.TreeCorner)}{p.Timing}{inputView.Duration.Humanize(true)}{p.Reset}"
                            : "";

                        var inputPercentage = inputView.Duration.HasValue && this.Duration.HasValue && this.Duration.Value > TimeSpan.Zero
                            ? $"{(index < inputs.Count - 1 ? p.TreeFork : p.TreeCorner)}{p.Timing}{100 * inputView.Duration.Value.TotalMilliseconds / this.Duration.Value.TotalMilliseconds:N1}%{p.Reset}"
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

                var builder = new StringBuilder();

                // summary start separator
                builder.AppendLine($"{prefix}{p.Default}{"".Prp(tarW + 2 + outW + 2 + timW, p.Dash)}{p.Reset}");

                // header
                builder.AppendLine($"{prefix}{rows[0].TargetOrInput.Prp(tarW, ws)}{ws}{ws}{rows[0].State.Prp(outW, ws)}{ws}{ws}{rows[0].Duration.Prp(timW, ws)}");

                // header separator
                builder.AppendLine($"{prefix}{p.Default}{"".Prp(tarW, p.Dash)}{p.Reset}{ws}{ws}{p.Default}{"".Prp(outW, p.Dash)}{p.Reset}{ws}{ws}{p.Default}{"".Prp(timW, p.Dash)}{p.Reset}");

                // targets
                foreach (var row in rows.Skip(1))
                {
                    builder.AppendLine($"{prefix}{row.TargetOrInput.Prp(tarW, ws)}{p.Reset}{ws}{ws}{row.State.Prp(outW, ws)}{p.Reset}{ws}{ws}{row.Duration.Prp(durW, ws)}{p.Reset}{ws}{ws}{row.Percentage.Prp(perW, ws)}{p.Reset}");
                }

                // summary end separator
                builder.AppendLine($"{prefix}{p.Default}{"".Prp(tarW + 2 + outW + 2 + timW, p.Dash)}{p.Reset}");

                return builder.ToString();
            }

            public TargetView Record(string target, TargetState state, TimeSpan? duration) =>
                this.Record(state, duration).Record(target).Record(state, duration);

            public (TargetView, InputView) Record(string target, InputState state, TimeSpan? duration, object input, Guid inputId) =>
                this.Record((TargetState)state, duration).Record(target).Record(state, duration, input, inputId);

            private View Record(TargetState state, TimeSpan? duration)
            {
                this.State = Coalesce(this.State, state);
                this.Duration = this.Duration.Add(duration);
                return this;
            }

            private TargetView Record(string target) =>
                this.targets.GetOrAdd(target, _ => new TargetView(target, Interlocked.Increment(ref this.targetCount)));

            private class SummaryRow
            {
                public string TargetOrInput { get; set; }

                public string State { get; set; }

                public string Duration { get; set; }

                public string Percentage { get; set; }
            }
        }

        private static TargetState Coalesce(TargetState x, TargetState y) =>
            (TargetState)Max((int)x, (int)y);

        private static InputState Coalesce(InputState x, InputState y) =>
            (InputState)Max((int)x, (int)y);

        private class TargetView
        {
            private readonly ConcurrentDictionary<Guid, InputView> inputs = new ConcurrentDictionary<Guid, InputView>();

            private int inputCount;

            public TargetView(string name, int ordinal)
            {
                this.Name = name;
                this.Ordinal = ordinal;
            }

            public string Name { get; }

            public int Ordinal { get; }

            public TargetState State { get; private set; }

            public TimeSpan? Duration { get; private set; }

            public IEnumerable<InputView> Inputs =>
                this.inputs.Values.OrderBy(input => input.Ordinal);

            public TargetView Record(TargetState state, TimeSpan? duration)
            {
                this.State = Coalesce(this.State, state);
                this.Duration = this.Duration.Add(duration);
                return this;
            }

            public (TargetView, InputView) Record(InputState state, TimeSpan? duration, object input, Guid inputId) =>
                (this, this.Record((TargetState)state, duration).Record(inputId).Record(state, duration, input));

            private InputView Record(Guid inputId) =>
                this.inputs.GetOrAdd(inputId, _ => new InputView(Interlocked.Increment(ref this.inputCount)));
        }

        private class InputView
        {
            public InputView(int ordinal) =>
                this.Ordinal = ordinal;

            public int Ordinal { get; }

            public InputState State { get; private set; }

            public TimeSpan? Duration { get; private set; }

            public object Input { get; private set; }

            public InputView Record(InputState state, TimeSpan? duration, object input)
            {
                this.State = Coalesce(this.State, state);
                this.Duration = this.Duration.Add(duration);
                this.Input = input;
                return this;
            }
        }

        private enum TargetState
        {
            Starting = 0,
            NoInputs = 1,
            Succeeded = 2,
            Failed = 3,
        }

        private enum InputState
        {
            Starting = 0,
            Succeeded = 2,
            Failed = 3,
        }
    }
}
