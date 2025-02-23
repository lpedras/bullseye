using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bullseye.Internal
{
    public class Target
    {
        public Target(string name, string description, IEnumerable<string> dependencies)
        {
            this.Name = name;
            this.Description = description;
            this.Dependencies = dependencies.ToList();
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyCollection<string> Dependencies { get; }

        public virtual Task RunAsync(bool dryRun, bool parallel, Output output, Func<Exception, bool> messageOnly, IReadOnlyCollection<Target> dependencyPath) => output.Succeeded(this, dependencyPath, TimeSpan.Zero);

        public override string ToString() => this.Name;
    }
}
