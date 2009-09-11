using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Xml;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Globalization;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Squared.Game.Serialization {
    public class ColorSerializer : IValueSerializer {
        public object Read (byte[] data) {
            return new Color(data[0], data[1], data[2], data[3]);
        }

        public byte[] Write (object instance) {
            var inst = (Color)instance;
            var result = new byte[4];
            result[0] = inst.R;
            result[1] = inst.G;
            result[2] = inst.B;
            result[3] = inst.A;
            return result;
        }
    }

    public class Vector2Serializer : IValueSerializer {
        public object Read (byte[] data) {
            if (!BitConverter.IsLittleEndian) {
                Array.Reverse(data, 0, 4);
                Array.Reverse(data, 4, 4);
            }

            return new Vector2(
                BitConverter.ToSingle(data, 0),
                BitConverter.ToSingle(data, 4)
            );
        }

        public byte[] Write (object instance) {
            var inst = (Vector2)instance;
            var result = new byte[8];
            Array.Copy(BitConverter.GetBytes(inst.X), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(inst.Y), 0, result, 4, 4);
            return result;
        }
    }

    public class Vector3Serializer : IValueSerializer {
        public object Read (byte[] data) {
            if (!BitConverter.IsLittleEndian) {
                Array.Reverse(data, 0, 4);
                Array.Reverse(data, 4, 4);
                Array.Reverse(data, 8, 4);
            }

            return new Vector3(
                BitConverter.ToSingle(data, 0),
                BitConverter.ToSingle(data, 4),
                BitConverter.ToSingle(data, 8)
            );
        }

        public byte[] Write (object instance) {
            var inst = (Vector3)instance;
            var result = new byte[12];
            Array.Copy(BitConverter.GetBytes(inst.X), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(inst.Y), 0, result, 4, 4);
            Array.Copy(BitConverter.GetBytes(inst.Z), 0, result, 8, 4);
            return result;
        }
    }

    public class Vector4Serializer : IValueSerializer {
        public object Read (byte[] data) {
            if (!BitConverter.IsLittleEndian) {
                Array.Reverse(data, 0, 4);
                Array.Reverse(data, 4, 4);
                Array.Reverse(data, 8, 4);
                Array.Reverse(data, 12, 4);
            }

            return new Vector4(
                BitConverter.ToSingle(data, 0),
                BitConverter.ToSingle(data, 4),
                BitConverter.ToSingle(data, 8),
                BitConverter.ToSingle(data, 12)
            );
        }

        public byte[] Write (object instance) {
            var inst = (Vector4)instance;
            var result = new byte[16];
            Array.Copy(BitConverter.GetBytes(inst.X), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(inst.Y), 0, result, 4, 4);
            Array.Copy(BitConverter.GetBytes(inst.Z), 0, result, 8, 4);
            Array.Copy(BitConverter.GetBytes(inst.W), 0, result, 12, 4);
            return result;
        }
    }

    public class PointSerializer : IValueSerializer {
        public object Read (byte[] data) {
            if (!BitConverter.IsLittleEndian) {
                Array.Reverse(data, 0, 4);
                Array.Reverse(data, 4, 4);
            }

            return new Point(
                BitConverter.ToInt32(data, 0),
                BitConverter.ToInt32(data, 4)
            );
        }

        public byte[] Write (object instance) {
            var inst = (Point)instance;
            var result = new byte[8];
            Array.Copy(BitConverter.GetBytes(inst.X), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(inst.Y), 0, result, 4, 4);
            return result;
        }
    }

    public class BoundsSerializer : IValueSerializer {
        public object Read (byte[] data) {
            if (!BitConverter.IsLittleEndian) {
                Array.Reverse(data, 0, 4);
                Array.Reverse(data, 4, 4);
                Array.Reverse(data, 8, 4);
                Array.Reverse(data, 12, 4);
            }

            return new Bounds(
                new Vector2(
                    BitConverter.ToSingle(data, 0),
                    BitConverter.ToSingle(data, 4)
                ),
                new Vector2(
                    BitConverter.ToSingle(data, 8),
                    BitConverter.ToSingle(data, 12)
                )
            );
        }

        public byte[] Write (object instance) {
            var inst = (Bounds)instance;
            var result = new byte[16];
            Array.Copy(BitConverter.GetBytes(inst.TopLeft.X), 0, result, 0, 4);
            Array.Copy(BitConverter.GetBytes(inst.TopLeft.Y), 0, result, 4, 4);
            Array.Copy(BitConverter.GetBytes(inst.BottomRight.X), 0, result, 8, 4);
            Array.Copy(BitConverter.GetBytes(inst.BottomRight.Y), 0, result, 12, 4);
            return result;
        }
    }
}
