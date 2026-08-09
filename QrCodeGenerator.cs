namespace AndroidApp1
{
    internal static class QrCodeGenerator
    {
        private const int Size = 21;

        public static Android.Graphics.Bitmap Create(string payload, int pixelSize)
        {
            var modules = CreateModules(payload);
            var scale = Math.Max(1, pixelSize / Size);
            var bitmap = Android.Graphics.Bitmap.CreateBitmap(Size * scale, Size * scale, Android.Graphics.Bitmap.Config.Argb8888!)!;
            using var canvas = new Android.Graphics.Canvas(bitmap);
            canvas.DrawColor(Android.Graphics.Color.White);
            using var paint = new Android.Graphics.Paint { Color = Android.Graphics.Color.Black };

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (modules[y, x])
                    {
                        canvas.DrawRect(x * scale, y * scale, (x + 1) * scale, (y + 1) * scale, paint);
                    }
                }
            }

            return bitmap;
        }

        private static bool[,] CreateModules(string payload)
        {
            var modules = new bool[Size, Size];
            var functions = new bool[Size, Size];
            DrawFinderPattern(modules, functions, 0, 0);
            DrawFinderPattern(modules, functions, Size - 7, 0);
            DrawFinderPattern(modules, functions, 0, Size - 7);

            for (var i = 8; i < Size - 8; i++)
            {
                modules[6, i] = i % 2 == 0;
                modules[i, 6] = i % 2 == 0;
                functions[6, i] = true;
                functions[i, 6] = true;
            }

            ReserveFormatAreas(functions);
            var codewords = CreateCodewords(payload);
            PlaceData(modules, functions, codewords);
            ApplyMaskZero(modules, functions);
            DrawFormatBits(modules);
            return modules;
        }

        private static void DrawFinderPattern(bool[,] modules, bool[,] functions, int left, int top)
        {
            for (var y = -1; y <= 7; y++)
            {
                for (var x = -1; x <= 7; x++)
                {
                    var row = top + y;
                    var column = left + x;
                    if (row < 0 || row >= Size || column < 0 || column >= Size)
                    {
                        continue;
                    }

                    var dark = x >= 0 && x <= 6 && y >= 0 && y <= 6 &&
                        (x == 0 || x == 6 || y == 0 || y == 6 || (x >= 2 && x <= 4 && y >= 2 && y <= 4));
                    modules[row, column] = dark;
                    functions[row, column] = true;
                }
            }
        }

        private static void ReserveFormatAreas(bool[,] functions)
        {
            for (var i = 0; i <= 8; i++)
            {
                if (i != 6)
                {
                    functions[8, i] = true;
                    functions[i, 8] = true;
                }
            }

            for (var i = 0; i < 8; i++)
            {
                functions[Size - 1 - i, 8] = true;
                functions[8, Size - 1 - i] = true;
            }

            functions[Size - 8, 8] = true;
        }

        private static byte[] CreateCodewords(string payload)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(payload);
            if (bytes.Length > 17)
            {
                throw new ArgumentException("Ticket QR payload is too long.", nameof(payload));
            }

            var bits = new List<bool>();
            AppendBits(bits, 0b0100, 4);
            AppendBits(bits, bytes.Length, 8);
            foreach (var value in bytes)
            {
                AppendBits(bits, value, 8);
            }

            var dataCapacityBits = 19 * 8;
            for (var index = 0; index < Math.Min(4, dataCapacityBits - bits.Count); index++)
            {
                bits.Add(false);
            }
            while (bits.Count % 8 != 0)
            {
                bits.Add(false);
            }

            var data = new List<byte>();
            for (var index = 0; index < bits.Count; index += 8)
            {
                byte value = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    if (bits[index + bit])
                    {
                        value |= (byte)(1 << (7 - bit));
                    }
                }
                data.Add(value);
            }

            var pads = new byte[] { 0xEC, 0x11 };
            for (var index = 0; data.Count < 19; index++)
            {
                data.Add(pads[index % pads.Length]);
            }

            var ecc = CalculateErrorCorrection(data.ToArray(), 7);
            return data.Concat(ecc).ToArray();
        }

        private static void AppendBits(List<bool> bits, int value, int count)
        {
            for (var index = count - 1; index >= 0; index--)
            {
                bits.Add(((value >> index) & 1) != 0);
            }
        }

        private static byte[] CalculateErrorCorrection(byte[] data, int degree)
        {
            var generator = new List<byte> { 1 };
            for (var index = 0; index < degree; index++)
            {
                var next = new byte[generator.Count + 1];
                for (var coefficient = 0; coefficient < generator.Count; coefficient++)
                {
                    next[coefficient] ^= generator[coefficient];
                    next[coefficient + 1] ^= Multiply(generator[coefficient], Power(index));
                }
                generator = next.ToList();
            }

            var remainder = new byte[degree];
            foreach (var value in data)
            {
                var factor = (byte)(value ^ remainder[0]);
                for (var index = 0; index < degree - 1; index++)
                {
                    remainder[index] = remainder[index + 1];
                }
                remainder[degree - 1] = 0;
                for (var index = 0; index < degree; index++)
                {
                    remainder[index] ^= Multiply(generator[index + 1], factor);
                }
            }

            return remainder;
        }

        private static byte Multiply(byte left, byte right)
        {
            var result = 0;
            var a = (int)left;
            var b = (int)right;
            while (b != 0)
            {
                if ((b & 1) != 0)
                {
                    result ^= a;
                }
                a <<= 1;
                if ((a & 0x100) != 0)
                {
                    a ^= 0x11D;
                }
                b >>= 1;
            }

            return (byte)result;
        }

        private static byte Power(int exponent)
        {
            byte value = 1;
            for (var index = 0; index < exponent; index++)
            {
                value = Multiply(value, 2);
            }

            return value;
        }

        private static void PlaceData(bool[,] modules, bool[,] functions, byte[] codewords)
        {
            var bitIndex = 0;
            var upward = true;
            for (var right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right--;
                }

                for (var offset = 0; offset < Size; offset++)
                {
                    var row = upward ? Size - 1 - offset : offset;
                    for (var columnOffset = 0; columnOffset < 2; columnOffset++)
                    {
                        var column = right - columnOffset;
                        if (functions[row, column])
                        {
                            continue;
                        }

                        var bit = bitIndex < codewords.Length * 8 &&
                            ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        modules[row, column] = bit;
                        bitIndex++;
                    }
                }

                upward = !upward;
            }
        }

        private static void ApplyMaskZero(bool[,] modules, bool[,] functions)
        {
            for (var row = 0; row < Size; row++)
            {
                for (var column = 0; column < Size; column++)
                {
                    if (!functions[row, column] && (row + column) % 2 == 0)
                    {
                        modules[row, column] = !modules[row, column];
                    }
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules)
        {
            const int formatBits = 0x77C4;
            for (var index = 0; index <= 5; index++)
            {
                modules[index, 8] = ((formatBits >> index) & 1) != 0;
            }
            modules[7, 8] = ((formatBits >> 6) & 1) != 0;
            modules[8, 8] = ((formatBits >> 7) & 1) != 0;
            modules[8, 7] = ((formatBits >> 8) & 1) != 0;
            for (var index = 9; index < 15; index++)
            {
                modules[8, 14 - index] = ((formatBits >> index) & 1) != 0;
            }

            for (var index = 0; index < 8; index++)
            {
                modules[Size - 1 - index, 8] = ((formatBits >> index) & 1) != 0;
            }
            for (var index = 8; index < 15; index++)
            {
                modules[8, Size - 15 + index] = ((formatBits >> index) & 1) != 0;
            }
            modules[Size - 8, 8] = true;
        }
    }
}
