using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util {
    public enum ADSRRampMode : byte {
        Linear = 0,
        Exponential = 1,
        Cosine = 2
    }

    public struct ADSR {
        public struct ModeSet {
            public ADSRRampMode Attack, Decay, Release;

            public static implicit operator ModeSet (ADSRRampMode mode) =>
                new ModeSet { Attack = mode, Decay = mode, Release = mode };
        }

        public struct InverseFlags {
            public bool Value, Attack, Decay, Release;
        }

        /// <summary>
        /// The amount of time the envelope takes to reach 1.0 from 0.0
        /// </summary>
        public double Attack;
        /// <summary>
        /// The amount of time the envelope takes to reach <seealso cref="Sustain"/> from 1.0
        /// </summary>
        public double Decay;
        /// <summary>
        /// The value that the envelope holds at after decay starts and before release begins
        /// </summary>
        public double Sustain;
        /// <summary>
        /// The amount of time the value takes to return to 0 
        /// </summary>
        public double Release;

        public ModeSet Modes;
        public InverseFlags Inverse;
        private double RampExponentMinus2;

        public ADSR (
            double attack, double decay, double sustain, double release, 
            ADSRRampMode mode = ADSRRampMode.Linear, double exponent = 2.0,
            InverseFlags inverse = default
        ) : this(attack, decay, sustain, release, (ModeSet)mode, exponent, inverse) {
        }

        public ADSR (
            double attack, double decay, double sustain, double release, 
            ModeSet modes, double exponent = 2.0,
            InverseFlags inverse = default
        ) : this() {
            Attack = attack;
            Decay = decay;
            Sustain = sustain;
            Release = release;
            Modes = modes;
            RampExponent = exponent;
            Inverse = inverse;
        }

        public ADSRRampMode Mode {
            set => Modes = value;
        }

        public double RampExponent {
            get => RampExponentMinus2 + 2;
            set => RampExponentMinus2 = value - 2;
        }

        public double Ramp (double input, ADSRRampMode mode, bool inverse) {
            double result;

            input = Arithmetic.Saturate(input);
            // HACK: This gives a typical ADSR ramp as far as I can tell
            if (!inverse)
                input = 1.0 - input;

            switch (mode) {
                case ADSRRampMode.Cosine:
                    result = (1.0 - Math.Cos(input * Math.PI)) * 0.5;
                    break;
                case ADSRRampMode.Exponential:
                    result = Math.Pow(input, RampExponentMinus2 + 2);
                    break;
                case ADSRRampMode.Linear:
                default:
                    result = input;
                    break;
            }

            if (!inverse)
                return 1.0 - result;
            else
                return result;
        }

        public double Eval (double seconds, double duration = 0.0, bool enableAttack = true, bool enableDecay = true, bool enableRelease = true) {
            if (Equals(default))
                return Inverse.Value ? 0 : 1;

            duration = Math.Max(duration, Attack + Decay);

            double releaseTime = seconds - duration + Release,
                // A normal exponential ADSR ramp slopes up slowly and then slopes down fast
                attack = ((Attack > 0) && enableAttack) ? Ramp(seconds / Attack, Modes.Attack, Inverse.Attack) : 0,
                decay = ((Decay > 0) && enableDecay) ? Ramp((seconds - Attack) / Decay, Modes.Decay, Inverse.Decay) : 0,
                release = ((Release > 0) && enableRelease) ? Ramp(releaseTime / Release, Modes.Release, Inverse.Release) : 0,
                decayed = Arithmetic.Lerp(1, Sustain, decay),
                result = Math.Min(attack, decayed);
            if (releaseTime >= 0)
                result = Math.Min(result, Arithmetic.Lerp(Sustain, 0, release));

            return Inverse.Value ? 1.0 - result : result;
        }

        public bool Equals (ADSR rhs) {
            return (Attack == rhs.Attack) &&
                (Decay == rhs.Decay) &&
                (Sustain == rhs.Sustain) &&
                (Release == rhs.Release);
        }

        public override bool Equals (object obj) {
            if (obj is ADSR adsr)
                return Equals(adsr);
            else
                return false;
        }

        public override int GetHashCode () {
            // FIXME
            return 0;
        }

        public override string ToString () {
            return $"ADSR({Attack}, {Decay}, {Sustain}, {Release})";
        }

        public double GetMinimumDuration (double holdDuration = 0) {
            return Math.Max(Attack, holdDuration) + Decay + Release;
        }
    }
}
