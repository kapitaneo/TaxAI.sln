using ClientIntake.Models;

namespace ClientIntake.Services;

internal static class IntakeUploadValidator
{
    private const int SignatureBufferLength = 8;

    internal static async Task<Stream?> ValidateAsync(
        IntakeUploadCommand command,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (command is null ||
            content is null ||
            !content.CanRead ||
            command.DeclaredLength is null or <= 0 ||
            command.DeclaredLength > maximumBytes ||
            command.DeclaredMediaKind is null ||
            !Enum.IsDefined(command.DeclaredMediaKind.Value))
        {
            return null;
        }

        var prefix = new byte[SignatureBufferLength];
        var read = 0;
        while (read < prefix.Length)
        {
            var count = await content.ReadAsync(
                prefix.AsMemory(read, prefix.Length - read),
                cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (!Matches(command.DeclaredMediaKind.Value, prefix.AsSpan(0, read)))
        {
            return null;
        }

        return new PrefixStream(prefix.AsMemory(0, read).ToArray(), content);
    }

    private static bool Matches(IntakeMediaKind kind, ReadOnlySpan<byte> prefix) =>
        kind switch
        {
            IntakeMediaKind.Pdf =>
                prefix.Length >= 5 &&
                prefix[0] == (byte)'%' &&
                prefix[1] == (byte)'P' &&
                prefix[2] == (byte)'D' &&
                prefix[3] == (byte)'F' &&
                prefix[4] == (byte)'-',
            IntakeMediaKind.Jpeg =>
                prefix.Length >= 3 &&
                prefix[0] == 0xff &&
                prefix[1] == 0xd8 &&
                prefix[2] == 0xff,
            IntakeMediaKind.Png =>
                prefix.Length >= 8 &&
                prefix[..8].SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            _ => false,
        };

    private sealed class PrefixStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly Stream _remainder;
        private int _offset;

        internal PrefixStream(byte[] prefix, Stream remainder)
        {
            _prefix = prefix;
            _remainder = remainder;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = CopyPrefix(buffer.AsSpan(offset, count));
            return copied == count
                ? copied
                : copied + _remainder.Read(buffer, offset + copied, count - copied);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var copied = CopyPrefix(buffer.Span);
            return copied == buffer.Length
                ? copied
                : copied + await _remainder.ReadAsync(
                    buffer[copied..],
                    cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int CopyPrefix(Span<byte> destination)
        {
            var remaining = _prefix.Length - _offset;
            var count = Math.Min(remaining, destination.Length);
            if (count > 0)
            {
                _prefix.AsSpan(_offset, count).CopyTo(destination);
                _offset += count;
            }

            return count;
        }
    }
}
