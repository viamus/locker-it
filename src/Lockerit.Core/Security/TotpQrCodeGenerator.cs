using System.Text;

namespace Lockerit.Core.Security;

public static class TotpQrCodeGenerator
{
    public static QrCodeMatrix CreateMatrix(string setupUri)
    {
        if (string.IsNullOrWhiteSpace(setupUri))
        {
            throw new ArgumentException("Setup URI is required.", nameof(setupUri));
        }

        var data = Encoding.UTF8.GetBytes(setupUri);
        var version = ChooseVersion(data.Length);
        var qr = new QrBuilder(version);
        qr.AddData(CreateCodewords(data, version));
        qr.ApplyBestMask();
        return qr.ToMatrix();
    }

    private static int ChooseVersion(int byteLength)
    {
        for (var version = 1; version <= QrTables.MaxVersion; version++)
        {
            var countBits = version <= 9 ? 8 : 16;
            var requiredBits = 4 + countBits + byteLength * 8;
            if (requiredBits <= QrTables.DataCodewords[version] * 8)
            {
                return version;
            }
        }

        throw new InvalidOperationException("The authenticator setup URI is too large for the built-in QR generator.");
    }

    private static byte[] CreateCodewords(byte[] data, int version)
    {
        var capacityBits = QrTables.DataCodewords[version] * 8;
        var bits = new BitBuffer();

        bits.Append(0b0100, 4);
        bits.Append(data.Length, version <= 9 ? 8 : 16);
        foreach (var value in data)
        {
            bits.Append(value, 8);
        }

        bits.Append(0, Math.Min(4, capacityBits - bits.Length));
        bits.Append(0, (8 - bits.Length % 8) % 8);

        var padByte = 0xEC;
        while (bits.Length < capacityBits)
        {
            bits.Append(padByte, 8);
            padByte ^= 0xEC ^ 0x11;
        }

        return bits.ToBytes();
    }

    private sealed class QrBuilder
    {
        private readonly int _version;
        private readonly int _size;
        private readonly bool[,] _modules;
        private readonly bool[,] _isFunction;

        public QrBuilder(int version)
        {
            _version = version;
            _size = version * 4 + 17;
            _modules = new bool[_size, _size];
            _isFunction = new bool[_size, _size];

            DrawFunctionPatterns();
        }

        public void AddData(byte[] dataCodewords)
        {
            var allCodewords = AddErrorCorrectionAndInterleave(dataCodewords, _version);
            var bitIndex = 0;
            var upward = true;

            for (var right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right--;
                }

                for (var vertical = 0; vertical < _size; vertical++)
                {
                    var y = upward ? _size - 1 - vertical : vertical;
                    for (var column = 0; column < 2; column++)
                    {
                        var x = right - column;
                        if (_isFunction[y, x])
                        {
                            continue;
                        }

                        var dark = bitIndex < allCodewords.Length * 8 &&
                            ((allCodewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        _modules[y, x] = dark;
                        bitIndex++;
                    }
                }

                upward = !upward;
            }
        }

        public void ApplyBestMask()
        {
            var bestMask = 0;
            var bestPenalty = int.MaxValue;
            var bestModules = (bool[,])_modules.Clone();

            for (var mask = 0; mask < 8; mask++)
            {
                var candidate = (bool[,])_modules.Clone();
                ApplyMask(candidate, mask);
                DrawFormatBits(candidate, mask);

                var penalty = CalculatePenalty(candidate);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                    bestModules = candidate;
                }
            }

            CopyModules(bestModules, _modules);
            DrawFormatBits(_modules, bestMask);
        }

        public QrCodeMatrix ToMatrix()
        {
            return new QrCodeMatrix((bool[,])_modules.Clone());
        }

        private void DrawFunctionPatterns()
        {
            DrawFinderPattern(3, 3);
            DrawFinderPattern(_size - 4, 3);
            DrawFinderPattern(3, _size - 4);

            var alignments = QrTables.AlignmentPatternPositions[_version];
            foreach (var x in alignments)
            {
                foreach (var y in alignments)
                {
                    var overlapsFinder =
                        x <= 8 && y <= 8 ||
                        x >= _size - 9 && y <= 8 ||
                        x <= 8 && y >= _size - 9;
                    if (!overlapsFinder)
                    {
                        DrawAlignmentPattern(x, y);
                    }
                }
            }

            for (var i = 0; i < _size; i++)
            {
                if (!_isFunction[6, i])
                {
                    SetFunctionModule(i, 6, i % 2 == 0);
                }

                if (!_isFunction[i, 6])
                {
                    SetFunctionModule(6, i, i % 2 == 0);
                }
            }

            DrawFormatBits(_modules, mask: 0);
            SetFunctionModule(8, _size - 8, true);

            if (_version >= 7)
            {
                DrawVersionBits();
            }
        }

