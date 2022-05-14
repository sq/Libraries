using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.CoreCLR;

namespace Squared.Util.Testing {
    public class FaultInjector {
        public List<Func<string, Exception>> ExceptionTypes = new List<Func<string, Exception>>();

        /// <summary>
        /// If false, Step will early-out without adjusting the countdown or injecting faults
        /// </summary>
        public bool Enabled;
        /// <summary>
        /// The percentage chance to inject a fault when the countdown reaches 0, 0 - 100
        /// </summary>
        public double PercentageChance = 0;
        /// <summary>
        /// Each time the countdown reaches 0, a new countdown is randomly selected between MinCountdown and MaxCountdown
        /// </summary>
        public int MinCountdown = 8;
        /// <summary>
        /// Each time the countdown reaches 0, a new countdown is randomly selected between MinCountdown and MaxCountdown
        /// </summary>
        public int MaxCountdown = 12;
        private int Countdown;
        private Xoshiro RNG;
        
        public FaultInjector () {
            ExceptionTypes.Add((msg) => new FaultInjectorException(this, msg));
            RNG = new Xoshiro(null);
            Countdown = RNG.Next(MinCountdown, MaxCountdown);
        }

        /// <summary>
        /// Decreases the fault injection countdown, and if it reaches 0,
        ///  randomly decides whether to inject a fault in this step.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Step () {
            if (!Enabled || (Countdown-- > 0))
                return;

            Countdown = RNG.Next(MinCountdown, MaxCountdown);
            if (RNG.NextDouble() * 100 < PercentageChance)
                InjectFault();
        }

        /// <summary>
        /// Decreases the fault injection countdown, and if it reaches 0,
        ///  randomly decides whether to inject a fault in this step.
        /// If a fault will be injected, the exception is returned so you can throw it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Exception StepNonThrowing () {
            if (!Enabled || (Countdown-- > 0))
                return null;
            if (RNG.NextDouble() * 100 >= PercentageChance)
                return null;

            Countdown = RNG.Next(MinCountdown, MaxCountdown);
            var ctor = SelectCtor();
            return ctor(GetFaultMessage(2));
        }

        private string GetFaultMessage (int offset) {
            var sf = new StackFrame(offset, false);
            var method = sf.GetMethod();
            return $"Fault injected in {method.DeclaringType.Name}::{method.Name}";
        }

        private Func<string, Exception> SelectCtor () {
            return (ExceptionTypes.Count == 1)
                ? ExceptionTypes[0]
                : ExceptionTypes[RNG.Next(0, ExceptionTypes.Count - 1)];
        }

        public void InjectFault () {
            var ctor = SelectCtor();
            var exc = ctor(GetFaultMessage(3));
            throw exc;
        }
    }

    public class FaultInjectorException : Exception {
        public readonly FaultInjector Parent;

        public FaultInjectorException (FaultInjector parent, string message)
            : base(message ?? "Fault injected") {
            Parent = parent;
        }
    }
}
