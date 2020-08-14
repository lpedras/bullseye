#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Bullseye.Internal
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;

    public partial class Logger
    {
        private class View
        {
            private readonly ConcurrentDictionary<string, TargetView> targets = new ConcurrentDictionary<string, TargetView>();

            private int targetCount;

            public TimeSpan? Duration { get; private set; }

            public IEnumerable<TargetView> Targets =>
                this.targets.Values.OrderBy(target => target.Ordinal);

            public string ToString(string prefix, Palette p)
            {
                var builder = new StringBuilder();

                this.AppendTargets(builder, prefix, p);



                return builder.ToString();
            }

            public TargetView Update(string target, TargetState state, TimeSpan? duration)
            {
                this.Duration = this.Duration.Add(duration);

                var targetView = this.targets.AddOrUpdate(
                    target,
                    new TargetView(target, Interlocked.Increment(ref this.targetCount), state, duration),
                    (_, view) =>
                    {
                        view.Update(state, duration);
                        return view;
                    });

                return targetView;
            }

            public (TargetView, InputView) Update(string target, State state, TimeSpan? duration, object input, Guid inputId)
            {
                this.Duration = this.Duration.Add(duration);

                TargetView targetView;
                InputView inputView;

                this.targets.AddOrUpdate(
                    target,
                    ((targetView, inputView) = TargetView.Create(target, Interlocked.Increment(ref this.targetCount), state, duration, input, inputId)).targetView,
                    (_, view) =>
                    {
                        targetView = view;
                        inputView = view.Update(state, duration, input, inputId);
                        return view;
                    });

                return (targetView, inputView);
            }

            private void AppendTargets(StringBuilder builder, string prefix, Palette p)
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

                        var inputState = inputView.State == Logger.State.Failed ? $"{p.Failed}Failed!{p.Reset}" : $"{p.Succeeded}Succeeded{p.Reset}";

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
                var timW = Math.Max(Palette.StripColours(rows[0].Duration).Length, durW + 2 + perW);

                // expand percentage column width to ensure time and percentage are as wide as duration
                perW = Math.Max(timW - durW - 2, perW);

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
            }

            private class SummaryRow
            {
                public string TargetOrInput { get; set; }

                public string State { get; set; }

                public string Duration { get; set; }

                public string Percentage { get; set; }
            }
        }

        private class TargetView
        {
            private readonly ConcurrentDictionary<Guid, InputView> inputs = new ConcurrentDictionary<Guid, InputView>();

            private int inputCount;

            public TargetView(string name, int ordinal, TargetState state, TimeSpan? duration)
            {
                this.Name = name;
                this.Ordinal = ordinal;
                this.State = state;
                this.Duration = duration;
            }

            public static (TargetView, InputView) Create(string name, int ordinal, State state, TimeSpan? duration, object input, Guid inputId)
            {
                var targetView = new TargetView(name, ordinal, (TargetState)state, duration);
                var inputView = targetView.inputs.GetOrAdd(inputId, new InputView(Interlocked.Increment(ref targetView.inputCount), state, duration, input));
                return (targetView, inputView);
            }

            public string Name { get; }

            public int Ordinal { get; }

            public TargetState State { get; private set; }

            public TimeSpan? Duration { get; private set; }

            public IEnumerable<InputView> Inputs =>
                this.inputs.Values.OrderBy(input => input.Ordinal);

            public void Update(TargetState state, TimeSpan? duration)
            {
                this.State = state;
                this.Duration = this.Duration.Add(duration);
            }

            public InputView Update(State state, TimeSpan? duration, object input, Guid inputId)
            {
                this.Duration = this.Duration.Add(duration);

                var inputView = this.inputs.AddOrUpdate(
                    inputId,
                    new InputView(Interlocked.Increment(ref this.inputCount), state, duration, input),
                    (_, view) =>
                    {
                        view.State = state;
                        view.Duration = duration;
                        view.Input = input;
                        return view;
                    });

                return inputView;
            }
        }

        private class InputView
        {
            public InputView(int ordinal, State state, TimeSpan? duration, object input)
            {
                this.Ordinal = ordinal;
                this.State = state;
                this.Duration = duration;
                this.Input = input;
            }

            public int Ordinal { get; }

            public State State { get; set; }

            public TimeSpan? Duration { get; set; }

            public object Input { get; set; }
        }

        private enum TargetState
        {
            NoInputs,
            Starting,
            Succeeded,
            Failed,
        }

        private enum State
        {
            Starting,
            Succeeded,
            Failed,
        }
    }
}
