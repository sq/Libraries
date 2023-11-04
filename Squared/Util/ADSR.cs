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

    public struct ADSRModes {
        public ADSRRampMode Attack, Decay, Release;

        public ADSRModes (ADSRRampMode mode) {
            Attack = Decay = Release = mode;
        }

        public ADSRModes (ADSRRampMode attack, ADSRRampMode decay, ADSRRampMode release) {
            Attack = attack;
            Decay = decay;
            Release = release;
        }

        public static implicit operator ADSRModes (ADSRRampMode mode) =>
            new ADSRModes { Attack = mode, Decay = mode, Release = mode };

        public override int GetHashCode () {
            // FIXME
            return 0;
        }

        public bool Equals (ADSRModes rhs) {
            return (Attack == rhs.Attack) &&
                (Decay == rhs.Decay) &&
                (Release == rhs.Release);
        }

        public override bool Equals (object obj) {
            if (obj is ADSRModes ms)
                return Equals(ms);
            else
                return false;
        }

        public override string ToString () {
            if ((Attack == Decay) && (Decay == Release))
                return $"ADSRModes({Attack})";
            else
                return $"ADSRModes(attack={Attack}, decay={Decay}, release={Release})";
        }
    }

    public struct ADSRInvert {
        public bool Value, Attack, Decay, Release;

        public ADSRInvert (bool value, bool attack, bool decay, bool release) {
            Value = value;
            Attack = attack;
            Decay = decay;
            Release = release;
        }

        public override int GetHashCode () {
            // FIXME
            return 0;
        }

        public bool Equals (ADSRInvert rhs) {
            return (Value == rhs.Value) &&
                (Attack == rhs.Attack) &&
                (Decay == rhs.Decay) &&
                (Release == rhs.Release);
        }

        public override bool Equals (object obj) {
            if (obj is ADSRInvert ifl)
                return Equals(ifl);
            else
                return false;
        }

        public override string ToString () {
            var sb = new StringBuilder();
            sb.Append("ADSRInvert(");
            sb.Append(Value ? "true, " : "false, ");
            sb.Append(Attack ? "true, " : "false, ");
            sb.Append(Decay ? "true, " : "false, ");
            sb.Append(Release ? "true)" : "false)");
            return sb.ToString();
        }
    }

    public struct ADSR {
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

        public ADSRModes Modes;
        public ADSRInvert Inverse;
        private double RampExponentMinus2;

        public ADSR (
            double attack, double decay, 
            ADSRRampMode mode = ADSRRampMode.Linear, double exponent = 2.0,
            ADSRInvert inverse = default
        ) : this(attack, decay, 0, 0, (ADSRModes)mode, exponent, inverse) {
        }

        public ADSR (
            double attack, double decay, double sustain, double release, 
            ADSRRampMode mode = ADSRRampMode.Linear, double exponent = 2.0,
            ADSRInvert inverse = default
        ) : this(attack, decay, sustain, release, (ADSRModes)mode, exponent, inverse) {
        }

        public ADSR (
            double attack, double decay, double sustain, double release, 
            ADSRModes modes, double exponent = 2.0,
            ADSRInvert inverse = default
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
                attack = ((Attack > 0) && enableAttack) ? Ramp(seconds / Attack, Modes.Attack, Inverse.Attack) : 1,
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
                (Release == rhs.Release) &&
                Modes.Equals(rhs.Modes);
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
            if (Inverse.Equals(default(ADSRInvert)))
                return $"ADSR({Attack}, {Decay}, {Sustain}, {Release}, modes={Modes}, exponent={RampExponent})";
            else
                return $"ADSR({Attack}, {Decay}, {Sustain}, {Release}, modes={Modes}, exponent={RampExponent}, inverse={Inverse})";
        }

        public double GetMinimumDuration (double holdDuration = 0) {
            return Math.Max(Attack, holdDuration) + Decay + Release;
        }
    }
}
