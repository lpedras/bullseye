#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Bullseye.Internal
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using static System.Math;

    public partial class Logger
    {
        private class View
        {
            private readonly ConcurrentDictionary<string, TargetView> targets = new ConcurrentDictionary<string, TargetView>();

            private int targetOrdinal;

            public State State { get; private set; }

            public TimeSpan? Duration { get; private set; }

            public IEnumerable<TargetView> Targets =>
                this.targets.Values.OrderBy(target => target.Ordinal);

            public TargetView Record(string target, State state, TimeSpan? duration) =>
                this.Record(state, duration).Record(target).Record(state, duration);

            public (TargetView, TargetView.InputView) Record(string target, State state, TimeSpan? duration, object input, Guid inputId) =>
                this.Record(state, duration).Record(target).Record(state, duration, input, inputId);

            private View Record(State state, TimeSpan? duration)
            {
                this.State = Coalesce(this.State, state);
                this.Duration = this.Duration.Add(duration);
                return this;
            }

            private TargetView Record(string target) =>
                this.targets.GetOrAdd(target, _ => new TargetView(target, Interlocked.Increment(ref this.targetOrdinal)));

            private static State Coalesce(State x, State y) =>
                (State)Max((int)x, (int)y);

            public class TargetView
            {
                private readonly ConcurrentDictionary<Guid, InputView> inputs = new ConcurrentDictionary<Guid, InputView>();

                private int inputOrdinal;

                public TargetView(string name, int ordinal)
                {
                    this.Name = name;
                    this.Ordinal = ordinal;
                }

                public string Name { get; }

                public int Ordinal { get; }

                public State State { get; private set; }

                public TimeSpan? Duration { get; private set; }

                public IEnumerable<InputView> Inputs =>
                    this.inputs.Values.OrderBy(input => input.Ordinal);

                public TargetView Record(State state, TimeSpan? duration)
                {
                    this.State = Coalesce(this.State, state);
                    this.Duration = this.Duration.Add(duration);
                    return this;
                }

                internal (TargetView, InputView) Record(State state, TimeSpan? duration, object input, Guid inputId) =>
                    (this, this.Record(state, duration).Record(inputId).Record(state, duration, input));

                private InputView Record(Guid inputId) =>
                    this.inputs.GetOrAdd(inputId, _ => new InputView(Interlocked.Increment(ref this.inputOrdinal)));

                public class InputView
                {
                    public InputView(int ordinal) =>
                        this.Ordinal = ordinal;

                    public int Ordinal { get; }

                    public State State { get; private set; }

                    public TimeSpan? Duration { get; private set; }

                    public object Input { get; private set; }

                    public InputView Record(State state, TimeSpan? duration, object input)
                    {
                        this.State = Coalesce(this.State, state);
                        this.Duration = this.Duration.Add(duration);
                        this.Input = input;
                        return this;
                    }
                }
            }
        }

        private enum State
        {
            Starting,
            NoInputs,
            Succeeded,
            Failed,
        }
    }
}