        private void DrawFinderPattern(int centerX, int centerY)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;
                    if (x < 0 || y < 0 || x >= _size || y >= _size)
                    {
                        continue;
                    }

                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(x, y, distance is 0 or 1 or 3);
                }
            }
        }

        private void DrawAlignmentPattern(int centerX, int centerY)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                for (var dx = -2; dx <= 2; dx++)
                {
                    var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunctionModule(centerX + dx, centerY + dy, distance != 1);
                }
            }
        }

        private void DrawVersionBits()
        {
            var bits = CalculateVersionBits(_version);
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                var a = _size - 11 + i % 3;
                var b = i / 3;
                SetFunctionModule(a, b, bit);
                SetFunctionModule(b, a, bit);
            }
        }

        private void DrawFormatBits(bool[,] target, int mask)
        {
            var bits = CalculateFormatBits(mask);

            for (var i = 0; i <= 5; i++)
            {
                SetModule(target, 8, i, ((bits >> i) & 1) != 0, function: true);
            }

            SetModule(target, 8, 7, ((bits >> 6) & 1) != 0, function: true);
            SetModule(target, 8, 8, ((bits >> 7) & 1) != 0, function: true);
            SetModule(target, 7, 8, ((bits >> 8) & 1) != 0, function: true);

            for (var i = 9; i < 15; i++)
            {
                SetModule(target, 14 - i, 8, ((bits >> i) & 1) != 0, function: true);
            }

            for (var i = 0; i < 8; i++)
            {
                SetModule(target, _size - 1 - i, 8, ((bits >> i) & 1) != 0, function: true);
            }

            for (var i = 8; i < 15; i++)
            {
                SetModule(target, 8, _size - 15 + i, ((bits >> i) & 1) != 0, function: true);
            }

            SetModule(target, 8, _size - 8, true, function: true);
        }

        private void SetFunctionModule(int x, int y, bool dark)
        {
            SetModule(_modules, x, y, dark, function: true);
        }

        private void SetModule(bool[,] target, int x, int y, bool dark, bool function)
        {
            target[y, x] = dark;
            if (function)
            {
                _isFunction[y, x] = true;
            }
        }

        private void ApplyMask(bool[,] target, int mask)
        {
            for (var y = 0; y < _size; y++)
            {
                for (var x = 0; x < _size; x++)
                {
                    if (!_isFunction[y, x] && MaskBit(mask, x, y))
                    {
                        target[y, x] = !target[y, x];
                    }
                }
            }
        }

        private static bool MaskBit(int mask, int x, int y)
        {
            return mask switch
            {
                0 => (x + y) % 2 == 0,
                1 => y % 2 == 0,
                2 => x % 3 == 0,
                3 => (x + y) % 3 == 0,
                4 => (x / 3 + y / 2) % 2 == 0,
                5 => (x * y) % 2 + (x * y) % 3 == 0,
                6 => ((x * y) % 2 + (x * y) % 3) % 2 == 0,
                7 => ((x + y) % 2 + (x * y) % 3) % 2 == 0,
                _ => throw new ArgumentOutOfRangeException(nameof(mask))
            };
        }

        private int CalculatePenalty(bool[,] target)
        {
            var penalty = 0;

            for (var y = 0; y < _size; y++)
            {
                penalty += CalculateLinePenalty(target, y, horizontal: true);
            }

            for (var x = 0; x < _size; x++)
            {
                penalty += CalculateLinePenalty(target, x, horizontal: false);
            }

            for (var y = 0; y < _size - 1; y++)
            {
                for (var x = 0; x < _size - 1; x++)
                {
                    var color = target[y, x];
                    if (target[y, x + 1] == color &&
                        target[y + 1, x] == color &&
                        target[y + 1, x + 1] == color)
                    {
                        penalty += 3;
                    }
                }
            }

            var dark = 0;
            foreach (var module in target)
            {
                if (module)
                {
                    dark++;
                }
            }

            var total = _size * _size;
            var k = Math.Abs(dark * 20 - total * 10) / total;
            penalty += k * 10;

            return penalty;
        }

        private int CalculateLinePenalty(bool[,] target, int index, bool horizontal)
        {
            var penalty = 0;
            var runColor = horizontal ? target[index, 0] : target[0, index];
            var runLength = 1;

            for (var i = 1; i < _size; i++)
            {
                var color = horizontal ? target[index, i] : target[i, index];
                if (color == runColor)
                {
                    runLength++;
                    if (runLength == 5)
                    {
                        penalty += 3;
                    }
                    else if (runLength > 5)
                    {
                        penalty++;
                    }
                }
                else
                {
                    runColor = color;
                    runLength = 1;
                }
            }

            return penalty;
        }

        private static int CalculateFormatBits(int mask)
        {
            var data = (1 << 3) | mask;
            var remainder = data;
            for (var i = 0; i < 10; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            return ((data << 10) | remainder) ^ 0x5412;
        }

        private static int CalculateVersionBits(int version)
        {
            var remainder = version;
            for (var i = 0; i < 12; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
            }

            return (version << 12) | remainder;
        }

        private static byte[] AddErrorCorrectionAndInterleave(byte[] dataCodewords, int version)
        {
            var totalCodewords = QrTables.TotalCodewords[version];
            var blockCount = QrTables.ErrorCorrectionBlocks[version];
            var eccLength = QrTables.ErrorCorrectionCodewordsPerBlock[version];
            var shortBlockLength = totalCodewords / blockCount;
            var longBlockCount = totalCodewords % blockCount;
            var shortBlockCount = blockCount - longBlockCount;
            var shortDataLength = shortBlockLength - eccLength;

            var blocks = new List<Block>(blockCount);
            var offset = 0;
            for (var i = 0; i < blockCount; i++)
            {
                var dataLength = shortDataLength + (i >= shortBlockCount ? 1 : 0);
                var data = dataCodewords.AsSpan(offset, dataLength).ToArray();
                offset += dataLength;
                blocks.Add(new Block(data, ReedSolomon.ComputeRemainder(data, eccLength)));
            }

            var result = new List<byte>(totalCodewords);
            var maxDataLength = blocks.Max(block => block.Data.Length);
            for (var i = 0; i < maxDataLength; i++)
            {
                foreach (var block in blocks)
                {
                    if (i < block.Data.Length)
                    {
                        result.Add(block.Data[i]);
                    }
                }
            }

            for (var i = 0; i < eccLength; i++)
            {
                foreach (var block in blocks)
                {
                    result.Add(block.ErrorCorrection[i]);
                }
            }

            return result.ToArray();
        }

        private static void CopyModules(bool[,] source, bool[,] target)
        {
            for (var y = 0; y < source.GetLength(0); y++)
            {
                for (var x = 0; x < source.GetLength(1); x++)
                {
                    target[y, x] = source[y, x];
                }
            }
        }

        private sealed record Block(byte[] Data, byte[] ErrorCorrection);
    }

    private sealed class BitBuffer
    {
        private readonly List<int> _bits = [];

        public int Length => _bits.Count;

        public void Append(int value, int length)
        {
            if (length < 0 || length > 31 || value >> length != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            for (var i = length - 1; i >= 0; i--)
            {
                _bits.Add((value >> i) & 1);
            }
        }

        public byte[] ToBytes()
        {
            var result = new byte[(_bits.Count + 7) / 8];
            for (var i = 0; i < _bits.Count; i++)
            {
                result[i >> 3] |= (byte)(_bits[i] << (7 - (i & 7)));
            }

            return result;
        }
    }

    private static class ReedSolomon
    {
        public static byte[] ComputeRemainder(byte[] data, int degree)
        {
            var generator = CreateGenerator(degree);
            var result = new byte[degree];

            foreach (var value in data)
            {
                var factor = value ^ result[0];
                Array.Copy(result, 1, result, 0, degree - 1);
                result[^1] = 0;

                for (var i = 0; i < result.Length; i++)
                {
                    result[i] ^= Multiply(generator[i], factor);
                }
            }

            return result;
        }

        private static byte[] CreateGenerator(int degree)
        {
            var generator = new List<byte> { 1 };
            var root = 1;

            for (var i = 0; i < degree; i++)
            {
                var next = new byte[generator.Count + 1];
                for (var j = 0; j < generator.Count; j++)
                {
                    next[j] ^= Multiply(generator[j], 1);
                    next[j + 1] ^= Multiply(generator[j], root);
                }

                generator = next.ToList();
                root = Multiply(root, 0x02);
            }

            return generator.Skip(1).ToArray();
        }

        private static byte Multiply(int x, int y)
        {
            var result = 0;
            while (y != 0)
            {
                if ((y & 1) != 0)
                {
                    result ^= x;
                }

                x <<= 1;
                if ((x & 0x100) != 0)
                {
                    x ^= 0x11D;
                }

                y >>= 1;
            }

            return (byte)result;
        }
    }

    private static class QrTables
    {
        public const int MaxVersion = 10;

        public static readonly int[] DataCodewords =
        [
            0, 19, 34, 55, 80, 108, 136, 156, 194, 232, 274
        ];

        public static readonly int[] TotalCodewords =
        [
            0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346
        ];

        public static readonly int[] ErrorCorrectionCodewordsPerBlock =
        [
            0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18
        ];

        public static readonly int[] ErrorCorrectionBlocks =
        [
            0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4
        ];

        public static readonly int[][] AlignmentPatternPositions =
        [
            [],
            [],
            [6, 18],
            [6, 22],
            [6, 26],
            [6, 30],
            [6, 34],
            [6, 22, 38],
            [6, 24, 42],
            [6, 26, 46],
            [6, 28, 50]
        ];
    }
}
